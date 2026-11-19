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
        return await Summaries(db, db.Events.Where(e => e.Date >= from)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<EventSummary?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await Summaries(db, db.Events.Where(e => e.Id == id)).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(ScheduledEvent newEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newEvent);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = new ScheduledEvent
        {
            Name = newEvent.Name,
            Date = newEvent.Date,
            Capacity = newEvent.Capacity,
            RegisteredCount = 0,
        };
        db.Events.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity.Id;
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
                db.Registrations.Count(r => r.EventId == e.Id && r.Status == RegistrationStatus.Waiting)));
}
