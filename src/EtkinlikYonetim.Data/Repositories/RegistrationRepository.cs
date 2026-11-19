using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class RegistrationRepository(IDbContextFactory<EventDbContext> contextFactory) : IRegistrationRepository
{
    public async Task<RegistrationOutcome> AddAsync(
        int participantId,
        int eventId,
        DateOnly today,
        DateTime createdAtUtc,
        Func<string> newTicketCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTicketCode);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Every change of this event's seats and waiting list waits here for the one before it, so the
        // decision between a seat and the waiting list below sees all of them.
        await AppLocks.AcquireAsync(db, AppLocks.Event(eventId), cancellationToken).ConfigureAwait(false);

        if (await HoldsPlaceAsync(db, participantId, eventId, cancellationToken).ConfigureAwait(false))
        {
            throw new AlreadyRegisteredException();
        }

        var date = await db.Events
            .Where(e => e.Id == eventId && !e.Cancelled)
            .Select(e => (DateOnly?)e.Date)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (date is null || date < today)
        {
            throw new EventClosedException();
        }

        // Nobody may get a seat past those already waiting for one. The conditional UPDATE repeats the
        // capacity and date checks, and the check constraint keeps the counter within the capacity, so
        // even a writer that skipped the lock could not overbook the event.
        var reserved = 0;
        if (!await db.Registrations.AnyAsync(r => r.EventId == eventId && r.Status == RegistrationStatus.Waiting, cancellationToken).ConfigureAwait(false))
        {
            reserved = await TakeSeatAsync(db, eventId, today, cancellationToken).ConfigureAwait(false);
        }

        var registration = new Registration
        {
            ParticipantId = participantId,
            EventId = eventId,
            CreatedAt = createdAtUtc,
        };
        if (reserved == 1)
        {
            registration.Status = RegistrationStatus.Active;
            registration.Ticket = NewTicket(newTicketCode(), createdAtUtc);
        }
        else
        {
            registration.Status = RegistrationStatus.Waiting;
            registration.QueueNumber = 1 + (await db.Registrations
                .Where(r => r.EventId == eventId)
                .MaxAsync(r => r.QueueNumber, cancellationToken)
                .ConfigureAwait(false) ?? 0);
        }

        db.Registrations.Add(registration);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            int? position = registration.Status == RegistrationStatus.Waiting
                ? await QueuePositionAsync(db, eventId, registration.QueueNumber!.Value, cancellationToken).ConfigureAwait(false)
                : null;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new RegistrationOutcome(registration.Id, registration.Status, position);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent registration of the same participant that skipped the lock committed between
            // the check and the insert, and the filtered unique index rejected this one; the rollback also
            // returns the seat. Checked on a fresh context, outside this transaction, so the provider
            // specific error code does not matter.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            await using var check = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
            if (await HoldsPlaceAsync(check, participantId, eventId, CancellationToken.None).ConfigureAwait(false))
            {
                throw new AlreadyRegisteredException("The participant is already registered for the event.", ex);
            }

            throw;
        }
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
                where r.Status == RegistrationStatus.Active || r.Status == RegistrationStatus.Waiting || r.Status == RegistrationStatus.EventCancelled
                orderby e.Date descending, r.Id descending
                select new RegistrationDetails(
                    r.Id,
                    e.Id,
                    e.Name,
                    e.Date,
                    p.Id,
                    p.FullName,
                    r.Ticket != null ? r.Ticket.Code : null,
                    r.Status,
                    r.Status == RegistrationStatus.Waiting
                        ? db.Registrations.Count(w => w.EventId == r.EventId && w.Status == RegistrationStatus.Waiting && w.QueueNumber <= r.QueueNumber)
                        : null,
                    p.OwnerUserId))
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
                select new RegistrationOwnership(
                    r.Id,
                    e.Id,
                    e.Date,
                    p.OwnerUserId,
                    r.Status,
                    db.Tickets.Any(t => t.RegistrationId == r.Id && t.CheckedInAt != null)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CancellationOutcome> CancelAsync(
        int id,
        int? ownerUserId,
        DateOnly today,
        DateTime cancelledAtUtc,
        Func<string> newTicketCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTicketCode);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // A registration never moves to another event, so its event can be read before the lock.
        var eventId = await db.Registrations
            .Where(r => r.Id == id)
            .Select(r => (int?)r.EventId)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (eventId is not int lockedEvent)
        {
            return CancellationOutcome.NotCancelled;
        }

        await AppLocks.AcquireAsync(db, AppLocks.Event(lockedEvent), cancellationToken).ConfigureAwait(false);
        var heldSeat = await db.Registrations
            .AnyAsync(r => r.Id == id && r.Status == RegistrationStatus.Active, cancellationToken)
            .ConfigureAwait(false);

        // The ownership, status, date and check-in conditions are part of the UPDATE, so whatever changed
        // since the caller's read is decided here. A participant who has been let in keeps the seat: giving
        // it to the next one waiting would put one person more in the room than the event has seats. The
        // entrance takes the same event lock, so a check-in and a cancellation of one registration are
        // decided one after the other.
        var query = db.Registrations.Where(r => r.Id == id
            && (r.Status == RegistrationStatus.Active || r.Status == RegistrationStatus.Waiting)
            && db.Events.Any(e => e.Id == r.EventId && e.Date >= today)
            && !db.Tickets.Any(t => t.RegistrationId == r.Id && t.CheckedInAt != null));
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
            return CancellationOutcome.NotCancelled;
        }

        var outcome = CancellationOutcome.Cancelled;
        if (heldSeat)
        {
            // Frees the seat; the check constraint keeps the counter from going negative.
            await db.Events
                .Where(e => e.Id == lockedEvent)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount - 1), cancellationToken)
                .ConfigureAwait(false);
            if (await PromoteNextAsync(db, lockedEvent, today, cancelledAtUtc, newTicketCode, cancellationToken).ConfigureAwait(false))
            {
                outcome = CancellationOutcome.CancelledAndPromoted;
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Moves the first waiting registration of the event to a free seat with a new ticket, inside the
    /// caller's transaction and event lock. Returns <see langword="false"/> when nobody waits or no seat is free.
    /// </summary>
    internal static async Task<bool> PromoteNextAsync(
        EventDbContext db,
        int eventId,
        DateOnly today,
        DateTime nowUtc,
        Func<string> newTicketCode,
        CancellationToken cancellationToken)
    {
        var next = await db.Registrations
            .Where(r => r.EventId == eventId && r.Status == RegistrationStatus.Waiting)
            .OrderBy(r => r.QueueNumber)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (next is not int nextId || await TakeSeatAsync(db, eventId, today, cancellationToken).ConfigureAwait(false) != 1)
        {
            return false;
        }

        var promoted = await db.Registrations
            .Where(r => r.Id == nextId && r.Status == RegistrationStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RegistrationStatus.Active), cancellationToken)
            .ConfigureAwait(false);
        if (promoted != 1)
        {
            throw new InvalidOperationException("The waiting registration changed while the event was locked.");
        }

        var ticket = NewTicket(newTicketCode(), nowUtc);
        ticket.RegistrationId = nextId;
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>A new ticket with <paramref name="code"/> and its lookup hash.</summary>
    private static Ticket NewTicket(string code, DateTime issuedAtUtc) =>
        new() { Code = code, CodeHash = TicketCodeHash.Of(code), IssuedAt = issuedAtUtc };

    /// <summary>Raises the seat counter by one while a seat is free and the event is not past; returns the rows changed.</summary>
    private static Task<int> TakeSeatAsync(EventDbContext db, int eventId, DateOnly today, CancellationToken cancellationToken) =>
        db.Events
            .Where(e => e.Id == eventId && !e.Cancelled && e.Date >= today && e.RegisteredCount < e.Capacity)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.RegisteredCount, e => e.RegisteredCount + 1), cancellationToken);

    private static Task<int> QueuePositionAsync(EventDbContext db, int eventId, int queueNumber, CancellationToken cancellationToken) =>
        db.Registrations.CountAsync(
            r => r.EventId == eventId && r.Status == RegistrationStatus.Waiting && r.QueueNumber <= queueNumber,
            cancellationToken);

    private static Task<bool> HoldsPlaceAsync(EventDbContext db, int participantId, int eventId, CancellationToken cancellationToken) =>
        db.Registrations.AnyAsync(
            r => r.ParticipantId == participantId
                && r.EventId == eventId
                && (r.Status == RegistrationStatus.Active || r.Status == RegistrationStatus.Waiting),
            cancellationToken);
}
