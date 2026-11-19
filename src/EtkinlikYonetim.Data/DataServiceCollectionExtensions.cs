using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Data.Repositories;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtkinlikYonetim.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>Environment variable that overrides the configured connection string.</summary>
    public const string ConnectionEnvironmentVariable = "ETKINLIK_CONNECTION";

    /// <summary>Environment variable that turns the demonstration data on ("1" or "true").</summary>
    public const string DemoEnvironmentVariable = "ETKINLIK_DEMO";

    /// <summary>
    /// Seconds SqlClient lets a statement run before it cancels it. Well above the wait for an application
    /// lock, so a busy lock is reported as such and not as a client side timeout.
    /// </summary>
    public const int CommandTimeoutSeconds = 60;

    /// <summary>Registers the data access layer on SQL Server.</summary>
    public static IServiceCollection AddSqlServerDataAccess(
        this IServiceCollection services,
        string connectionString,
        DataAccessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return services.AddDataAccess(builder => builder.UseSqlServer(connectionString, sql => sql.CommandTimeout(CommandTimeoutSeconds)), options);
    }

    /// <summary>
    /// Registers the data access layer with a caller supplied provider configuration, so tests can
    /// plug in another provider.
    /// </summary>
    public static IServiceCollection AddDataAccess(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider,
        DataAccessOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddDbContextFactory<EventDbContext>(configureProvider);
        services.AddSingleton(options ?? new DataAccessOptions());
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IEventRepository, EventRepository>();
        services.AddSingleton<IParticipantRepository, ParticipantRepository>();
        services.AddSingleton<IRegistrationRepository, RegistrationRepository>();
        services.AddSingleton<ITicketRepository, TicketRepository>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDatabaseClock, DatabaseClock>();
        return services;
    }
}
