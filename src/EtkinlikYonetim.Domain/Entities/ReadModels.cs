namespace EtkinlikYonetim.Domain.Entities;

/// <summary>What a sign-in needs to know about an account.</summary>
public sealed record AccountCredentials(
    int Id,
    string UserName,
    UserRole Role,
    string PasswordHash,
    PasswordFormat PasswordFormat,
    int FailedLoginCount,
    DateTime? LockoutEnd);

/// <summary>A usable setup key, as a given key is compared with it.</summary>
public sealed record StoredSetupKey(int Id, byte[] KeyHash);

/// <summary>An event with its seat usage, for pickers and rule checks.</summary>
public sealed record EventSummary(int Id, string Name, DateOnly Date, int Capacity, int RegisteredCount)
{
    public int FreeSeats => Math.Max(0, Capacity - RegisteredCount);

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Name} - {Date:dd.MM.yyyy}");
}

/// <summary>A participant as found by national id, with its owner for the ownership check.</summary>
public sealed record ParticipantRecord(int Id, string NationalId, string? FullName, string? Email, int? OwnerUserId);

/// <summary>An active registration of an event with its participant, for the admin listing.</summary>
public sealed record EventParticipant(
    int RegistrationId,
    string? FullName,
    string NationalId,
    string? Email,
    string? TicketCode);

/// <summary>An active registration with event, participant and ticket, for "my registrations".</summary>
public sealed record RegistrationDetails(
    int Id,
    int EventId,
    string EventName,
    DateOnly EventDate,
    int ParticipantId,
    string? ParticipantName,
    string? TicketCode);

/// <summary>The parts of a registration that decide who may cancel it and whether it still can be.</summary>
public sealed record RegistrationOwnership(
    int Id,
    int EventId,
    DateOnly EventDate,
    int? OwnerUserId,
    RegistrationStatus Status);
