using System.Diagnostics.CodeAnalysis;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using FluentValidation;

namespace EtkinlikYonetim.Business.Validation;

// The user facing (Turkish) messages stay with the services that document them. They name fields and
// limits only, never the entered values, so they can be shown and logged as they are. Every validator
// stops at its first failure, so the user sees one clear message at a time, in the order of the rules.

internal static class ValidationRuleExtensions
{
    /// <summary>
    /// Validates <paramref name="input"/> and throws a <see cref="BusinessRuleException"/> carrying the
    /// first failure message.
    /// </summary>
    public static void ValidateOrThrow<T>(this IValidator<T> validator, T input)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = validator.Validate(input);
        if (!result.IsValid)
        {
            throw new BusinessRuleException(result.Errors[0].ErrorMessage);
        }
    }

    /// <summary>
    /// Sign-in only bounds the password by what an existing account can hold; the registration rules of
    /// <see cref="PasswordPolicy"/> apply to new passwords and would lock out legacy accounts here.
    /// </summary>
    public static bool IsAcceptableSignInPassword([NotNullWhen(true)] string? password) =>
        !string.IsNullOrEmpty(password) && password.Length <= PasswordPolicy.SignInMaxLength;
}

/// <summary>
/// A new account, from the sign-up form or the first-run setup: a name and a password are given, the
/// name is letters only and at most <see cref="InputRules.UserNameMaxLength"/> characters, the two
/// passwords are equal and <see cref="PasswordPolicy.MinLength"/> to <see cref="PasswordPolicy.MaxLength"/>
/// characters long.
/// </summary>
internal sealed class AccountInputValidator : AbstractValidator<AccountInput>
{
    public AccountInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(a => a.UserName)
            .Must((a, name) => name.Length > 0 && !string.IsNullOrWhiteSpace(a.Password)).WithMessage(AuthService.RegisterInputRequired)
            .Must(InputRules.IsValidUserName).WithMessage(AuthService.UserNameInvalid);
        RuleFor(a => a.PasswordRepeat)
            .Must((a, repeat) => string.Equals(a.Password, repeat, StringComparison.Ordinal)).WithMessage(AuthService.PasswordsDoNotMatch);
        RuleFor(a => a.Password)
            .Must(p => p is { Length: >= PasswordPolicy.MinLength and <= PasswordPolicy.MaxLength }).WithMessage(AuthService.PasswordInvalid);
    }
}

/// <summary>
/// Sign-in: a name no longer than an existing account can have and a password an existing account can
/// have. Every failure gets the same message.
/// </summary>
internal sealed class SignInInputValidator : AbstractValidator<SignInInput>
{
    public SignInInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(s => s.UserName)
            .NotEmpty().WithMessage(AuthService.InputRequired)
            .MaximumLength(InputRules.StoredUserNameMaxLength).WithMessage(AuthService.InputRequired);
        RuleFor(s => s.Password)
            .Must(ValidationRuleExtensions.IsAcceptableSignInPassword).WithMessage(AuthService.InputRequired);
    }
}

/// <summary>
/// A participant: every field given, a national id with a valid checksum, a name of letters and single
/// spaces, and an e-mail address of the form the original form checked.
/// </summary>
internal sealed class ParticipantInputValidator : AbstractValidator<ParticipantInput>
{
    public ParticipantInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(p => p)
            .Must(p => p.NationalId.Length > 0 && p.FullName.Length > 0 && p.Email.Length > 0)
            .WithName(nameof(ParticipantInput))
            .WithMessage(ParticipantService.FieldsRequired);
        RuleFor(p => p.NationalId)
            .Must(NationalIdNumber.IsValid).WithMessage(ParticipantService.NationalIdInvalid);
        RuleFor(p => p.FullName)
            .Must(InputRules.IsValidPersonName).WithMessage(ParticipantService.FullNameInvalid);
        RuleFor(p => p.Email)
            .Must(InputRules.IsValidEmail).WithMessage(ParticipantService.EmailInvalid);
    }
}

/// <summary>
/// A new event: a name of at most <see cref="InputRules.EventNameMaxLength"/> characters, a day that is
/// today or later, and a capacity from <see cref="InputRules.MinCapacity"/> to <see cref="InputRules.MaxCapacity"/>.
/// </summary>
internal sealed class EventInputValidator : AbstractValidator<EventInput>
{
    public EventInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(e => e.Name)
            .NotEmpty().WithMessage(EventService.NameRequired)
            .MaximumLength(InputRules.EventNameMaxLength).WithMessage(EventService.NameTooLong);
        RuleFor(e => e.Day)
            .Must((e, day) => day >= e.Today).WithMessage(EventService.PastDate);
        RuleFor(e => e.Capacity)
            .InclusiveBetween(InputRules.MinCapacity, InputRules.MaxCapacity).WithMessage(EventService.CapacityInvalid);
    }
}

/// <summary>
/// A change of an event: a day that is today or later, a capacity from <see cref="InputRules.MinCapacity"/>
/// to <see cref="InputRules.MaxCapacity"/> and a description of at most
/// <see cref="InputRules.EventDescriptionMaxLength"/> characters. Whether the capacity still holds the seats
/// taken is decided by the repository, inside its transaction.
/// </summary>
internal sealed class EventUpdateInputValidator : AbstractValidator<EventUpdateInput>
{
    public EventUpdateInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(e => e.Day)
            .Must((e, day) => day >= e.Today).WithMessage(EventService.NewDateInPast);
        RuleFor(e => e.Capacity)
            .InclusiveBetween(InputRules.MinCapacity, InputRules.MaxCapacity).WithMessage(EventService.CapacityInvalid);
        RuleFor(e => e.Description)
            .MaximumLength(InputRules.EventDescriptionMaxLength).WithMessage(EventService.DescriptionTooLong);
    }
}

/// <summary>
/// A registration request: an event is chosen and the national id has eleven digits. Rows of the
/// original application were checked for the format only, so the lookup does not require a valid
/// checksum; new participants are checked fully when they are added.
/// </summary>
internal sealed class RegistrationRequestInputValidator : AbstractValidator<RegistrationRequestInput>
{
    public RegistrationRequestInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(r => r.EventId)
            .GreaterThan(0).WithMessage(RegistrationService.EventRequired);
        RuleFor(r => r.NationalId)
            .Must(NationalIdNumber.HasValidFormat).WithMessage(RegistrationService.NationalIdFormat);
    }
}

/// <summary>The event of a registration takes place today or later; a full event leads to the waiting list.</summary>
internal sealed class RegistrationTargetInputValidator : AbstractValidator<RegistrationTargetInput>
{
    public RegistrationTargetInputValidator()
    {
        RuleFor(t => t.EventDate)
            .Must((t, day) => day >= t.Today).WithMessage(RegistrationService.PastEvent);
    }
}

/// <summary>
/// A cancellation: the registration still holds a seat or waits for one, its ticket has not been used at the
/// entrance, and its event is today or later.
/// </summary>
internal sealed class CancellationInputValidator : AbstractValidator<CancellationInput>
{
    public CancellationInputValidator()
    {
        ClassLevelCascadeMode = CascadeMode.Stop;

        RuleFor(c => c.Status)
            .Must(s => s is RegistrationStatus.Active or RegistrationStatus.Waiting).WithMessage(RegistrationService.AlreadyCancelled);
        RuleFor(c => c.CheckedIn)
            .Equal(false).WithMessage(RegistrationService.CheckedInCannotCancel);
        RuleFor(c => c.EventDate)
            .Must((c, day) => day >= c.Today).WithMessage(RegistrationService.PastCannotCancel);
    }
}
