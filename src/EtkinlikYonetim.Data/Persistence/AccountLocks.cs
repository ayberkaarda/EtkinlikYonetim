using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>Locking reads of the first-admin setup, taken inside its serializable transaction.</summary>
internal static class AccountLocks
{
    private const string SetupKeys = "SELECT * FROM dbo.KurulumAnahtarlari WITH (UPDLOCK, HOLDLOCK)";

    /// <summary>
    /// The setup keys usable at <paramref name="now"/> (unused and unexpired), locked until the commit
    /// on SQL Server. Every caller holds the account table lock already, so this lock only guards the
    /// rows against writers that do not take it, such as the issuing script revoking a key. Other
    /// providers rely on the serializable transaction alone.
    /// </summary>
    public static Task<List<StoredSetupKey>> UsableSetupKeysLockedAsync(EventDbContext db, DateTime now, CancellationToken cancellationToken)
    {
#pragma warning disable EF1002 // A constant statement, never user input.
        var keys = db.Database.IsSqlServer()
            ? db.SetupKeys.FromSqlRaw(SetupKeys)
            : db.SetupKeys;
#pragma warning restore EF1002
        return keys
            .AsNoTracking()
            .Where(k => !k.Consumed && k.ExpiresAt > now)
            .OrderBy(k => k.Id)
            .Select(k => new StoredSetupKey(k.Id, k.KeyHash))
            .ToListAsync(cancellationToken);
    }
}
