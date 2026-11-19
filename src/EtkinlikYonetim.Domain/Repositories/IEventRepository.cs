using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

public interface IEventRepository
{
    /// <summary>All events, earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Events on or after <paramref name="from"/>, earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetFromAsync(DateOnly from, CancellationToken cancellationToken = default);

    Task<EventSummary?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Stores a new event without registrations and returns its id.</summary>
    Task<int> AddAsync(ScheduledEvent newEvent, CancellationToken cancellationToken = default);
}
