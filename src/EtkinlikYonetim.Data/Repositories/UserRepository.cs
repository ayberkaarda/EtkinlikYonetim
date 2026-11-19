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
            .Select(u => new AccountCredentials(u.Id, u.UserName, u.Role, u.PasswordHash, u.PasswordFormat, u.FailedLoginCount, u.LockoutEnd, u.Disabled, u.SessionVersion))
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

    public async Task<FirstAdminResult> AddFirstAdminAsync(
        User admin,
        SetupKeyAttempt? setupKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admin);

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
                if (setupKey is null)
                {
                    return new FirstAdminResult(FirstAdminStatus.KeyRequired, 0);
                }

                // A taken name is refused before the authority is asked, so it does not use up the key.
                var name = admin.UserName;
                if (await db.Users.AnyAsync(u => u.UserName == name, cancellationToken).ConfigureAwait(false))
                {
                    throw new DuplicateUserNameException("The user name is already registered.");
                }

                // Decided while the lock is held: only the setup that gets here first can use the key, and
                // the key is used up in this transaction, so a failed insert or commit leaves it unused.
                // A wrong key writes nothing and is rolled back with the rest.
                if (!await UseSetupKeyAsync(db, setupKey, cancellationToken).ConfigureAwait(false))
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

        // The WHERE clause is the final decision: a lock set by a concurrent failure, an account disabled,
        // a password changed or any change of the account's access since it was verified (also one undone
        // again) makes the statement match no row and the sign-in fails.
        var id = verified.Id;
        var verifiedHash = verified.PasswordHash;
        var verifiedFormat = verified.PasswordFormat;
        var verifiedVersion = verified.SessionVersion;
        var hash = newPasswordHash ?? verifiedHash;
        var format = newPasswordHash is null ? verifiedFormat : PasswordFormat.Pbkdf2;

        var updated = await db.Users
            .Where(u => u.Id == id
                && !u.Disabled
                && (u.LockoutEnd == null || u.LockoutEnd <= now)
                && u.PasswordHash == verifiedHash
                && u.PasswordFormat == verifiedFormat
                && u.SessionVersion == verifiedVersion)
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

    public async Task<SessionState?> GetSessionStateAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new SessionState(u.Role, u.Disabled, u.SessionVersion))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .ThenBy(u => u.Id)
            .Select(u => new AccountSummary(u.Id, u.UserName, u.Role, u.Disabled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<AccountChangeStatus> SetDisabledAsync(int actorId, int targetId, bool disabled, CancellationToken cancellationToken = default) =>
        ChangeAccountAsync(
            actorId,
            targetId,
            target => !disabled || !IsEnabledAdmin(target),
            accounts => accounts.SetProperty(u => u.Disabled, disabled),
            cancellationToken);

    public Task<AccountChangeStatus> SetRoleAsync(int actorId, int targetId, UserRole role, CancellationToken cancellationToken = default) =>
        ChangeAccountAsync(
            actorId,
            targetId,
            target => role == UserRole.Admin || !IsEnabledAdmin(target),
            accounts => accounts.SetProperty(u => u.Role, role),
            cancellationToken);

    private static bool IsEnabledAdmin(SessionState account) => account.Role == UserRole.Admin && !account.Disabled;

    /// <summary>
    /// Runs one account change under the account lock: the actor is checked, the target read and, when
    /// <paramref name="keepsEveryAdmin"/> says the change may remove an enabled admin, the other enabled
    /// admins counted, all in the transaction that writes. Two admins who demote or disable each other at
    /// the same time are therefore decided one after the other, and the second finds itself no longer
    /// allowed or the last one.
    /// </summary>
    private async Task<AccountChangeStatus> ChangeAccountAsync(
        int actorId,
        int targetId,
        Func<SessionState, bool> keepsEveryAdmin,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<User>> change,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AppLocks.AcquireAsync(db, AppLocks.Accounts, cancellationToken).ConfigureAwait(false);

        var states = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == actorId || u.Id == targetId)
            .Select(u => new { u.Id, State = new SessionState(u.Role, u.Disabled) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var actor = states.FirstOrDefault(s => s.Id == actorId)?.State;
        if (actor is null || !IsEnabledAdmin(actor))
        {
            return AccountChangeStatus.ActorNotAuthorized;
        }

        if (states.FirstOrDefault(s => s.Id == targetId)?.State is not { } target)
        {
            return AccountChangeStatus.NotFound;
        }

        if (!keepsEveryAdmin(target)
            && !await db.Users.AnyAsync(u => u.Id != targetId && u.Role == UserRole.Admin && !u.Disabled, cancellationToken).ConfigureAwait(false))
        {
            return AccountChangeStatus.LastActiveAdmin;
        }

        // Every change of access also raises the session version, which ends the account's open sessions
        // for good: undoing the change later does not bring them back.
        await db.Users
            .Where(u => u.Id == targetId)
            .ExecuteUpdateAsync(
                accounts =>
                {
                    change(accounts);
                    accounts.SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1);
                },
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return AccountChangeStatus.Changed;
    }

    /// <summary>
    /// Checks <paramref name="attempt"/> against the usable setup keys and, when one matches, uses it up
    /// with a conditional UPDATE of Tuketildi and TuketimTarihi (the only columns the application's
    /// login may change) inside the caller's transaction, so the use commits or rolls back with the
    /// admin write. A wrong key writes nothing: there is no attempt counter that a guesser could exhaust.
    /// </summary>
    private static async Task<bool> UseSetupKeyAsync(EventDbContext db, SetupKeyAttempt attempt, CancellationToken cancellationToken)
    {
        var keys = await AccountLocks.UsableSetupKeysLockedAsync(db, attempt.Now, cancellationToken).ConfigureAwait(false);
        if (attempt.FindMatch(keys) is not { } id)
        {
            return false;
        }

        // The WHERE clause is the final decision: of two transactions with the same key only one can
        // match the unused row.
        if (db.Database.IsSqlServer())
        {
            // The expiry is decided again at the moment of use, against the database's clock (the one
            // the issuing script set SonKullanma with), and the use is stamped with it: a key that
            // expired after it was read matches no row, and the computer's clock plays no part.
            var used = await db.Database.ExecuteSqlAsync(
                $"""
                UPDATE dbo.KurulumAnahtarlari
                SET Tuketildi = 1, TuketimTarihi = SYSUTCDATETIME()
                WHERE AnahtarID = {id} AND Tuketildi = 0 AND SonKullanma > SYSUTCDATETIME()
                """,
                cancellationToken).ConfigureAwait(false);
            return used == 1;
        }

        // Other providers (the tests' SQLite) have no database clock and use the attempt's time.
        var now = attempt.Now;
        var consumed = await db.SetupKeys
            .Where(k => k.Id == id && !k.Consumed && k.ExpiresAt > now)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(k => k.Consumed, true)
                    .SetProperty(k => k.ConsumedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
        return consumed == 1;
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
