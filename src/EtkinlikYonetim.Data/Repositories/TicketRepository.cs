using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class TicketRepository(IDbContextFactory<EventDbContext> contextFactory, TimeProvider timeProvider) : ITicketRepository
{
    public async Task<TicketForCheckIn?> FindForCheckInAsync(byte[] codeHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await (
                from t in db.Tickets.AsNoTracking()
                join r in db.Registrations on t.RegistrationId equals r.Id
                join e in db.Events on r.EventId equals e.Id
                join p in db.Participants on r.ParticipantId equals p.Id
                where t.CodeHash == codeHash
                select new TicketForCheckIn(t.Id, t.Code, r.EventId, r.Status, e.Cancelled, t.CheckedInAt, e.Date, p.FullName))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CheckInWriteStatus> CheckInAsync(Actor actor, int ticketId, int eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The event lock of every seat change and of every change of the event: a cancellation of this
        // registration, or a change or cancellation of the event, waits for the check-in or the check-in waits
        // for it, so the two can never both succeed (also not under READ_COMMITTED_SNAPSHOT, where the
        // conditions below would otherwise read the last committed rows).
        await AppLocks.AcquireAsync(db, AppLocks.Event(eventId), cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);

        // The day and the event's date as they are now, under the lock: the service's check was made before
        // the wait, and the event may have been moved to another day since.
        var (checkedInAtUtc, today) = await DatabaseTime.NowAsync(db, timeProvider, cancellationToken).ConfigureAwait(false);
        var date = await db.Events
            .Where(e => e.Id == eventId)
            .Select(e => (DateOnly?)e.Date)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (date is { } day && day != today)
        {
            return CheckInWriteStatus.NotEventDay;
        }

        // The WHERE clause is the decision: a second scan, a cancellation, a cancelled or moved event since
        // the lookup make the statement match nothing.
        var updated = await db.Tickets
            .Where(t => t.Id == ticketId
                && t.CheckedInAt == null
                && db.Registrations.Any(r => r.Id == t.RegistrationId
                    && r.EventId == eventId
                    && r.Status == RegistrationStatus.Active
                    && db.Events.Any(e => e.Id == eventId && !e.Cancelled && e.Date == today)))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CheckedInAt, checkedInAtUtc), cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1 ? CheckInWriteStatus.CheckedIn : CheckInWriteStatus.NotMatched;
    }

    public async Task<CheckInCount> GetCheckInCountAsync(int eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var counts = await db.Registrations
            .AsNoTracking()
            .Where(r => r.EventId == eventId && r.Status == RegistrationStatus.Active)
            .GroupBy(r => 1)
            .Select(g => new
            {
                Seats = g.Count(),
                CheckedIn = g.Count(r => r.Ticket != null && r.Ticket.CheckedInAt != null),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return counts is null ? default : new CheckInCount(counts.CheckedIn, counts.Seats);
    }
}