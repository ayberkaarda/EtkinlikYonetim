using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

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
    TimeProvider timeProvider) : IAuthService
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
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > InputRules.StoredUserNameMaxLength || !IsAcceptableSignInPassword(password))
        {
            throw new BusinessRuleException(InputRequired);
        }

        var account = await users.FindByUserNameAsync(name, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            passwordHasher.SimulateVerification(password);
            throw new BusinessRuleException(CredentialsInvalid);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (account.LockoutEnd > now)
        {
            // Same answer and about the same cost as a wrong password; nothing is written, so the
            // lock neither grows nor ends early.
            passwordHasher.SimulateVerification(password);
            throw new BusinessRuleException(CredentialsInvalid);
        }

        var result = passwordHasher.Verify(password, account.PasswordHash, account.PasswordFormat);
        if (result == PasswordVerificationResult.Failed)
        {
            await users.RecordFailedLoginAsync(
                account.Id,
                LoginPolicy.MaxFailedAttempts,
                now + LoginPolicy.LockoutDuration,
                cancellationToken).ConfigureAwait(false);
            throw new BusinessRuleException(CredentialsInvalid);
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
            throw new BusinessRuleException(CredentialsInvalid);
        }

        return new UserSession(account.Id, account.UserName, account.Role);
    }

    public async Task RegisterAsync(
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default)
    {
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(password))
        {
            throw new BusinessRuleException(RegisterInputRequired);
        }

        if (!InputRules.IsValidUserName(name))
        {
            throw new BusinessRuleException(UserNameInvalid);
        }

        if (!string.Equals(password, passwordRepeat, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(PasswordsDoNotMatch);
        }

        if (password.Length < PasswordPolicy.MinLength || password.Length > PasswordPolicy.MaxLength)
        {
            throw new BusinessRuleException(PasswordInvalid);
        }

        if (await users.UserNameExistsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessRuleException(UserNameTaken);
        }

        var user = new User
        {
            UserName = name,
            PasswordHash = passwordHasher.Hash(password),
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
            throw new BusinessRuleException(UserNameTaken, ex);
        }
    }

    /// <summary>
    /// Sign-in only bounds the input by what an existing account can hold; the registration rules of
    /// <see cref="PasswordPolicy"/> apply to new passwords and would lock out legacy accounts here.
    /// </summary>
    private static bool IsAcceptableSignInPassword([NotNullWhen(true)] string? password) =>
        !string.IsNullOrEmpty(password) && password.Length <= PasswordPolicy.SignInMaxLength;
}
