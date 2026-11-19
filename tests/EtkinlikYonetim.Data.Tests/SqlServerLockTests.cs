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

        await using (var holder = await HeldAppLock.TakeAsync(database, AppLocks.Event(eventId), Ct))
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
        await using (var holder = await HeldAppLock.TakeAsync(database, "EtkinlikYonetim.LegacySchemaUpgrade", Ct))
        {
            initialize = app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
            await Task.Delay(TimeSpan.FromSeconds(ShortCommandTimeoutSeconds + 3), Ct);
            Assert.False(initialize.IsCompleted, "The start did not wait for the upgrade lock.");
        }

        await initialize;
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId LIKE N'%_InitialCreate'", Ct));
    }

    /// <summary>
    /// A transaction chosen as a deadlock victim (error 1205) wrote nothing and can be tried again; it is
    /// recognised, so the screens answer with the busy message instead of a database failure.
    /// </summary>
    [Fact]
    public async Task DeadlockVictim_IsRecognised()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices(demo: true);
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);

        await using var first = new SqlConnection(database.ConnectionString);
        await using var second = new SqlConnection(database.ConnectionString);
        await first.OpenAsync(Ct);
        await second.OpenAsync(Ct);
        await using var firstTransaction = (SqlTransaction)await first.BeginTransactionAsync(Ct);
        await using var secondTransaction = (SqlTransaction)await second.BeginTransactionAsync(Ct);
        await TouchAsync(first, firstTransaction, SeedAdminId);
        await TouchAsync(second, secondTransaction, SeedUserId);

        // Each waits for the row the other holds: SQL Server ends one of them with error 1205.
        var results = await Task.WhenAll(
            CaptureAsync(() => TouchAsync(first, firstTransaction, SeedUserId)),
            CaptureAsync(() => TouchAsync(second, secondTransaction, SeedAdminId)));

        var victim = Assert.Single(results, r => r is not null);
        Assert.True(DatabaseErrors.IsDeadlock(victim));
        Assert.True(DatabaseErrors.IsDeadlock(new InvalidOperationException("wrapped", victim)));
        Assert.False(DatabaseErrors.IsDeadlock(new TimeoutException()));

        static async Task TouchAsync(SqlConnection connection, SqlTransaction transaction, int id)
        {
            await using var command = new SqlCommand("UPDATE dbo.Kullanicilar SET BasarisizGiris = BasarisizGiris WHERE Id = @id;", connection, transaction);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync(Ct);
        }

        static async Task<Exception?> CaptureAsync(Func<Task> operation)
        {
            try
            {
                await Task.Run(operation, Ct);
                return null;
            }
            catch (SqlException ex)
            {
                return ex;
            }
        }
    }

    private static Action<DbContextOptionsBuilder> ShortCommandTimeout(SqlServerTestDatabase database) =>
        builder => builder.UseSqlServer(database.ConnectionString, sql => sql.CommandTimeout(ShortCommandTimeoutSeconds));
}
