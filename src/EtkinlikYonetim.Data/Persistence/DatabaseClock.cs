using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// SQL Server's UTC clock (SYSUTCDATETIME), the one the setup key script also uses. Other providers
/// (the tests' SQLite) have no server clock and use <see cref="TimeProvider"/> instead.
/// </summary>
internal sealed class DatabaseClock(IDbContextFactory<EventDbContext> contextFactory, TimeProvider timeProvider) : IDatabaseClock
{
    public async Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return (await DatabaseTime.NowAsync(db, timeProvider, cancellationToken).ConfigureAwait(false)).Utc;
    }
}
