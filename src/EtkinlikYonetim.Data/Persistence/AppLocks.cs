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

    /// <summary>How long a writer waits for the lock before it gives up with <see cref="DatabaseBusyException"/>.</summary>
    internal static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The statement that waits for the lock gets at least this much time on the client: the lock's own
    /// wait plus a margin, so the server always answers "not granted" before the client gives up on the
    /// statement. Otherwise a busy lock would surface as a client side command timeout.
    /// </summary>
    internal static readonly TimeSpan CommandTimeout = Wait + TimeSpan.FromSeconds(30);

    // SqlClient's default when the context sets no command timeout.
    private const int DefaultCommandTimeoutSeconds = 30;

    /// <summary>Resource of the seats, waiting list and registrations of one event.</summary>
    public static string Event(int eventId) =>
        "EtkinlikYonetim.Event." + eventId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Takes the exclusive lock on <paramref name="resource"/> for the rest of the current transaction,
    /// which the caller must have begun. Throws <see cref="DatabaseBusyException"/> when the lock was not
    /// granted, before anything is written.
    /// </summary>
    public static async Task AcquireAsync(EventDbContext db, string resource, CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        var name = new SqlParameter("@resource", SqlDbType.NVarChar, 255) { Value = resource };
        var timeout = new SqlParameter("@timeout", SqlDbType.Int) { Value = (int)Wait.TotalMilliseconds };
        var configured = db.Database.GetCommandTimeout();
        var needed = (int)CommandTimeout.TotalSeconds;
        var effective = configured ?? DefaultCommandTimeoutSeconds;
        var raise = effective != 0 && effective < needed; // 0 waits without limit
        if (raise)
        {
            db.Database.SetCommandTimeout(needed);
        }

        try
        {
#pragma warning disable EF1002 // Constant statement; the values are parameters.
            await db.Database.ExecuteSqlRawAsync(
                "EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @timeout;",
                [result, name, timeout],
                cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
        }
        finally
        {
            if (raise)
            {
                db.Database.SetCommandTimeout(configured);
            }
        }

        if (result.Value is not int code || code < 0)
        {
            throw new DatabaseBusyException();
        }
    }
}

/// <summary>
/// Another writer held the lock of the same event or of the accounts for longer than the wait; nothing was
/// changed and the operation can simply be tried again.
/// </summary>
public sealed class DatabaseBusyException : TimeoutException
{
    public DatabaseBusyException()
        : base("The database is busy with another change of the same data; nothing was changed. Try again.")
    {
    }

    public DatabaseBusyException(string message)
        : base(message)
    {
    }

    public DatabaseBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
