using System.Data;
using System.Data.Common;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Repositories;

internal sealed class UserRepository(IDbContextFactory<EventDbContext> contextFactory) : IUserRepository
{
    public async Task<AccountCredentials?> FindByUserNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users
            .AsNoTracking()
            .Where(u => u.UserName == userName)
            .Select(u => new AccountCredentials(u.Id, u.UserName, u.Role, u.PasswordHash, u.PasswordFormat, u.FailedLoginCount, u.LockoutEnd))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> AddAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = new User
        {
            UserName = user.UserName,
            PasswordHash = user.PasswordHash,
            PasswordFormat = user.PasswordFormat,
            Role = user.Role,
        };
        db.Users.Add(entity);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // The unique index rejected a concurrent registration of the same name. Checked with a new
            // query so the provider specific error code does not matter.
            if (await UserNameExistsAsync(user.UserName, CancellationToken.None).ConfigureAwait(false))
            {
                throw new DuplicateUserNameException("The user name is already registered.", ex);
            }

            throw;
        }

        return entity.Id;
    }

    public async Task<bool> AnyAdminAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> AnyUserAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<FirstAdminResult> AddFirstAdminAsync(User admin, Func<bool> authorize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admin);
        ArgumentNullException.ThrowIfNull(authorize);

        try
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Serializable: the account check holds a range lock on the whole table until the commit, so
            // two concurrent setups can never both insert and no account can appear between the check and
            // the insert. On SQL Server the check takes an update lock, which a second setup has to wait
            // for, so the loser sees the winner's row instead of becoming a deadlock victim.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
            var state = await AccountStateLockedAsync(db, cancellationToken).ConfigureAwait(false);
            if (state == AccountState.AdminExists)
            {
                return new FirstAdminResult(FirstAdminStatus.AdminExists, 0);
            }

            if (state == AccountState.UsersOnly)
            {
                // A taken name is refused before the authority is asked, so it does not use up the key.
                var name = admin.UserName;
                if (await db.Users.AnyAsync(u => u.UserName == name, cancellationToken).ConfigureAwait(false))
                {
                    throw new DuplicateUserNameException("The user name is already registered.");
                }

                // Decided while the lock is held: only the setup that gets here first can use the authority.
                if (!authorize())
                {
                    return new FirstAdminResult(FirstAdminStatus.NotAuthorized, 0);
                }
            }

            var entity = new User
            {
                UserName = admin.UserName,
                PasswordHash = admin.PasswordHash,
                PasswordFormat = admin.PasswordFormat,
                Role = UserRole.Admin,
            };
            db.Users.Add(entity);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new FirstAdminResult(FirstAdminStatus.Created, entity.Id);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException || ex.InnerException is DbUpdateException or DbException)
        {
            // Lost the race (deadlock victim or a rejected insert): the winner's row decides; otherwise
            // the unique index rejected a name that another account already has.
            if (await AnyAdminAsync(CancellationToken.None).ConfigureAwait(false))
            {
                return new FirstAdminResult(FirstAdminStatus.AdminExists, 0);
            }

            if (await UserNameExistsAsync(admin.UserName, CancellationToken.None).ConfigureAwait(false))
            {
                throw new DuplicateUserNameException("The user name is already registered.", ex);
            }

            throw;
        }
    }

    public async Task RecordFailedLoginAsync(int userId, int maxAttempts, DateTime lockoutEnd, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // One UPDATE statement, so concurrent failures cannot lose increments.
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.LockoutEnd, u => u.FailedLoginCount + 1 >= maxAttempts ? lockoutEnd : u.LockoutEnd)
                    .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1 >= maxAttempts ? 0 : u.FailedLoginCount + 1),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> RecordSuccessfulLoginAsync(
        AccountCredentials verified,
        DateTime now,
        string? newPasswordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verified);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // The WHERE clause is the final decision: a lock set by a concurrent failure, or a password
        // changed since it was verified, makes the statement match no row and the sign-in fails.
        var id = verified.Id;
        var verifiedHash = verified.PasswordHash;
        var verifiedFormat = verified.PasswordFormat;
        var hash = newPasswordHash ?? verifiedHash;
        var format = newPasswordHash is null ? verifiedFormat : PasswordFormat.Pbkdf2;

        var updated = await db.Users
            .Where(u => u.Id == id
                && (u.LockoutEnd == null || u.LockoutEnd <= now)
                && u.PasswordHash == verifiedHash
                && u.PasswordFormat == verifiedFormat)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.LockoutEnd, (DateTime?)null)
                    .SetProperty(u => u.PasswordHash, hash)
                    .SetProperty(u => u.PasswordFormat, format),
                cancellationToken)
            .ConfigureAwait(false);

        return updated == 1;
    }

    /// <summary>Reads whether the table is empty, has accounts only or has an admin, locking all of it until the commit.</summary>
    private static async Task<AccountState> AccountStateLockedAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            if (await db.Users.AnyAsync(u => u.Role == UserRole.Admin, cancellationToken).ConfigureAwait(false))
            {
                return AccountState.AdminExists;
            }

            return await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false) ? AccountState.UsersOnly : AccountState.Empty;
        }

        // One scan of the whole table under UPDLOCK and HOLDLOCK: 0 empty, 1 accounts only, 2 an admin.
#pragma warning disable EF1002 // Constant statement without any input.
        var state = await db.Database
            .SqlQueryRaw<int>("SELECT ISNULL(MAX(CASE WHEN Rol = N'Admin' THEN 2 ELSE 1 END), 0) AS [Value] FROM dbo.Kullanicilar WITH (UPDLOCK, HOLDLOCK)")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002
        return (AccountState)state;
    }

    private enum AccountState
    {
        Empty = 0,
        UsersOnly = 1,
        AdminExists = 2,
    }
}
