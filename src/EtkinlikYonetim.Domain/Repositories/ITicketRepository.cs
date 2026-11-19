using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

/// <summary>Tickets at the entrance.</summary>
public interface ITicketRepository
{
    /// <summary>The ticket whose code has the SHA-256 <paramref name="codeHash"/>, or <see langword="null"/>.</summary>
    Task<TicketForCheckIn?> FindForCheckInAsync(byte[] codeHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the ticket used, in one transaction under the event's lock. After the lock the event's date and
    /// the database's day are read again: on another day nothing is written. Otherwise one conditional
    /// UPDATE marks the ticket used at the database's time, only while it is unused, its registration holds
    /// a seat of <paramref name="eventId"/> and the event is not cancelled and is still on that day. Of two
    /// check-ins of the same ticket at the same time exactly one matches.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<CheckInWriteStatus> CheckInAsync(Actor actor, int ticketId, int eventId, CancellationToken cancellationToken = default);

    /// <summary>The used tickets and the seats held of the event.</summary>
    Task<CheckInCount> GetCheckInCountAsync(int eventId, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="ITicketRepository.CheckInAsync"/>.</summary>
public enum CheckInWriteStatus
{
    /// <summary>The ticket was marked used.</summary>
    CheckedIn = 0,

    /// <summary>
    /// The ticket was used meanwhile, or its registration or event was cancelled, or the event no longer
    /// exists; nothing was written.
    /// </summary>
    NotMatched = 1,

    /// <summary>The event is not today by the database's clock at the time of the write; nothing was written.</summary>
    NotEventDay = 2,
}
