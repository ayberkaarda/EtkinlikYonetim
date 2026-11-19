using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class RegistrationRepository(IDbContextFactory<EventDbContext> contextFactory) : IRegistrationRepository
{
    public async Task<int> AddAsync(
        int participantId,
        int eventId,
        DateOnly today,
        DateTime createdAtUtc,
        string ticketCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketCode);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        if (await IsActivelyRegisteredAsync(db, participantId, eventId, cancellationToken).ConfigureAwait(false))
        {
            throw new AlreadyRegisteredException();
        }

        // Takes a seat. The UPDATE locks the event row until the transaction ends, so concurrent
        // registrations for the same event are serialized here and each one sees the seats taken by
        // those that committed before it; when no seat is left (or the event is past) nothing matches.
        var reserved = await db.Events
            .Where(e => e.Id == eventId && e.Date >= today && e.RegisteredCount < e.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount + 1), cancellationToken)
            .ConfigureAwait(false);
        if (reserved != 1)
        {
            var date = await db.Events
                .Where(e => e.Id == eventId)
                .Select(e => (DateOnly?)e.Date)
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (date is null || date < today)
            {
                throw new EventClosedException();
            }

            throw new EventFullException();
        }

        var registration = new Registration
        {
            ParticipantId = participantId,
            EventId = eventId,
            CreatedAt = createdAtUtc,
            Status = RegistrationStatus.Active,
            Ticket = new Ticket { Code = ticketCode, IssuedAt = createdAtUtc },
        };
        db.Registrations.Add(registration);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent registration of the same participant committed between the check and the
            // insert and the filtered unique index rejected this one; the rollback also returns the
            // seat. Checked on a fresh context, outside this transaction, so the provider specific
            // error code does not matter.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await using var check = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            if (await IsActivelyRegisteredAsync(check, participantId, eventId, CancellationToken.None).ConfigureAwait(false))
            {
                throw new AlreadyRegisteredException("The participant is already registered for the event.", ex);
            }

            throw;
        }

        return registration.Id;
    }

    public async Task<IReadOnlyList<RegistrationDetails>> GetActiveAsync(int? ownerUserId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var participants = db.Participants.AsQueryable();
        if (ownerUserId is int owner)
        {
            participants = participants.Where(p => p.OwnerUserId == owner);
        }

        return await (
                from r in db.Registrations.AsNoTracking()
                join p in participants on r.ParticipantId equals p.Id
                join e in db.Events on r.EventId equals e.Id
                where r.Status == RegistrationStatus.Active
                orderby e.Date descending, r.Id descending
                select new RegistrationDetails(
                    r.Id,
                    e.Id,
                    e.Name,
                    e.Date,
                    p.Id,
                    p.FullName,
                    r.Ticket != null ? r.Ticket.Code : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<RegistrationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await (
                from r in db.Registrations.AsNoTracking()
                join p in db.Participants on r.ParticipantId equals p.Id
                join e in db.Events on r.EventId equals e.Id
                where r.Id == id
                select new RegistrationOwnership(r.Id, e.Id, e.Date, p.OwnerUserId, r.Status))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> CancelAsync(
        int id,
        int? ownerUserId,
        DateOnly today,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var query = db.Registrations.Where(r => r.Id == id
            && r.Status == RegistrationStatus.Active
            && db.Events.Any(e => e.Id == r.EventId && e.Date >= today));
        if (ownerUserId is int owner)
        {
            query = query.Where(r => db.Participants.Any(p => p.Id == r.ParticipantId && p.OwnerUserId == owner));
        }

        var cancelled = await query
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, RegistrationStatus.Cancelled)
                    .SetProperty(r => r.CancelledAt, cancelledAtUtc),
                cancellationToken)
            .ConfigureAwait(false);
        if (cancelled != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        // Frees the seat in the same transaction; the check constraint keeps the counter from going negative.
        await db.Events
            .Where(e => db.Registrations.Any(r => r.Id == id && r.EventId == e.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount - 1), cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static Task<bool> IsActivelyRegisteredAsync(EventDbContext db, int participantId, int eventId, CancellationToken cancellationToken) =>
        db.Registrations.AnyAsync(
            r => r.ParticipantId == participantId && r.EventId == eventId && r.Status == RegistrationStatus.Active,
            cancellationToken);
}
