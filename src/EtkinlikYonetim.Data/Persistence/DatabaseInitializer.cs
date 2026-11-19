using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>Brings the database up to date when the application starts.</summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Applies pending EF Core migrations (creating the database when it does not exist). Providers
    /// without migrations get the schema from the model.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

internal sealed class DatabaseInitializer(IDbContextFactory<EventDbContext> contextFactory) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (db.Database.IsSqlServer())
            {
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
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
