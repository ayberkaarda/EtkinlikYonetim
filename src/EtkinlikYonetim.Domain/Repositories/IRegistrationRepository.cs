using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

public interface IRegistrationRepository
{
    /// <summary>
    /// Registers the participant for the event in one transaction, serialized with every other change of
    /// the event's seats (an application lock per event on SQL Server). While a seat is free and nobody is
    /// waiting, the seat counter is raised by a conditional UPDATE that also checks the capacity and the
    /// date, and the ticket <paramref name="newTicketCode"/> returns is issued. Otherwise the registration
    /// joins the end of the event's waiting list without a ticket. The day and the stored time are the
    /// database's, read in the transaction after the lock.
    /// </summary>
    /// <exception cref="AlreadyRegisteredException">The participant already holds a seat or waits for one at the event.</exception>
    /// <exception cref="EventClosedException">The event does not exist, is cancelled or is in the past.</exception>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<RegistrationOutcome> AddAsync(
        Actor actor,
        int participantId,
        int eventId,
        Func<string> newTicketCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registrations that hold a seat or wait for one, and those closed by the cancellation of their event,
    /// of the participants owned by <paramref name="ownerUserId"/>, or of everyone when it is
    /// <see langword="null"/>; latest event first.
    /// </summary>
    Task<IReadOnlyList<RegistrationDetails>> GetActiveAsync(int? ownerUserId, CancellationToken cancellationToken = default);

    Task<RegistrationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the registration in one transaction under the event's lock, only if it still holds a seat
    /// or waits for one, its event is not past by the database's day (read after the lock) and, when
    /// <paramref name="ownerUserId"/> is given, its participant belongs to that account. A freed seat goes
    /// to the first waiting registration in the same transaction, with the ticket
    /// <paramref name="newTicketCode"/> returns; the seat counter stays equal to the seats held.
    /// </summary>
    /// <exception cref="ActorRevokedException"><paramref name="actor"/> is no longer current, checked after the lock in the writing transaction; nothing was written.</exception>
    Task<CancellationOutcome> CancelAsync(
        Actor actor,
        int id,
        int? ownerUserId,
        Func<string> newTicketCode,
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
