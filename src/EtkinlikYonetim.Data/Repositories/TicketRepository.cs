using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class TicketRepository(IDbContextFactory<EventDbContext> contextFactory) : ITicketRepository
{
    public async Task<TicketForCheckIn?> FindForCheckInAsync(byte[] codeHash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codeHash);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await (
                from t in db.Tickets.AsNoTracking()
                join r in db.Registrations on t.RegistrationId equals r.Id
                join e in db.Events on r.EventId equals e.Id
                where t.CodeHash == codeHash
                select new TicketForCheckIn(t.Id, t.Code, r.EventId, r.Status, e.Cancelled, t.CheckedInAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> CheckInAsync(int ticketId, int eventId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // The event lock of every seat change: a cancellation of this registration, or of the event, waits
        // for the check-in or the check-in waits for it, so the two can never both succeed (also not under
        // READ_COMMITTED_SNAPSHOT, where the conditions below would otherwise read the last committed rows).
        await AppLocks.AcquireAsync(db, AppLocks.Event(eventId), cancellationToken).ConfigureAwait(false);

        // The WHERE clause is the decision: a second scan, a cancellation or a cancelled event since the
        // lookup make the statement match nothing.
        var updated = await db.Tickets
            .Where(t => t.Id == ticketId
                && t.CheckedInAt == null
                && db.Registrations.Any(r => r.Id == t.RegistrationId
                    && r.EventId == eventId
                    && r.Status == RegistrationStatus.Active
                    && db.Events.Any(e => e.Id == eventId && !e.Cancelled)))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.CheckedInAt, checkedInAtUtc), cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return updated == 1;
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