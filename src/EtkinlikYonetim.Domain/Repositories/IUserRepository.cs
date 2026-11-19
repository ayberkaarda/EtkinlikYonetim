using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Domain.Repositories;

/// <summary>Accounts and their sign-in state.</summary>
public interface IUserRepository
{
    Task<AccountCredentials?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default);

    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>Stores a new account and returns its id.</summary>
    /// <exception cref="DuplicateUserNameException">The user name is taken.</exception>
    Task<int> AddAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Whether any account has the admin role.</summary>
    Task<bool> AnyAdminAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether any account exists, of any role.</summary>
    Task<bool> AnyUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="admin"/> as the first admin account, only while no admin exists. The
    /// check and the insert are one atomic step, so of concurrent setups exactly one succeeds. An empty
    /// database needs no key. A database that already has accounts needs <paramref name="setupKey"/>:
    /// inside the same serializable write transaction, after the checks found no admin and a free user
    /// name, the key is compared with every usable stored key and the matching one is used up by a
    /// conditional UPDATE. When the insert or the commit fails the use rolls back with it; a wrong key
    /// writes nothing, so wrong keys can never revoke the founder's key. There is no way to create a
    /// key here: keys come only from the database owner's script.
    /// </summary>
    /// <exception cref="DuplicateUserNameException">The user name is taken by another account.</exception>
    Task<FirstAdminResult> AddFirstAdminAsync(
        User admin,
        SetupKeyAttempt? setupKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts a failed sign-in atomically. When the count reaches <paramref name="maxAttempts"/> the
    /// account is locked until <paramref name="lockoutEnd"/> and the count starts again from zero.
    /// </summary>
    Task RecordFailedLoginAsync(int userId, int maxAttempts, DateTime lockoutEnd, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful sign-in in one conditional UPDATE: only while the account is not locked at
    /// <paramref name="now"/> and still has the password value and format of <paramref name="verified"/>
    /// are the failed attempts reset. When <paramref name="newPasswordHash"/> is given the stored
    /// password is replaced with it and its format becomes <see cref="PasswordFormat.Pbkdf2"/> in the
    /// same statement (upgrade of a plain text or weaker hash).
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the account changed after it was read (for example a concurrent
    /// failure locked it); nothing is written then and the sign-in must be refused.
    /// </returns>
    Task<bool> RecordSuccessfulLoginAsync(
        AccountCredentials verified,
        DateTime now,
        string? newPasswordHash,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IUserRepository.AddFirstAdminAsync"/>.</summary>
public enum FirstAdminStatus
{
    /// <summary>The admin was stored; its id is in <see cref="FirstAdminResult.AdminId"/>.</summary>
    Created = 0,

    /// <summary>An admin existed already; nothing was written.</summary>
    AdminExists = 1,

    /// <summary>The database has accounts and the setup key was wrong, expired or used; nothing was written.</summary>
    NotAuthorized = 2,

    /// <summary>The database has accounts, so a setup key is needed and none was given; nothing was written.</summary>
    KeyRequired = 3,
}

/// <summary>
/// A setup key given by someone, checked by a repository inside its write transaction. The repository
/// passes every key usable now (unused and unexpired); <see cref="FindMatch"/> compares
/// the given key with each of them in constant time and returns the id of the matching one, or
/// <see langword="null"/>. Wrong keys are never counted against a stored key, so nobody can make the
/// founder's key unusable by guessing.
/// </summary>
public sealed class SetupKeyAttempt
{
    private readonly Func<IReadOnlyList<StoredSetupKey>, int?> _findMatch;

    public SetupKeyAttempt(Func<IReadOnlyList<StoredSetupKey>, int?> findMatch, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(findMatch);

        _findMatch = findMatch;
        Now = now;
    }

    /// <summary>
    /// UTC time of the attempt by the computer's clock. On SQL Server it plays no part: both the
    /// selection of the usable keys and the final UPDATE that uses one up compare the expiry with the
    /// database's clock, at the moment of use. Providers without such a clock (the tests' SQLite) use
    /// this time instead.
    /// </summary>
    public DateTime Now { get; }

    public int? FindMatch(IReadOnlyList<StoredSetupKey> usableKeys) => _findMatch(usableKeys);
}

/// <summary>Result of an attempt to store the first admin.</summary>
public readonly record struct FirstAdminResult(FirstAdminStatus Status, int AdminId);

/// <summary>Raised when a user name is already registered.</summary>
public sealed class DuplicateUserNameException : Exception
{
    public DuplicateUserNameException()
        : base("The user name is already registered.")
    {
    }

    public DuplicateUserNameException(string message)
        : base(message)
    {
    }

    public DuplicateUserNameException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
