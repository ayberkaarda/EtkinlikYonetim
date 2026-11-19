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
    /// check and the insert are one atomic step, so of concurrent setups exactly one succeeds. When the
    /// database already has accounts, <paramref name="authorize"/> is the final decision about the
    /// caller's right to create the first admin: it is called inside the write transaction, after the
    /// checks found no admin and a free user name and while concurrent setups wait, and nothing is
    /// written when it returns <see langword="false"/>. An empty database needs no authorization.
    /// </summary>
    /// <exception cref="DuplicateUserNameException">The user name is taken by another account.</exception>
    Task<FirstAdminResult> AddFirstAdminAsync(User admin, Func<bool> authorize, CancellationToken cancellationToken = default);

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

    /// <summary>The database has accounts and the authorization was refused; nothing was written.</summary>
    NotAuthorized = 2,
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
