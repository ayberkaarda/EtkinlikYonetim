using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>
/// First-run setup. A new database contains no account at all (there is no default or documented
/// password); the person installing the application creates the first admin account with a password
/// of their own choosing.
/// </summary>
public interface ISetupService
{
    /// <summary>Whether no admin account exists yet.</summary>
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the first admin account and signs it in. Refused once any admin exists, also when two
    /// setups run at the same time.
    /// </summary>
    /// <exception cref="BusinessRuleException">Invalid input, a taken user name or a finished setup.</exception>
    Task<UserSession> CreateFirstAdminAsync(
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default);
}

internal sealed class SetupService(IUserRepository users, IPasswordHasher passwordHasher) : ISetupService
{
    internal const string AlreadyDone = "Kurulum zaten tamamlanmış. Lütfen giriş yapın.";

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyAdminAsync(cancellationToken).ConfigureAwait(false);

    public async Task<UserSession> CreateFirstAdminAsync(
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

        int? id;
        try
        {
            // The repository decides atomically; a check here would leave a window for a second setup.
            id = await users.AddFirstAdminAsync(admin, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateUserNameException ex)
        {
            throw new BusinessRuleException(AuthService.UserNameTaken, ex);
        }

        return id is int created
            ? new UserSession(created, name, UserRole.Admin)
            : throw new BusinessRuleException(AlreadyDone);
    }
}
