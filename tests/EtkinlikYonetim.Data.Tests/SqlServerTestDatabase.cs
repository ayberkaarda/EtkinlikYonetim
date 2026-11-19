using System.Globalization;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Throw-away database on a real SQL Server instance. Set ETKINLIK_TEST_SQLSERVER to a server-level
/// connection string to choose the instance (default: local SQL Express with Windows authentication)
/// and ETKINLIK_SKIP_SQL_TESTS=1 to skip every test that needs SQL Server. With
/// ETKINLIK_REQUIRE_SQL_TESTS=1 (the SQL Server job of the CI) an unreachable server fails the tests
/// instead of skipping them.
/// </summary>
internal sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private const string ServerEnvironmentVariable = "ETKINLIK_TEST_SQLSERVER";
    private const string SkipEnvironmentVariable = "ETKINLIK_SKIP_SQL_TESTS";
    private const string RequireEnvironmentVariable = "ETKINLIK_REQUIRE_SQL_TESTS";
    private const string DefaultServer =
        @"Server=localhost\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5;";

    private SqlServerTestDatabase(string server, string name)
    {
        Server = server;
        Name = name;
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
    }

    public string Server { get; }

    public string Name { get; }

    public string ConnectionString { get; }

    public FakeTimeProvider Clock { get; } = new(TestServices.Start);

    /// <summary>
    /// Creates an empty database with the case-insensitive Turkish collation of the original
    /// application's database, or skips the test when no SQL Server is available.
    /// </summary>
    public static async Task<SqlServerTestDatabase> CreateAsync(CancellationToken ct)
    {
        var database = await ReserveAsync(ct);
        await ExecuteAsync(database.Server, "DECLARE @sql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@name) + N' COLLATE Turkish_CI_AS'; EXEC(@sql);", ct, ("@name", database.Name));
        return database;
    }

    /// <summary>Picks a server and a unique database name without creating the database.</summary>
    public static async Task<SqlServerTestDatabase> ReserveAsync(CancellationToken ct)
    {
        var server = await ResolveServerOrSkipAsync(ct);
        return new SqlServerTestDatabase(server, "EtkinlikYonetimTest_" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The application's services on this database. Each provider has its own connection pool, so
    /// two providers behave like two application instances. <paramref name="demo"/> turns the
    /// demonstration data on, as ETKINLIK_DEMO=1 does for the application.
    /// </summary>
    /// <paramref name="configureContext"/> adds to the context options (for example interceptors) and
    /// <paramref name="pooling"/> off keeps its connections out of the shared pool (needed when they
    /// impersonate another database user).
    public ServiceProvider BuildServices(
        Action<IServiceCollection>? configure = null,
        bool demo = false,
        Action<DbContextOptionsBuilder>? configureContext = null,
        bool pooling = true)
    {
        var connectionString = pooling
            ? ConnectionString
            : new SqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString;
        var services = new ServiceCollection();
        services.AddDataAccess(
            builder =>
            {
                builder.UseSqlServer(connectionString);
                configureContext?.Invoke(builder);
            },
            new DataAccessOptions { SeedDemoData = demo });
        services.AddTestBusinessServices(Clock);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters) =>
        ExecuteAsync(ConnectionString, sql, ct, parameters);

    public Task<int> ScalarAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters) =>
        ScalarAsync(ConnectionString, sql, ct, parameters);

    public async Task<string?> TextAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(ct) as string;
    }

    public static async Task ExecuteAsync(
        string connectionString,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<int> ScalarAsync(
        string connectionString,
        string sql,
        CancellationToken ct,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Runs docs/issue-setup-key.sql as the database owner (or with <paramref name="asUser"/>
    /// impersonated) and returns the key it shows once.
    /// </summary>
    public async Task<string> IssueSetupKeyAsync(CancellationToken ct, bool revokeUnused = false, string? asUser = null)
    {
        var script = await File.ReadAllTextAsync(RepositoryFile("docs", "issue-setup-key.sql"), ct);
        if (revokeUnused)
        {
            Assert.Contains("DECLARE @RevokeUnusedKeys bit = 0;", script, StringComparison.Ordinal);
            script = script.Replace("DECLARE @RevokeUnusedKeys bit = 0;", "DECLARE @RevokeUnusedKeys bit = 1;", StringComparison.Ordinal);
        }

        if (asUser is not null)
        {
            script = "EXECUTE AS USER = N'" + asUser + "';\n" + script;
        }

        // Impersonation stays on a connection, so such a connection never goes back to the pool.
        await using var connection = new SqlConnection(asUser is null
            ? ConnectionString
            : new SqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(script, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct));
        return reader.GetString(0);
    }

    /// <summary>A file of the repository, found by walking up from the test output folder.</summary>
    public static string RepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(parts));
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(
            Server,
            """
            IF DB_ID(@name) IS NOT NULL
            BEGIN
                DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name) + N';';
                EXEC(@sql);
            END
            """,
            CancellationToken.None,
            ("@name", Name));
    }

    private static async Task<string> ResolveServerOrSkipAsync(CancellationToken ct)
    {
        if (IsYes(Environment.GetEnvironmentVariable(SkipEnvironmentVariable)))
        {
            Assert.Skip($"{SkipEnvironmentVariable} is set.");
        }

        var server = Environment.GetEnvironmentVariable(ServerEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(server))
        {
            server = DefaultServer;
        }

        try
        {
            await using var connection = new SqlConnection(server);
            await connection.OpenAsync(ct);
        }
        catch (SqlException ex) when (!IsYes(Environment.GetEnvironmentVariable(RequireEnvironmentVariable)))
        {
            Assert.Skip($"No SQL Server available ({ex.Number}). Set {ServerEnvironmentVariable} to run this test.");
        }

        return server;
    }

    private static bool IsYes(string? value) =>
        value is "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
