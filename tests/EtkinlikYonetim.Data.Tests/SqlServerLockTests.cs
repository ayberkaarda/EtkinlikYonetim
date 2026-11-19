using System.Diagnostics;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Application locks against SqlClient's command timeout on a real SQL Server instance (see
/// <see cref="SqlServerTestDatabase"/>). The instance under test is configured with a command timeout far
/// below the lock wait, so only the lock code itself can keep a busy lock from turning into a client side
/// timeout.
/// </summary>
public sealed class SqlServerLockTests
{
    private const int ShortCommandTimeoutSeconds = 5;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Another instance holds the event's lock and does not let go. A registration waits for the lock's own
    /// wait (not the command timeout), then gives up with <see cref="DatabaseBusyException"/> and writes
    /// nothing; once the lock is free the same registration goes through.
    /// </summary>
    [Fact]
    public async Task Register_WhileTheEventLockIsHeld_IsBusyAfterTheLockWaitAndWritesNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices(demo: true, configureContext: ShortCommandTimeout(database));
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 1, Ct);
        var eventId = await app.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Meşgul", database.Today.AddDays(3), 5, Ct);
        var registrations = app.GetRequiredService<IRegistrationService>();

        await using (var holder = await HoldLockAsync(database, AppLocks.Event(eventId)))
        {
            var watch = Stopwatch.StartNew();
            await Assert.ThrowsAsync<DatabaseBusyException>(() => registrations.RegisterAsync(Admin(SeedAdminId), eventId, ids[0], Ct));
            watch.Stop();

            Assert.InRange(watch.Elapsed, AppLocks.Wait - TimeSpan.FromSeconds(1), AppLocks.CommandTimeout);
            Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id", Ct, ("@id", eventId)));
        }

        var receipt = await registrations.RegisterAsync(Admin(SeedAdminId), eventId, ids[0], Ct);
        Assert.NotNull(receipt.TicketCode);
    }

    /// <summary>
    /// Another process is upgrading the legacy database and holds the upgrade lock longer than the command
    /// timeout of the instance that starts now. The start waits for the lock (up to its minute) instead of
    /// failing on the command timeout, and then finds the database ready.
    /// </summary>
    [Fact]
    public async Task Initialize_WhileTheUpgradeLockIsHeldLongerThanTheCommandTimeout_WaitsForIt()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync(Ct);
        await database.ExecuteAsync(LegacyUpgradeTests.LegacySchema, Ct);
        await database.ExecuteAsync(LegacyUpgradeTests.LegacyData, Ct);
        await using var app = database.BuildServices(configureContext: ShortCommandTimeout(database));

        Task initialize;
        await using (var holder = await HoldLockAsync(database, "EtkinlikYonetim.LegacySchemaUpgrade"))
        {
            initialize = app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
            await Task.Delay(TimeSpan.FromSeconds(ShortCommandTimeoutSeconds + 3), Ct);
            Assert.False(initialize.IsCompleted, "The start did not wait for the upgrade lock.");
        }

        await initialize;
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_InitialCreate'", Ct));
    }

    private static Action<DbContextOptionsBuilder> ShortCommandTimeout(SqlServerTestDatabase database) =>
        builder => builder.UseSqlServer(database.ConnectionString, sql => sql.CommandTimeout(ShortCommandTimeoutSeconds));

    /// <summary>Takes the exclusive application lock <paramref name="resource"/> on its own connection until disposed.</summary>
    private static async Task<HeldLock> HoldLockAsync(SqlServerTestDatabase database, string resource)
    {
        var connection = new SqlConnection(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(Ct);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(Ct);
        await using var command = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0; SELECT @r;",
            connection,
            transaction);
        command.Parameters.AddWithValue("@resource", resource);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync(Ct))!);
        return new HeldLock(connection, transaction);
    }

    private sealed class HeldLock(SqlConnection connection, SqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
