namespace EtkinlikYonetim.Domain.Entities;

/// <summary>What a sign-in needs to know about an account.</summary>
public sealed record AccountCredentials(
    int Id,
    string UserName,
    UserRole Role,
    string PasswordHash,
    PasswordFormat PasswordFormat,
    int FailedLoginCount,
    DateTime? LockoutEnd,
    bool Disabled = false);

/// <summary>What an operation of an open session checks about its account, read at the time of the operation.</summary>
public sealed record SessionState(UserRole Role, bool Disabled);

/// <summary>An account as admins see it in the account management; no password data.</summary>
public sealed record AccountSummary(int Id, string UserName, UserRole Role, bool Disabled);

/// <summary>A usable setup key, as a given key is compared with it.</summary>
public sealed record StoredSetupKey(int Id, byte[] KeyHash);

/// <summary>
/// An event with its seat usage, the length of its waiting list, its description and whether it was
/// cancelled, for pickers, the event management and rule checks.
/// </summary>
public sealed record EventSummary(
    int Id,
    string Name,
    DateOnly Date,
    int Capacity,
    int RegisteredCount,
    int WaitingCount = 0,
    string? Description = null,
    bool Cancelled = false)
{
    public int FreeSeats => Math.Max(0, Capacity - RegisteredCount);

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name} - {Date:dd.MM.yyyy}{(Cancelled ? " (İptal edildi)" : string.Empty)}");
}

/// <summary>A participant as found by national id, with its owner for the ownership check.</summary>
public sealed record ParticipantRecord(int Id, string NationalId, string? FullName, string? Email, int? OwnerUserId);

/// <summary>
/// A registration of an event that holds a seat or waits for one, with its participant, for the admin
/// listing. <paramref name="QueuePosition"/> is the current place in the waiting list (1 is next).
/// </summary>
public sealed record EventParticipant(
    int RegistrationId,
    string? FullName,
    string NationalId,
    string? Email,
    string? TicketCode,
    RegistrationStatus Status = RegistrationStatus.Active,
    int? QueuePosition = null);

/// <summary>
/// A registration that holds a seat or waits for one, with event, participant and ticket, for "my
/// registrations". <paramref name="QueuePosition"/> is the current place in the waiting list (1 is next).
/// </summary>
public sealed record RegistrationDetails(
    int Id,
    int EventId,
    string EventName,
    DateOnly EventDate,
    int ParticipantId,
    string? ParticipantName,
    string? TicketCode,
    RegistrationStatus Status = RegistrationStatus.Active,
    int? QueuePosition = null);

/// <summary>
/// The result of a registration: a seat with a ticket (<see cref="RegistrationStatus.Active"/>) or a
/// place in the waiting list of a full event (<see cref="RegistrationStatus.Waiting"/>, with its position).
/// </summary>
public sealed record RegistrationOutcome(int RegistrationId, RegistrationStatus Status, int? QueuePosition);

/// <summary>What a cancellation did.</summary>
public enum CancellationOutcome
{
    /// <summary>No registration matched; nothing was changed.</summary>
    NotCancelled = 0,

    /// <summary>The registration was cancelled (a waiting one, or a seat no one was waiting for).</summary>
    Cancelled = 1,

    /// <summary>The seat was freed and the first waiting registration moved up to it with a new ticket.</summary>
    CancelledAndPromoted = 2,
}

/// <summary>The parts of a registration that decide who may cancel it and whether it still can be.</summary>
public sealed record RegistrationOwnership(
    int Id,
    int EventId,
    DateOnly EventDate,
    int? OwnerUserId,
    RegistrationStatus Status);
