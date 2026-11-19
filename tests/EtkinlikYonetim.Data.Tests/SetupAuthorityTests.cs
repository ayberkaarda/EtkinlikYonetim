using System.Data.Common;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// The setup key's authority lives in the database (see <see cref="SqlServerTestDatabase"/>): only the
/// database owner creates keys, with docs/issue-setup-key.sql; the application's login, restricted by
/// docs/grant-app-login.sql, can only check a key and mark it used.
/// </summary>
public sealed class SetupAuthorityTests
{
    private const string AppUser = "EtkinlikYonetimAppTest";

    private const string KeyRows =
        "SELECT STRING_AGG(CONCAT(AnahtarID, N'|', CONVERT(nvarchar(64), AnahtarOzeti, 2), N'|', CONVERT(nvarchar(30), SonKullanma, 126), N'|', Tuketildi, N'|', CONVERT(nvarchar(30), TuketimTarihi, 126)), N';') WITHIN GROUP (ORDER BY AnahtarID) FROM dbo.KurulumAnahtarlari";

    private const string Admins = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin'";

    private const string UnusedKeys = "SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 0 AND TuketimTarihi IS NULL";

    /// <summary>
    /// A database with users but no admin, and two authorities: the owner and the ordinary client (the
    /// application's login with the rights of docs/grant-app-login.sql). The client can neither create,
    /// replace, extend nor delete a key, nor run the issuing script, and the key rows stay unchanged;
    /// the application under that login still starts and creates the first admin with the owner's key,
    /// once. Afterwards the owner's script refuses to create another key.
    /// </summary>
    [Fact]
    public async Task SetupKey_OrdinaryClientCannotIssueOrReplaceAuthority()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using (var owner = database.BuildServices())
        {
            await owner.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
            await owner.GetRequiredService<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        }

        await database.ExecuteAsync($"CREATE USER [{AppUser}] WITHOUT LOGIN", ct);
        var grants = await File.ReadAllTextAsync(SqlServerTestDatabase.RepositoryFile("docs", "grant-app-login.sql"), ct);
        Assert.Contains("N'EtkinlikYonetimApp'", grants, StringComparison.Ordinal);
        await database.ExecuteAsync(grants.Replace("N'EtkinlikYonetimApp'", $"N'{AppUser}'", StringComparison.Ordinal), ct);
        var key = await database.IssueSetupKeyAsync(ct);
        var rows = await database.TextAsync(KeyRows, ct);

        string[] forbidden =
        [
            "INSERT dbo.KurulumAnahtarlari (AnahtarOzeti, Olusturma, SonKullanma, Tuketildi) VALUES (HASHBYTES('SHA2_256', 'X'), SYSUTCDATETIME(), DATEADD(DAY, 1, SYSUTCDATETIME()), 0)",
            "UPDATE dbo.KurulumAnahtarlari SET SonKullanma = DATEADD(YEAR, 1, SonKullanma)",
            "UPDATE dbo.KurulumAnahtarlari SET AnahtarOzeti = HASHBYTES('SHA2_256', 'X')",
            "DELETE dbo.KurulumAnahtarlari",
        ];

        // Impersonation stays on a connection, so these never go back to the pool.
        var unpooled = new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString;
        foreach (var statement in forbidden)
        {
            var denied = await Assert.ThrowsAsync<SqlException>(() => SqlServerTestDatabase.ExecuteAsync(unpooled, $"EXECUTE AS USER = N'{AppUser}'; {statement}", ct));
            Assert.True(denied.Number is 229 or 230, $"Permission denied was expected, got {denied.Number}: {statement}"); // 229 object, 230 column
        }

        await Assert.ThrowsAsync<SqlException>(() => database.IssueSetupKeyAsync(ct, asUser: AppUser));
        await Assert.ThrowsAsync<SqlException>(() => database.IssueSetupKeyAsync(ct, revokeUnused: true, asUser: AppUser));
        Assert.Equal(rows, await database.TextAsync(KeyRows, ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari", ct));

        // The application under that login starts, refuses a key it was not given, and uses the owner's key once.
        await using var app = database.BuildServices(configureContext: b => b.AddInterceptors(new ImpersonateUser(AppUser)), pooling: false);
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var setup = app.GetRequiredService<ISetupService>();
        Assert.Equal(AppUser, await ScalarTextAsync(app, ct));
        var invented = await Assert.ThrowsAsync<BusinessRuleException>(
            () => setup.CreateFirstAdminAsync(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)), "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct));
        Assert.Equal(SetupService.SetupKeyInvalid, invented.Message);
        Assert.Equal(rows, await database.TextAsync(KeyRows, ct));

        Assert.True((await setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct)).IsAdmin);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 AND TuketimTarihi IS NOT NULL", ct));
        Assert.Equal(1, await database.ScalarAsync(Admins, ct));

        // With an admin who signs in with a password, the owner's script creates no key either.
        var refused = await Assert.ThrowsAsync<SqlException>(() => database.IssueSetupKeyAsync(ct));
        Assert.Equal(50001, refused.Number);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari", ct));
    }

    /// <summary>
    /// The owner revokes a key that was lost or shown to someone else (@RevokeUnusedKeys = 1) before it
    /// was used: the old key is refused from then on, the new one works.
    /// </summary>
    [Fact]
    public async Task SetupKey_RevokedByTheOwnerBeforeUse_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        var behind = await database.ClockAsync(TimeSpan.FromDays(-30), ct);
        await using var provider = database.BuildServices(clock: behind);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await provider.GetRequiredService<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        var setup = provider.GetRequiredService<ISetupService>();

        var lost = await database.IssueSetupKeyAsync(ct);
        var kept = await database.IssueSetupKeyAsync(ct);
        var current = await database.IssueSetupKeyAsync(ct, revokeUnused: true);
        Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1", ct));

        foreach (var revoked in new[] { lost, kept })
        {
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(revoked, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct));
            Assert.Equal(SetupService.SetupKeyInvalid, refused.Message);
        }

        Assert.Equal(0, await database.ScalarAsync(Admins, ct));
        Assert.True((await setup.CreateFirstAdminAsync(current, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct)).IsAdmin);
    }

    /// <summary>
    /// A setup that has taken the account lock and used the key holds it until its commit. The owner's
    /// script started meanwhile waits for it instead of deciding on a database without an admin, and
    /// is then refused; a second setup with the same key from another instance also waits and is told
    /// that the setup is done. The admin is the one of the setup that held the lock.
    /// </summary>
    [Fact]
    public async Task SetupInProgress_HoldsTheLock_IssuingAndASecondSetupWaitAndAreRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using (var owner = database.BuildServices())
        {
            await owner.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
            await owner.GetRequiredService<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        }

        var key = await database.IssueSetupKeyAsync(ct);
        var gate = new InsertGate("[Kullanicilar]");
        await using var first = database.BuildServices(configureContext: b => b.AddInterceptors(gate));
        await using var second = database.BuildServices();

        var holding = first.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "birinci", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);

        var issuing = database.IssueSetupKeyAsync(ct);
        await WaitForBlockedRequestsAsync(database, 1, ct);
        var racing = second.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "ikinci", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        await WaitForBlockedRequestsAsync(database, 2, ct);
        Assert.False(issuing.IsCompleted);
        Assert.False(racing.IsCompleted);

        gate.Release();
        Assert.True((await holding).IsAdmin);
        Assert.Equal(50001, (await Assert.ThrowsAsync<SqlException>(() => issuing)).Number);
        Assert.Equal(SetupService.AlreadyDone, (await Assert.ThrowsAsync<BusinessRuleException>(() => racing)).Message);

        Assert.Equal("birinci", await database.TextAsync("SELECT KullaniciAdi FROM dbo.Kullanicilar WHERE Rol = N'Admin'", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari", ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1", ct));
    }

    /// <summary>
    /// The key is valid when the setup reads the usable keys, and expires by the database's clock while
    /// the setup is paused just before its final UPDATE. The UPDATE must refuse it: no admin, and the
    /// key stays unused with no use time. The computer's clock is a month behind the database's,
    /// so a check against the computer's clock would accept the key.
    /// </summary>
    [Fact]
    public async Task SetupKey_ExpiresBeforeFinalUpdate_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using (var owner = database.BuildServices())
        {
            await owner.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
            await owner.GetRequiredService<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        }

        var key = await database.IssueSetupKeyAsync(ct);
        await database.ExecuteAsync("UPDATE dbo.KurulumAnahtarlari SET SonKullanma = DATEADD(SECOND, 3, SYSUTCDATETIME())", ct);
        var pause = new KeyUpdatePause(database);
        await using var app = database.BuildServices(configureContext: b => b.AddInterceptors(pause), clock: await database.ClockAsync(TimeSpan.FromDays(-30), ct));

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => app.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct));

        Assert.True(pause.Paused, "The setup never reached the final UPDATE with the key still valid.");
        Assert.Equal(SetupService.SetupKeyInvalid, refused.Message);
        Assert.Equal(0, await database.ScalarAsync(Admins, ct));
        Assert.Equal(1, await database.ScalarAsync(UnusedKeys, ct));
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 OR TuketimTarihi IS NOT NULL", ct));
    }

    /// <summary>
    /// The computer's clock is behind the database's: by the computer's clock the key would still be
    /// valid, by the database's it has expired. The database decides, so the key is refused and stays
    /// unused; a key issued afterwards works and its use is stamped with the database's time.
    /// </summary>
    [Fact]
    public async Task SetupKey_ExpiredByTheDatabaseClock_IsRefusedWhileTheClientClockIsBehind()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        var behind = await database.ClockAsync(TimeSpan.FromDays(-30), ct);
        await using var provider = database.BuildServices(clock: behind);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await provider.GetRequiredService<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", ct);
        var setup = provider.GetRequiredService<ISetupService>();

        var expired = await database.IssueSetupKeyAsync(ct);
        await database.ExecuteAsync("UPDATE dbo.KurulumAnahtarlari SET SonKullanma = DATEADD(SECOND, -1, SYSUTCDATETIME())", ct);
        var clientNow = behind.GetUtcNow().UtcDateTime;
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE SonKullanma > @client", ct, ("@client", clientNow)));

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(expired, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct));
        Assert.Equal(SetupService.SetupKeyInvalid, refused.Message);
        Assert.Equal(0, await database.ScalarAsync(Admins, ct));
        Assert.Equal(1, await database.ScalarAsync(UnusedKeys, ct));

        var current = await database.IssueSetupKeyAsync(ct);
        Assert.True((await setup.CreateFirstAdminAsync(current, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", ct)).IsAdmin);
        Assert.Equal(1, await database.ScalarAsync(UnusedKeys, ct));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 AND TuketimTarihi > DATEADD(MINUTE, -5, SYSUTCDATETIME())", ct));
    }

    /// <summary>Waits until <paramref name="count"/> requests on the database are blocked by a lock.</summary>
    private static async Task WaitForBlockedRequestsAsync(SqlServerTestDatabase database, int count, CancellationToken ct)
    {
        const string Blocked = "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE database_id = DB_ID(@name) AND blocking_session_id <> 0";
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await database.ScalarAsync(Blocked, ct, ("@name", database.Name)) < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"Fewer than {count} blocked requests.");
            await Task.Delay(25, ct);
        }
    }

    private static async Task<string?> ScalarTextAsync(IServiceProvider provider, CancellationToken ct)
    {
        await using var db = await provider.GetRequiredService<IDbContextFactory<EventDbContext>>().CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT USER_NAME()";
        return await command.ExecuteScalarAsync(ct) as string;
    }

    /// <summary>Runs every command of the context as a database user without a login of its own.</summary>
    private sealed class ImpersonateUser(string user) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var command = Impersonate(connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = Impersonate(connection);
            command.ExecuteNonQuery();
        }

        private DbCommand Impersonate(DbConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = "EXECUTE AS USER = @user";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@user";
            parameter.Value = user;
            command.Parameters.Add(parameter);
            return command;
        }
    }

    /// <summary>
    /// A controlled pause after the usable keys were read: the first UPDATE of the key table waits,
    /// inside the setup's transaction, until every key has expired by the database's clock. The clock
    /// is read on a separate connection that takes no locks.
    /// </summary>
    private sealed class KeyUpdatePause(SqlServerTestDatabase database) : DbCommandInterceptor
    {
        private const string Unexpired = "SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WITH (READUNCOMMITTED) WHERE SonKullanma > SYSUTCDATETIME()";

        private int _fired;

        /// <summary>Whether the UPDATE was reached while a key was still valid.</summary>
        public bool Paused { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("UPDATE", StringComparison.Ordinal)
                && command.CommandText.Contains("KurulumAnahtarlari", StringComparison.Ordinal)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                Paused = await database.ScalarAsync(Unexpired, cancellationToken) > 0;
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (await database.ScalarAsync(Unexpired, cancellationToken) > 0)
                {
                    Assert.True(DateTime.UtcNow < deadline, "The key did not expire.");
                    await Task.Delay(100, cancellationToken);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// A deterministic interleaving point: the first insert into <c>table</c> waits, inside its
    /// transaction and with every lock taken so far, until the test releases it.
    /// </summary>
    private sealed class InsertGate(string table) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _fired;

        public Task Reached => _reached.Task;

        public void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO " + table, StringComparison.Ordinal) && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                _reached.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
