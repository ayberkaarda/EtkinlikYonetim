using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Checks the account a write acts for against the stored account, inside the write's transaction. The
/// services check the session when an operation starts; this check repeats it where the write happens, so
/// an account disabled, demoted or changed and changed back while the write waited for its lock changes
/// nothing.
/// </summary>
/// <remarks>
/// Lock order: every write takes its resource lock (the event's or the accounts' application lock) first
/// and checks the actor after it. The check share-locks the account row until the commit, so an account
/// change of the actor waits for the write to end instead of slipping in between the check and the write;
/// the account change itself never waits for an event lock, so the two cannot wait for each other.
/// </remarks>
internal static class Actors
{
    /// <summary>
    /// Whether <paramref name="actor"/>'s account exists, is enabled and still has the actor's role and
    /// session version and, with <paramref name="admin"/>, the admin role. On SQL Server the row stays
    /// share-locked until the caller's transaction ends (HOLDLOCK, a locking read also under read committed
    /// snapshot).
    /// </summary>
    public static async Task<bool> IsCurrentAsync(EventDbContext db, Actor actor, bool admin, CancellationToken cancellationToken)
    {
        var (actorId, role, version) = actor;
        if (admin && role != UserRole.Admin)
        {
            return false;
        }

        if (!db.Database.IsSqlServer())
        {
            return await db.Users
                .AnyAsync(u => u.Id == actorId && u.Role == role && !u.Disabled && u.SessionVersion == version, cancellationToken)
                .ConfigureAwait(false);
        }

        var stored = role == UserRole.Admin ? SchemaLimits.AdminRoleValue : SchemaLimits.UserRoleValue;
        return await db.Database
            .SqlQuery<int>(
                $"SELECT COUNT(*) AS [Value] FROM dbo.Kullanicilar WITH (HOLDLOCK) WHERE Id = {actorId} AND Rol = {stored} AND DevreDisi = 0 AND OturumSurumu = {version}")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// As <see cref="IsCurrentAsync"/>, throwing <see cref="ActorRevokedException"/> when the actor is no
    /// longer current. Called inside the caller's transaction, after its resource lock and before anything
    /// is written.
    /// </summary>
    public static async Task RequireAsync(EventDbContext db, Actor actor, bool admin, CancellationToken cancellationToken)
    {
        if (!await IsCurrentAsync(db, actor, admin, cancellationToken).ConfigureAwait(false))
        {
            throw new ActorRevokedException();
        }
    }
}
