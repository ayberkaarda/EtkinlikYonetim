using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using FluentValidation;

namespace EtkinlikYonetim.Business.Services;

/// <summary>What the ETKINLIK_SETUP_KEY environment variable fills in, or why it fills in nothing.</summary>
/// <param name="Key">The key to put in the key field, or <see langword="null"/>.</param>
/// <param name="Invalid">The variable is set to something that is not a setup key.</param>
public sealed record SetupKeyPrefill(string? Key, bool Invalid);

/// <summary>
/// First-run setup. A new database contains no account at all (there is no default or documented
/// password); the person installing the application creates the first admin account with a password
/// of their own choosing. A database that has accounts but no admin (for example an upgraded one) is no
/// such invitation: there the first admin needs a setup key that only the database owner can create
/// (docs/issue-setup-key.sql). It is valid for 24 hours, usable once from any computer, and accepted only
/// while no admin exists. The application cannot create keys.
/// </summary>
public interface ISetupService
{
    /// <summary>
    /// Whether the database is empty (no account at all), so the application opens the setup instead
    /// of the sign-in. A database with accounts keeps its normal sign-in.
    /// </summary>
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether no admin account exists, so the first one can still be created with a setup key.</summary>
    Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>The key ETKINLIK_SETUP_KEY fills into the form; checked against the database like a typed one.</summary>
    SetupKeyPrefill GetEnvironmentKey();

    /// <summary>
    /// Creates the first admin account and signs it in. Refused once any admin exists, also when two
    /// setups run at the same time. When the database has accounts, <paramref name="setupKey"/> must be
    /// a valid key issued by the database owner; it is used up in the transaction that creates the
    /// admin. An empty database needs no key. A refused key is followed by a fixed pause.
    /// </summary>
    /// <exception cref="BusinessRuleException">Invalid input, a missing or wrong key, a taken user name or a finished setup.</exception>
    Task<UserSession> CreateFirstAdminAsync(
        string? setupKey,
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default);
}

internal sealed class SetupService(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    SetupKeyGate setupKeys,
    SetupKeyEnvironment environment,
    IValidator<AccountInput> accountValidator) : ISetupService
{
    internal const string AlreadyDone = "Kurulum zaten tamamlanmış. Lütfen giriş yapın.";

    internal const string SetupKeyInvalid =
        "Kurulum anahtarı hatalı, süresi dolmuş veya daha önce kullanılmış. Gerekirse veritabanı sahibinden yeni bir anahtar isteyin.";

    internal const string SetupKeyRequired =
        "Bu veritabanında hesaplar olduğu için ilk yönetici hesabı yalnızca kurulum anahtarıyla oluşturulabilir.";

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyUserAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) =>
        !await users.AnyAdminAsync(cancellationToken).ConfigureAwait(false);

    public SetupKeyPrefill GetEnvironmentKey() => new(environment.Key, environment.IsSet && !environment.IsValid);

    public async Task<UserSession> CreateFirstAdminAsync(
        string? setupKey,
        string? userName,
        string? password,
        string? passwordRepeat,
        CancellationToken cancellationToken = default)
    {
        // The same rules as a new account of the sign-up form.
        var input = AccountInput.Create(userName, password, passwordRepeat);
        accountValidator.ValidateOrThrow(input);
        var name = input.UserName;

        var attempt = setupKeys.CreateAttempt(setupKey);
        var admin = new User
        {
            UserName = name,
            PasswordHash = passwordHasher.Hash(input.Password!),
            PasswordFormat = PasswordFormat.Pbkdf2,
            Role = UserRole.Admin,
        };

        FirstAdminResult result;
        try
        {
            // The repository decides atomically and checks the key inside its transaction, only when
            // the database has accounts; a check here would leave a window for a second setup. A key is
            // used up only by the write it authorized.
            result = await users.AddFirstAdminAsync(admin, attempt, cancellationToken).ConfigureAwait(false);
        }
        catch (DuplicateUserNameException ex)
        {
            throw new BusinessRuleException(AuthService.UserNameTaken, ex);
        }

        switch (result.Status)
        {
            case FirstAdminStatus.Created:
                return new UserSession(result.AdminId, name, UserRole.Admin);
            case FirstAdminStatus.NotAuthorized:
                await setupKeys.DelayAfterRefusalAsync(cancellationToken).ConfigureAwait(false);
                throw new BusinessRuleException(SetupKeyInvalid);
            case FirstAdminStatus.KeyRequired:
                throw new BusinessRuleException(SetupKeyRequired);
            default:
                throw new BusinessRuleException(AlreadyDone);
        }
    }
}
