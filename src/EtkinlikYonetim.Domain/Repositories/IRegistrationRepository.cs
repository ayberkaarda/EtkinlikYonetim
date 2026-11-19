using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

public interface IRegistrationRepository
{
    /// <summary>
    /// Registers the participant for the event and issues the ticket, in one transaction: the event's
    /// seat counter is raised by a conditional UPDATE that only matches while a seat is free and the
    /// event is not before <paramref name="today"/>, so concurrent registrations can neither overbook the
    /// event nor register for a past one. Returns the new registration id.
    /// </summary>
    /// <exception cref="AlreadyRegisteredException">The participant already has an active registration for the event.</exception>
    /// <exception cref="EventFullException">No seat is free.</exception>
    /// <exception cref="EventClosedException">The event does not exist or is in the past.</exception>
    Task<int> AddAsync(
        int participantId,
        int eventId,
        DateOnly today,
        DateTime createdAtUtc,
        string ticketCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Active registrations of the participants owned by <paramref name="ownerUserId"/>, or of everyone
    /// when it is <see langword="null"/>; latest event first.
    /// </summary>
    Task<IReadOnlyList<RegistrationDetails>> GetActiveAsync(int? ownerUserId, CancellationToken cancellationToken = default);

    Task<RegistrationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the registration and frees its seat in one transaction, only if it is still active, its
    /// event is not before <paramref name="today"/> and, when <paramref name="ownerUserId"/> is given, its
    /// participant belongs to that account.
    /// </summary>
    /// <returns><see langword="false"/> when no registration matched; nothing is changed then.</returns>
    Task<bool> CancelAsync(
        int id,
        int? ownerUserId,
        DateOnly today,
        DateTime cancelledAtUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when a participant is already registered for an event.</summary>
public sealed class AlreadyRegisteredException : Exception
{
    public AlreadyRegisteredException()
        : base("The participant is already registered for the event.")
    {
    }

    public AlreadyRegisteredException(string message)
        : base(message)
    {
    }

    public AlreadyRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when an event has no free seat.</summary>
public sealed class EventFullException : Exception
{
    public EventFullException()
        : base("The event has no free seat.")
    {
    }

    public EventFullException(string message)
        : base(message)
    {
    }

    public EventFullException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Raised when an event does not exist or is in the past.</summary>
public sealed class EventClosedException : Exception
{
    public EventClosedException()
        : base("The event does not exist or is in the past.")
    {
    }

    public EventClosedException(string message)
        : base(message)
    {
    }

    public EventClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
