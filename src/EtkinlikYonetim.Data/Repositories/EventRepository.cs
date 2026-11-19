using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class EventRepository(IDbContextFactory<EventDbContext> contextFactory) : IEventRepository
{
    public async Task<IReadOnlyList<EventSummary>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await Summaries(db, db.Events).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EventSummary>> GetFromAsync(DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await Summaries(db, db.Events.Where(e => e.Date >= from && !e.Cancelled)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<EventSummary?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await Summaries(db, db.Events.Where(e => e.Id == id)).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(Actor actor, ScheduledEvent newEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newEvent);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // A new event has no lock of its own yet; the admin is checked in the transaction that inserts it.
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);
        var entity = new ScheduledEvent
        {
            Name = newEvent.Name,
            Date = newEvent.Date,
            Capacity = newEvent.Capacity,
            Description = newEvent.Description,
            RegisteredCount = 0,
        };
        db.Events.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
    }

    public async Task<EventChangeStatus> UpdateAsync(
        Actor actor,
        int id,
        EventValues expected,
        EventValues changed,
        DateOnly today,
        DateTime nowUtc,
        Func<string> newTicketCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTicketCode);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AppLocks.AcquireAsync(db, AppLocks.Event(id), cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);

        // The conditions are the WHERE clause: whatever changed since the caller read the event (a
        // registration took a seat, someone cancelled the event, another admin changed its date, capacity
        // or description) is decided by this statement. Without the expected values a form loaded earlier
        // would silently write another admin's change back.
        var (expectedDate, expectedCapacity, expectedDescription) = expected;
        var (day, capacity, description) = changed;
        var updated = await db.Events
            .Where(e => e.Id == id
                && !e.Cancelled
                && e.Date >= today
                && e.RegisteredCount <= capacity
                && e.Date == expectedDate
                && e.Capacity == expectedCapacity
                && e.Description == expectedDescription)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Date, day)
                    .SetProperty(e => e.Capacity, capacity)
                    .SetProperty(e => e.Description, description),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated != 1)
        {
            var current = await db.Events
                .Where(e => e.Id == id)
                .Select(e => new { e.Cancelled, e.Date, e.Capacity, e.Description })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return current is null ? EventChangeStatus.NotFound
                : current.Cancelled || current.Date < today ? EventChangeStatus.Closed
                : new EventValues(current.Date, current.Capacity, current.Description) != expected ? EventChangeStatus.ChangedElsewhere
                : EventChangeStatus.CapacityBelowRegistrations;
        }

        // Seats added by a larger capacity go to the waiting list in its order.
        while (await RegistrationRepository.PromoteNextAsync(db, id, today, nowUtc, newTicketCode, cancellationToken).ConfigureAwait(false))
        {
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return EventChangeStatus.Changed;
    }

    public async Task<EventChangeStatus> CancelAsync(Actor actor, int id, DateOnly today, DateTime cancelledAtUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AppLocks.AcquireAsync(db, AppLocks.Event(id), cancellationToken).ConfigureAwait(false);
        await Actors.RequireAsync(db, actor, admin: true, cancellationToken).ConfigureAwait(false);

        var cancelled = await db.Events
            .Where(e => e.Id == id && !e.Cancelled && e.Date >= today)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Cancelled, true)
                    .SetProperty(e => e.CancelledAt, cancelledAtUtc)
                    .SetProperty(e => e.RegisteredCount, 0),
                cancellationToken)
            .ConfigureAwait(false);
        if (cancelled != 1)
        {
            var exists = await db.Events.AnyAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return exists ? EventChangeStatus.Closed : EventChangeStatus.NotFound;
        }

        // The rows stay as history; the tickets of the closed seats are no longer valid at the entrance.
        await db.Registrations
            .Where(r => r.EventId == id && (r.Status == RegistrationStatus.Active || r.Status == RegistrationStatus.Waiting))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, RegistrationStatus.EventCancelled)
                    .SetProperty(r => r.CancelledAt, cancelledAtUtc),
                cancellationToken)
            .ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return EventChangeStatus.Changed;
    }

    private static IQueryable<EventSummary> Summaries(EventDbContext db, IQueryable<ScheduledEvent> events) =>
        events
            .AsNoTracking()
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id)
            .Select(e => new EventSummary(
                e.Id,
                e.Name,
                e.Date,
                e.Capacity,
                e.RegisteredCount,
                db.Registrations.Count(r => r.EventId == e.Id && r.Status == RegistrationStatus.Waiting),
                e.Description,
                e.Cancelled));
}
