using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

/// <summary>Tickets at the entrance.</summary>
public interface ITicketRepository
{
    /// <summary>The ticket whose code has the SHA-256 <paramref name="codeHash"/>, or <see langword="null"/>.</summary>
    Task<TicketForCheckIn?> FindForCheckInAsync(byte[] codeHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the ticket used at <paramref name="checkedInAtUtc"/> with one conditional UPDATE: only while it
    /// is unused, its registration holds a seat of <paramref name="eventId"/> and the event is not
    /// cancelled. Of two check-ins of the same ticket at the same time exactly one matches.
    /// </summary>
    /// <returns><see langword="false"/> when nothing matched; nothing is changed then.</returns>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<bool> CheckInAsync(Actor actor, int ticketId, int eventId, DateTime checkedInAtUtc, CancellationToken cancellationToken = default);

    /// <summary>The used tickets and the seats held of the event.</summary>
    Task<CheckInCount> GetCheckInCountAsync(int eventId, CancellationToken cancellationToken = default);
}