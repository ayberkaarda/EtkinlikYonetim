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
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (db.Database.IsSqlServer())
            {
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
