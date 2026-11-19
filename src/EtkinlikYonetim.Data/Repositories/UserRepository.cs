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
}
