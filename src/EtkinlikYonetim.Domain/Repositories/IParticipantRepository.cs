using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

public interface IParticipantRepository
{
    Task<ParticipantRecord?> FindByNationalIdAsync(string nationalId, CancellationToken cancellationToken = default);

    /// <summary>Stores a new participant and returns its id.</summary>
    /// <exception cref="DuplicateNationalIdException">A participant with the same national id exists.</exception>
    Task<int> AddAsync(Participant participant, CancellationToken cancellationToken = default);

    /// <summary>Participants with an active registration for the event, by name.</summary>
    Task<IReadOnlyList<EventParticipant>> GetByEventAsync(int eventId, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a national id is already registered.</summary>
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
