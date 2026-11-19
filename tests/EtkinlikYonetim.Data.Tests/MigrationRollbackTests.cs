using EtkinlikYonetim.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Rolling back ScopeNationalIdToOwner on SQL Server (see <see cref="SqlServerTestDatabase"/>).</summary>
public sealed class MigrationRollbackTests
{
    private const string InitialCreate = "20261119024708_InitialCreate";
    private const string OwnerIndex = "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Katilimcilar_KullaniciId_TcKimlikNo'";
    private const string GlobalIndex = "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Katilimcilar_TcKimlikNo'";

    /// <summary>
    /// When two accounts have a participant with the same national id, the rollback refuses before it
    /// changes the participants: both rows and the per-account index stay. Once the rows are resolved
    /// by hand it goes back to the global index.
    /// </summary>
    [Fact]
    public async Task Down_SharedNationalIdAcrossAccounts_StopsWithoutChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await database.ExecuteAsync(
            "INSERT dbo.Katilimcilar (TcKimlikNo, AdSoyad, KullaniciId) VALUES (@tc, N'Aynı Kişi', @owner)",
            ct,
            ("@tc", DenizNationalId),
            ("@owner", SeedAdminId));
        const string Shared = "SELECT COUNT(*) FROM dbo.Katilimcilar WHERE TcKimlikNo = @tc";

        var refused = await Assert.ThrowsAnyAsync<Exception>(() => MigrateToAsync(provider, InitialCreate, ct));
        Assert.Contains("resolve them by hand", refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, await database.ScalarAsync(Shared, ct, ("@tc", DenizNationalId)));
        Assert.Equal(1, await database.ScalarAsync(OwnerIndex, ct));
        Assert.Equal(0, await database.ScalarAsync(GlobalIndex, ct));

        await database.ExecuteAsync("DELETE dbo.Katilimcilar WHERE TcKimlikNo = @tc AND KullaniciId = @owner", ct, ("@tc", DenizNationalId), ("@owner", SeedAdminId));
        await MigrateToAsync(provider, InitialCreate, ct);
        Assert.Equal(0, await database.ScalarAsync(OwnerIndex, ct));
        Assert.Equal(1, await database.ScalarAsync(GlobalIndex, ct));
    }

    private static async Task MigrateToAsync(IServiceProvider provider, string migration, CancellationToken ct)
    {
        await using var db = await provider.GetRequiredService<IDbContextFactory<EventDbContext>>().CreateDbContextAsync(ct);
        await db.GetService<IMigrator>().MigrateAsync(migration, ct);
    }
}
