using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>Brings the database up to date when the application starts.</summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Applies pending EF Core migrations (creating the database when it does not exist). A database
    /// created by the original application is upgraded in place first, keeping its data.
    /// Providers without migrations get the schema from the model. Demonstration data is added only
    /// when <see cref="DataAccessOptions.SeedDemoData"/> is set.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Settings of the data access layer.</summary>
public sealed class DataAccessOptions
{
    /// <summary>
    /// Adds fictional demonstration accounts, events and participants to an empty database. Off by
    /// default: a default installation must not contain any account with a known password.
    /// </summary>
    public bool SeedDemoData { get; init; }
}

internal sealed class DatabaseInitializer(
    IDbContextFactory<EventDbContext> contextFactory,
    DataAccessOptions options) : IDatabaseInitializer
{
    /// <summary>Command timeout of the start-up statements: above the legacy upgrade's 60 second lock wait.</summary>
    internal const int StartupCommandTimeoutSeconds = 300;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (db.Database.IsSqlServer())
            {
                // The legacy upgrade waits up to a minute for another instance's upgrade, and schema changes
                // of a large database take time: the statements of the start-up get more time than the
                // operations, so the client never cancels a statement the server is still working on.
                var seconds = db.Database.GetCommandTimeout() ?? 30; // 30: SqlClient's default, 0: no limit
                if (seconds != 0 && seconds < StartupCommandTimeoutSeconds)
                {
                    db.Database.SetCommandTimeout(StartupCommandTimeoutSeconds);
                }

                // A database from the original application has the tables but no migration history.
                await LegacySchemaUpgrader.UpgradeIfLegacyAsync(db, cancellationToken).ConfigureAwait(false);
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            }

            if (options.SeedDemoData)
            {
                await DemoData.SeedIfEmptyAsync(db, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DatabaseInitializationException(ex);
        }
    }
}

/// <summary>Raised when the database cannot be created or migrated at startup.</summary>
public sealed class DatabaseInitializationException : Exception
{
    public DatabaseInitializationException()
        : base("Database initialization failed.")
    {
    }

    public DatabaseInitializationException(Exception innerException)
        : base("Database initialization failed.", innerException)
    {
    }

    public DatabaseInitializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
