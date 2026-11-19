using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

public interface IParticipantRepository
{
    /// <summary>
    /// Participants with the national id: only those owned by <paramref name="ownerUserId"/>, or those
    /// of every account when it is <see langword="null"/>. An account has at most one.
    /// </summary>
    Task<IReadOnlyList<ParticipantRecord>> FindByNationalIdAsync(string nationalId, int? ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Stores a new participant and returns its id.</summary>
    /// <exception cref="DuplicateNationalIdException">The owning account already has a participant with the same national id.</exception>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<int> AddAsync(Actor actor, Participant participant, CancellationToken cancellationToken = default);

    /// <summary>Participants with an active registration for the event, by name.</summary>
    Task<IReadOnlyList<EventParticipant>> GetByEventAsync(int eventId, CancellationToken cancellationToken = default);
}

/// <summary>Raised when the owning account already has a participant with the national id.</summary>
public sealed class DuplicateNationalIdException : Exception
{
    public DuplicateNationalIdException()
        : base("The national id is already registered.")
    {
    }

    public DuplicateNationalIdException(string message)
        : base(message)
    {
    }

    public DuplicateNationalIdException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
