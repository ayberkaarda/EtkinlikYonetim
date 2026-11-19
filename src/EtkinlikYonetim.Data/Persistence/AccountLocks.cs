using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>Locking reads of the first-admin setup, taken inside its serializable transaction.</summary>
internal static class AccountLocks
{
    // The expiry is compared with the database's own clock, the one docs/issue-setup-key.sql sets
    // SonKullanma with, so the computer's clock cannot keep an expired key usable.
    private const string UsableSetupKeys =
        "SELECT * FROM dbo.KurulumAnahtarlari WITH (UPDLOCK, HOLDLOCK) WHERE Tuketildi = 0 AND SonKullanma > SYSUTCDATETIME()";

    /// <summary>
    /// The setup keys usable now (unused and unexpired), locked until the commit on SQL Server. Every
    /// caller holds the account table lock already, so this lock only guards the rows against writers
    /// that do not take it, such as the issuing script revoking a key. On SQL Server "now" is the
    /// database's UTC clock; other providers (the tests' SQLite) have none and use
    /// <paramref name="clientNow"/>, relying on the serializable transaction alone.
    /// </summary>
    public static Task<List<StoredSetupKey>> UsableSetupKeysLockedAsync(EventDbContext db, DateTime clientNow, CancellationToken cancellationToken)
    {
#pragma warning disable EF1002 // A constant statement, never user input.
        var keys = db.Database.IsSqlServer()
            ? db.SetupKeys.FromSqlRaw(UsableSetupKeys)
            : db.SetupKeys.Where(k => !k.Consumed && k.ExpiresAt > clientNow);
#pragma warning restore EF1002
        return keys
            .AsNoTracking()
            .OrderBy(k => k.Id)
            .Select(k => new StoredSetupKey(k.Id, k.KeyHash))
            .ToListAsync(cancellationToken);
    }
}
