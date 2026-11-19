using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Transaction scoped application locks (sp_getapplock) that serialize the writes of one resource across
/// every application instance: the account changes, or everything that moves the seats of one event.
/// Providers without application locks (the tests' SQLite) serialize writers by themselves, so the lock
/// is skipped there.
/// </summary>
internal static class AppLocks
{
    /// <summary>Resource of every account change (role, enabled state).</summary>
    public const string Accounts = "EtkinlikYonetim.Accounts";

    private const int TimeoutMilliseconds = 30_000;

    /// <summary>Resource of the seats, waiting list and registrations of one event.</summary>
    public static string Event(int eventId) =>
        "EtkinlikYonetim.Event." + eventId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Takes the exclusive lock on <paramref name="resource"/> for the rest of the current transaction,
    /// which the caller must have begun. Throws when the lock was not granted, before anything is written.
    /// </summary>
    public static async Task AcquireAsync(EventDbContext db, string resource, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var name = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource };
        var timeout = new SqlParameter("@timeout", SqlDbType.Int) { Value = TimeoutMilliseconds };
#pragma warning disable EF1002 // Constant statement; the values are parameters.
        await db.Database.ExecuteSqlRawAsync(
            "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
            [result, name, timeout],
            cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002

        if (result.Value is not int code || code < 0)
        {
            throw new TimeoutException("A lock of the event database could not be acquired; nothing was changed. Try again.");
        }
    }
}
