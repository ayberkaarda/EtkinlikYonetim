using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

public interface IEventRepository
{
    /// <summary>All events, earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Events on or after <paramref name="from"/> that are not cancelled, earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetFromAsync(DateOnly from, CancellationToken cancellationToken = default);

    Task<EventSummary?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Stores a new event without registrations and returns its id.</summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<int> AddAsync(Actor actor, ScheduledEvent newEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the date, capacity and description of an event to <paramref name="changed"/>, only while the
    /// event still has the <paramref name="expected"/> values the caller loaded and is neither cancelled nor
    /// past, in one transaction under the event's lock; the new date must not be past either. "Today" is the
    /// database's day, read in that transaction after the lock. The capacity cannot go below
    /// the seats held (the UPDATE checks it, and so does the check constraint); seats added by a larger capacity
    /// go to the waiting list in order, each with the ticket <paramref name="newTicketCode"/> returns.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<EventChangeStatus> UpdateAsync(
        Actor actor,
        int id,
        EventValues expected,
        EventValues changed,
        Func<string> newTicketCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an event that is neither cancelled nor past by the database's day, read after the lock (soft delete): the row
    /// stays, its seats and waiting places are closed with <see cref="RegistrationStatus.EventCancelled"/> and
    /// the seat counter goes to zero, in one transaction under the event's lock.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<EventChangeStatus> CancelAsync(Actor actor, int id, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a change of an event by an admin.</summary>
public enum EventChangeStatus
{
    /// <summary>The change was stored.</summary>
    Changed = 0,

    /// <summary>The event does not exist; nothing was written.</summary>
    NotFound = 1,

    /// <summary>The event is cancelled or its day has passed; nothing was written.</summary>
    Closed = 2,

    /// <summary>The new capacity is below the seats held; nothing was written.</summary>
    CapacityBelowRegistrations = 3,

    /// <summary>The event no longer has the values the caller loaded (somebody else changed it); nothing was written.</summary>
    ChangedElsewhere = 4,

    /// <summary>The new date has passed by the database's day at the time of the write; nothing was written.</summary>
    DateInPast = 5,
}

/// <summary>The fields of an event an admin edits.</summary>
public readonly record struct EventValues(DateOnly Date, int Capacity, string? Description);
