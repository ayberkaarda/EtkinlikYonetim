using System.Data;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Optional demonstration data, inserted only when it is asked for explicitly (ETKINLIK_DEMO=1 or
/// <c>Seed:Demo</c> = true) and only into a database without any account, event or participant. The
/// migrations themselves create no account, so a default installation has no known password; its
/// first admin is created by the first-run setup.
/// </summary>
/// <remarks>
/// The people and events are fictional, the national ids are synthetic numbers that merely pass the
/// checksum and the e-mail addresses use the reserved example.com domain. The password hashes are
/// precomputed so the data is deterministic (admin: "Demo.Admin1", ornek: "Demo.Kullanici1"); they
/// are meant for trying the application out and must never be enabled on a real installation.
/// </remarks>
internal static class DemoData
{
    /// <summary>
    /// Adds the demonstration rows when the database is empty. Returns <see langword="false"/> when
    /// it already holds data; nothing is changed then. The emptiness check and the inserts share one
    /// serializable transaction (with update locks on SQL Server), so concurrent starts add the rows once.
    /// </summary>
    public static async Task<bool> SeedIfEmptyAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            db,
            static async (context, token) =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token).ConfigureAwait(false);
                if (!await IsEmptyAsync(context, token).ConfigureAwait(false))
                {
                    await transaction.RollbackAsync(token).ConfigureAwait(false);
                    return false;
                }

                // One row per save keeps the identity values in this order on a new database.
                var admin = await AddAsync(context, User("admin", UserRole.Admin, "PBKDF2-SHA256$600000$YpX54aoSvQPCiUKqlMMoGA==$MH93hF8SQ80mWaKR6Y+dpuj10DcfJLUBryuy8ZdEnhY="), token).ConfigureAwait(false);
                var sample = await AddAsync(context, User("ornek", UserRole.User, "PBKDF2-SHA256$600000$Xm3uBwS1RHIIhXfHqUMdzw==$gfpLWUifJbAP+nhdV43Lr5lHCog4znWXgpJF+kyjiRw="), token).ConfigureAwait(false);

                await AddAsync(context, new ScheduledEvent { Name = "Bahar Konseri", Date = new DateOnly(2027, 4, 17), Capacity = 150 }, token).ConfigureAwait(false);
                await AddAsync(context, new ScheduledEvent { Name = "Yazılım Atölyesi", Date = new DateOnly(2027, 5, 8), Capacity = 30 }, token).ConfigureAwait(false);
                await AddAsync(context, new ScheduledEvent { Name = "Kısa Film Gösterimi", Date = new DateOnly(2027, 6, 12), Capacity = 2 }, token).ConfigureAwait(false);

                await AddAsync(context, Participant("10000000146", "Deniz Örnek", "deniz@example.com", sample.Id), token).ConfigureAwait(false);
                await AddAsync(context, Participant("10000000214", "Ece Taslak", "ece@example.com", sample.Id), token).ConfigureAwait(false);
                await AddAsync(context, Participant("10000000382", "Mert Deneme", "mert@example.com", admin.Id), token).ConfigureAwait(false);

                await transaction.CommitAsync(token).ConfigureAwait(false);
                return true;
            },
            verifySucceeded: null,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsEmptyAsync(EventDbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return !await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false)
                && !await db.Events.AnyAsync(cancellationToken).ConfigureAwait(false)
                && !await db.Participants.AnyAsync(cancellationToken).ConfigureAwait(false);
        }

#pragma warning disable EF1002 // Constant statement without any input.
        var rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT (SELECT COUNT(*) FROM dbo.Kullanicilar WITH (UPDLOCK, HOLDLOCK))
                     + (SELECT COUNT(*) FROM dbo.Etkinlikler WITH (UPDLOCK, HOLDLOCK))
                     + (SELECT COUNT(*) FROM dbo.Katilimcilar WITH (UPDLOCK, HOLDLOCK)) AS [Value]
                """)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002
        return rows == 0;
    }

    private static async Task<T> AddAsync<T>(EventDbContext db, T entity, CancellationToken cancellationToken)
        where T : class
    {
        db.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    private static User User(string userName, UserRole role, string hash) =>
        new() { UserName = userName, Role = role, PasswordHash = hash, PasswordFormat = PasswordFormat.Pbkdf2 };

    private static Participant Participant(string nationalId, string fullName, string email, int ownerUserId) =>
        new() { NationalId = nationalId, FullName = fullName, Email = email, OwnerUserId = ownerUserId };
}
