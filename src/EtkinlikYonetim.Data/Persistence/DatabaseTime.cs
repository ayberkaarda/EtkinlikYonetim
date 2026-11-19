using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>The database's UTC time and the local calendar day it falls on, read once by one write.</summary>
internal readonly record struct DatabaseInstant(DateTime Utc, DateOnly Today);

/// <summary>
/// Reads the database's clock on the connection of a context, so a write reads it inside its own
/// transaction, after its locks: a write that waited for a lock across midnight decides with the day it
/// writes on, and stores the time it wrote at, not a time read before it waited.
/// </summary>
internal static class DatabaseTime
{
    /// <summary>
    /// SQL Server's SYSUTCDATETIME() and the calendar day it falls on in the local time zone of
    /// <paramref name="timeProvider"/> (event dates are local calendar days). Providers without a server
    /// clock (the tests' SQLite) use <paramref name="timeProvider"/>'s time instead.
    /// </summary>
    public static async Task<DatabaseInstant> NowAsync(EventDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var utc = db.Database.IsSqlServer()
            ? await db.Database
                .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
                .SingleAsync(cancellationToken)
                .ConfigureAwait(false)
            : timeProvider.GetUtcNow().UtcDateTime;
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DatabaseInstant(utc, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, timeProvider.LocalTimeZone)));
    }
}
