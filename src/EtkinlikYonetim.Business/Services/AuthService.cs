using System.Globalization;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using FluentValidation;

namespace EtkinlikYonetim.Business.Services;

public interface IAuthService
{
    /// <summary>Signs an account in with its user name and password.</summary>
    /// <exception cref="BusinessRuleException">Wrong credentials, invalid input or a locked account.</exception>
    Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default);

    /// <summary>Registers a new account with the user role. The password is stored as a hash.</summary>
    Task RegisterAsync(string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default);
}

internal sealed class AuthService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IDatabaseClock clock,
    IValidator<SignInInput> signInValidator,
    IValidator<AccountInput> accountValidator,
    IAuditLog audit) : IAuthService
{
    internal const string InputRequired = "Lütfen kullanıcı adı ve şifrenizi girin.";
    internal const string RegisterInputRequired = "Kullanıcı adı ve şifre boş olamaz.";
    internal const string PasswordsDoNotMatch = "Şifreler uyuşmuyor.";
    internal const string UserNameTaken = "Bu kullanıcı adı zaten kullanılıyor.";

    internal static readonly string UserNameInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kullanıcı adı yalnızca harflerden oluşmalı ve en fazla {InputRules.UserNameMaxLength} karakter olmalıdır.");

    internal static readonly string PasswordInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Şifre en az {PasswordPolicy.MinLength}, en fazla {PasswordPolicy.MaxLength} karakter olmalıdır.");

    // A locked account gets the same answer as wrong credentials, so the lock state is not disclosed
    // before the user has proven who they are; the lockout rule itself is stated for everyone.
    internal static readonly string CredentialsInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kullanıcı adı veya şifre hatalı. {LoginPolicy.MaxFailedAttempts} hatalı denemeden sonra giriş {LoginPolicy.LockoutDuration.TotalMinutes:0} dakika engellenir.");

    public async Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        // Every refusal records the same audit entry, without a role: the local log must not tell an
        // unknown account from a locked one or from a wrong password.
        var input = SignInInput.Create(userName, password);
        if (!signInValidator.Validate(input).IsValid)
        {
            throw SignInRefused(InputRequired);
        }

        var name = input.UserName;
        password = input.Password!;

        // Locks are set and ended by the database's clock, so a computer whose clock is ahead cannot end
        // one early. Read for every attempt, so an unknown account takes the same round trips.
        var now = await clock.GetUtcNowAsync(cancellationToken).ConfigureAwait(false);
        var account = await users.FindByUserNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            passwordHasher.SimulateVerification(password);
            throw SignInRefused();
        }

        if (account.Disabled || account.LockoutEnd > now)
        {
            // Same answer and about the same cost as a wrong password; nothing is written, so the
            // lock neither grows nor ends early, and a disabled account is not told apart.
            passwordHasher.SimulateVerification(password);
            throw SignInRefused();
        }

        var result = passwordHasher.Verify(password, account.PasswordHash, account.PasswordFormat);
        if (result == PasswordVerificationResult.Failed)
        {
            await users.RecordFailedLoginAsync(
                account.Id,
                LoginPolicy.MaxFailedAttempts,
                now + LoginPolicy.LockoutDuration,
                cancellationToken).ConfigureAwait(false);
            throw SignInRefused();
        }

        string? newHash = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // Plain text passwords were trimmed by the original application before they were stored.
            var legacy = account.PasswordFormat == PasswordFormat.LegacyPlainText;
            newHash = passwordHasher.Hash(legacy ? password.Trim() : password);
        }

        // The session is issued only if the account is still unlocked and unchanged when the success
        // is written; a lock placed by a concurrent failure since the read wins.
        var recorded = await users.RecordSuccessfulLoginAsync(account, now, newHash, cancellationToken).ConfigureAwait(false);
        if (!recorded)
        {
            throw SignInRefused();
        }

        if (newHash is not null)
        {
            audit.Record(AuditEvent.PasswordUpgraded, account.Role);
        }

        audit.Record(AuditEvent.SignInSucceeded, account.Role);
        return new UserSession(account.Id, account.UserName, account.Role, account.SessionVersion);
    }

    public async Task RegisterAsync(
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default)
    {
        var input = AccountInput.Create(userName, password, passwordRepeat);
        accountValidator.ValidateOrThrow(input);
        var name = input.UserName;

        if (await users.UserNameExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            throw RegistrationRefused();
        }

        var user = new User
        {
            UserName = name,
            PasswordHash = passwordHasher.Hash(input.Password!),
            PasswordFormat = PasswordFormat.Pbkdf2,
            Role = UserRole.User,
        };

        try
        {
            await users.AddAsync(user, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateUserNameException ex)
        {
            // Registered concurrently between the check and the insert.
            throw RegistrationRefused(ex);
        }

        audit.Record(AuditEvent.AccountRegistered, UserRole.User);
    }

    /// <summary>A taken name; the log file writes it with the code of a new account.</summary>
    private BusinessRuleException RegistrationRefused(Exception? inner = null)
    {
        audit.Record(AuditEvent.AccountRegistrationRefused, UserRole.User);
        return inner is null ? new BusinessRuleException(UserNameTaken) : new BusinessRuleException(UserNameTaken, inner);
    }

    private BusinessRuleException SignInRefused(string? message = null)
    {
        audit.Record(AuditEvent.SignInFailed, null);
        return new BusinessRuleException(message ?? CredentialsInvalid);
    }
}
