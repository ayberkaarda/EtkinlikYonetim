using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Validation;

/// <summary>
/// A new account (sign-up form or first-run setup). The user name is trimmed on creation; the
/// passwords are kept exactly as typed.
/// </summary>
internal sealed record AccountInput(string UserName, string? Password, string? PasswordRepeat)
{
    public static AccountInput Create(string? userName, string? password, string? passwordRepeat) =>
        new(userName?.Trim() ?? string.Empty, password, passwordRepeat);
}

/// <summary>A sign-in attempt. The user name is trimmed; the password is kept as typed.</summary>
internal sealed record SignInInput(string UserName, string? Password)
{
    public static SignInInput Create(string? userName, string? password) =>
        new(userName?.Trim() ?? string.Empty, password);
}

/// <summary>A participant to add; every field is trimmed on creation.</summary>
internal sealed record ParticipantInput(string NationalId, string FullName, string Email)
{
    public static ParticipantInput Create(string? nationalId, string? fullName, string? email) =>
        new(nationalId?.Trim() ?? string.Empty, fullName?.Trim() ?? string.Empty, email?.Trim() ?? string.Empty);
}

/// <summary>
/// A new event, checked against the local date <paramref name="Today"/>. The name is trimmed and the
/// date reduced to its calendar day on creation.
/// </summary>
internal sealed record EventInput(string Name, DateOnly Day, int Capacity, DateOnly Today)
{
    public static EventInput Create(string? name, DateTime eventDate, int capacity, DateOnly today) =>
        new(name?.Trim() ?? string.Empty, DateOnly.FromDateTime(eventDate), capacity, today);
}

/// <summary>A registration request: the chosen event and the participant's national id (trimmed).</summary>
internal sealed record RegistrationRequestInput(int EventId, string NationalId)
{
    public static RegistrationRequestInput Create(int eventId, string? nationalId) =>
        new(eventId, nationalId?.Trim() ?? string.Empty);
}

/// <summary>
/// The event a participant is about to be registered for, checked against the local date
/// <paramref name="Today"/>. A full event is no refusal: the repository puts the participant on its
/// waiting list, and repeats the date check inside its transaction.
/// </summary>
internal sealed record RegistrationTargetInput(DateOnly EventDate, DateOnly Today);

/// <summary>
/// A cancellation of a registration the caller may see, checked against the local date
/// <paramref name="Today"/>. Ownership is decided before this input is built.
/// </summary>
internal sealed record CancellationInput(RegistrationStatus Status, DateOnly EventDate, DateOnly Today);
