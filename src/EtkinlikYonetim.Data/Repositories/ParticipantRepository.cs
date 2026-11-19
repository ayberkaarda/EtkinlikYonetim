using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class ParticipantRepository(IDbContextFactory<EventDbContext> contextFactory) : IParticipantRepository
{
    public async Task<IReadOnlyList<ParticipantRecord>> FindByNationalIdAsync(
        string nationalId,
        int? ownerUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nationalId);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var query = db.Participants.AsNoTracking().Where(p => p.NationalId == nationalId);
        if (ownerUserId is int owner)
        {
            query = query.Where(p => p.OwnerUserId == owner);
        }

        return await query
            .OrderBy(p => p.Id)
            .Select(p => new ParticipantRecord(p.Id, p.NationalId, p.FullName, p.Email, p.OwnerUserId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> AddAsync(Participant participant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(participant);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = new Participant
        {
            NationalId = participant.NationalId,
            FullName = participant.FullName,
            Email = participant.Email,
            OwnerUserId = participant.OwnerUserId,
        };
        db.Participants.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // The unique index rejected a concurrent insert of the same national id for the same
            // account. Checked with a new query so the provider specific error code does not matter.
            if (await OwnerHasNationalIdAsync(participant.OwnerUserId, participant.NationalId).ConfigureAwait(false))
            {
                throw new DuplicateNationalIdException("The national id is already registered.", ex);
            }

            throw;
        }

        return entity.Id;
    }

    public async Task<IReadOnlyList<EventParticipant>> GetByEventAsync(int eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await (
                from r in db.Registrations.AsNoTracking()
                join p in db.Participants on r.ParticipantId equals p.Id
                where r.EventId == eventId
                    && (r.Status == RegistrationStatus.Active || r.Status == RegistrationStatus.Waiting || r.Status == RegistrationStatus.EventCancelled)
                orderby r.Status, r.QueueNumber, p.FullName, r.Id
                select new EventParticipant(
                    r.Id,
                    p.FullName,
                    p.NationalId,
                    p.Email,
                    r.Ticket != null ? r.Ticket.Code : null,
                    r.Status,
                    r.Status == RegistrationStatus.Waiting
                        ? db.Registrations.Count(w => w.EventId == r.EventId && w.Status == RegistrationStatus.Waiting && w.QueueNumber <= r.QueueNumber)
                        : null))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> OwnerHasNationalIdAsync(int? ownerUserId, string nationalId)
    {
        await using var db = await contextFactory.CreateDbContextAsync(CancellationToken.None).ConfigureAwait(false);
        return await db.Participants
            .AnyAsync(p => p.OwnerUserId == ownerUserId && p.NationalId == nationalId, CancellationToken.None)
            .ConfigureAwait(false);
    }
}
