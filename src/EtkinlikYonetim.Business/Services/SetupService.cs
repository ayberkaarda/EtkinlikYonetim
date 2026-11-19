using System.Globalization;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// First-run setup. A new database contains no account at all (there is no default or documented
/// password); the person installing the application creates the first admin account with a password
/// of their own choosing. A database that has accounts but no admin (for example an upgraded one) is no
/// such invitation: there the first admin needs the local setup key (<see cref="ISetupKeyStore"/>), so
/// having no admin alone does not make whoever opens the application its founder.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Whether the database is empty (no account at all), so the application opens the setup instead
    /// of the sign-in. A database with accounts keeps its normal sign-in.
    /// </summary>
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether no admin account exists, so the first one can still be created with the setup key.</summary>
    Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure a setup key exists and returns the file that holds it, or <see langword="null"/> when
    /// the key is taken from the environment.
    /// </summary>
    string? IssueSetupKey();

    /// <summary>
    /// Creates the first admin account and signs it in. Refused once any admin exists, also when two
    /// setups run at the same time. When the database has accounts, <paramref name="setupKey"/> must be
    /// the current setup key; it is used up by the setup that creates the admin. An empty database
    /// needs no key.
    /// </summary>
    /// <exception cref="BusinessRuleException">Invalid input, a wrong key, a taken user name or a finished setup.</exception>
    Task<UserSession> CreateFirstAdminAsync(
        string? setupKey,
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default);
}

internal sealed class SetupService(IUserRepository users, IPasswordHasher passwordHasher, ISetupKeyStore setupKeys) : ISetupService
{
    internal const string AlreadyDone = "Kurulum zaten tamamlanmış. Lütfen giriş yapın.";
    internal const string SetupKeyInvalid = "Kurulum anahtarı hatalı veya daha önce kullanılmış.";

    internal static readonly string SetupKeyLocked = string.Create(
        CultureInfo.InvariantCulture,
        $"Çok fazla hatalı kurulum anahtarı girildi. {SetupKeyPolicy.LockoutDuration.TotalMinutes:0} dakika sonra yeniden deneyin.");

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyUserAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyAdminAsync(cancellationToken).ConfigureAwait(false);

    public string? IssueSetupKey()
    {
        setupKeys.EnsureIssued();
        return setupKeys.KeyFilePath;
    }

    public async Task<UserSession> CreateFirstAdminAsync(
        string? setupKey,
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default)
    {
        // The same rules as a new account of the sign-up form.
        var name = userName?.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(password))
        {
            throw new BusinessRuleException(AuthService.RegisterInputRequired);
        }

        if (!InputRules.IsValidUserName(name))
        {
            throw new BusinessRuleException(AuthService.UserNameInvalid);
        }

        if (!string.Equals(password, passwordRepeat, StringComparison.Ordinal))
        {
            throw new BusinessRuleException(AuthService.PasswordsDoNotMatch);
        }

        if (password.Length < PasswordPolicy.MinLength || password.Length > PasswordPolicy.MaxLength)
        {
            throw new BusinessRuleException(AuthService.PasswordInvalid);
        }

        var admin = new User
        {
            UserName = name,
            PasswordHash = passwordHasher.Hash(password),
            PasswordFormat = PasswordFormat.Pbkdf2,
            Role = UserRole.Admin,
        };

        FirstAdminResult result;
        var keyCheck = SetupKeyCheck.Wrong;
        try
        {
            // The repository decides atomically and asks for the key inside its transaction, only when
            // the database has accounts; a check here would leave a window for a second setup.
            result = await users.AddFirstAdminAsync(
                admin,
                () => (keyCheck = setupKeys.TryConsume(setupKey)) == SetupKeyCheck.Consumed,
                cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateUserNameException ex)
        {
            throw new BusinessRuleException(AuthService.UserNameTaken, ex);
        }

        return result.Status switch
        {
            FirstAdminStatus.Created => new UserSession(result.AdminId, name, UserRole.Admin),
            FirstAdminStatus.NotAuthorized => throw new BusinessRuleException(keyCheck == SetupKeyCheck.LockedOut ? SetupKeyLocked : SetupKeyInvalid),
            _ => throw new BusinessRuleException(AlreadyDone),
        };
    }
}
