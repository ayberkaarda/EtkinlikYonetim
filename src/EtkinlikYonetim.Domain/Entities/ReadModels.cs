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
    bool Disabled = false,
    int SessionVersion = 0);

/// <summary>
/// What an operation of an open session checks about its account, read at the time of the operation;
/// <paramref name="SessionVersion"/> changes with every change of the account's access.
/// </summary>
public sealed record SessionState(UserRole Role, bool Disabled, int SessionVersion = 0);

/// <summary>An account as admins see it in the account management; no password data.</summary>
public sealed record AccountSummary(int Id, string UserName, UserRole Role, bool Disabled);

/// <summary>A usable setup key, as a given key is compared with it.</summary>
public sealed record StoredSetupKey(int Id, byte[] KeyHash);

/// <summary>
/// An event with its seat usage, the length of its waiting list, its description and whether it was
/// cancelled, for pickers, the event management and rule checks. <paramref name="EditVersion"/> is the
/// event's edit version as loaded; a change made on this summary is stored only while it is still current.
/// </summary>
public sealed record EventSummary(
    int Id,
    string Name,
    DateOnly Date,
    int Capacity,
    int RegisteredCount,
    int WaitingCount = 0,
    string? Description = null,
    bool Cancelled = false,
    int EditVersion = 0)
{
    public int FreeSeats => Math.Max(0, Capacity - RegisteredCount);

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name} - {Date:dd.MM.yyyy}{(Cancelled ? " (İptal edildi)" : string.Empty)}");
}

/// <summary>A participant as found by national id, with its owner for the ownership check.</summary>
public sealed record ParticipantRecord(int Id, string NationalId, string? FullName, string? Email, int? OwnerUserId);

/// <summary>
/// A registration of an event that holds a seat, waits for one or was closed with the event, with its
/// participant, for the admin listing and the export. <paramref name="QueuePosition"/> is the current place
/// in the waiting list (1 is next); <paramref name="CheckedInAt"/> the UTC time the ticket was used at the
/// entrance. The ticket code is deliberately not part of it.
/// </summary>
public sealed record EventParticipant(
    int RegistrationId,
    string? FullName,
    string NationalId,
    string? Email,
    RegistrationStatus Status = RegistrationStatus.Active,
    int? QueuePosition = null,
    DateTime? CheckedInAt = null);

/// <summary>
/// A registration that holds a seat, waits for one or was closed with its event, with event, participant
/// and ticket, for "my registrations". <paramref name="QueuePosition"/> is the current place in the waiting
/// list (1 is next); <paramref name="OwnerUserId"/> the account that added the participant.
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
    int? QueuePosition = null,
    int? OwnerUserId = null);

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

/// <summary>
/// The parts of a registration that decide who may cancel it and whether it still can be;
/// <paramref name="CheckedIn"/> tells whether its ticket was used at the entrance.
/// </summary>
public sealed record RegistrationOwnership(
    int Id,
    int EventId,
    DateOnly EventDate,
    int? OwnerUserId,
    RegistrationStatus Status,
    bool CheckedIn = false);

/// <summary>
/// What the entrance needs to know about a ticket found by the hash of its code: its event's day, and the
/// participant's name, which the staff compare with an identity document.
/// </summary>
public sealed record TicketForCheckIn(
    int TicketId,
    string Code,
    int EventId,
    RegistrationStatus Status,
    bool EventCancelled,
    DateTime? CheckedInAt,
    DateOnly EventDate = default,
    string? ParticipantName = null);

/// <summary>The check-ins of an event: tickets used at the entrance and seats held.</summary>
public readonly record struct CheckInCount(int CheckedIn, int Seats);