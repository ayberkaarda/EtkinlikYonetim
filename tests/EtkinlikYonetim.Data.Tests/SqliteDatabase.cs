using EtkinlikYonetim.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// A private SQLite in-memory database wired through the application's own registrations.
/// The connection stays open for the lifetime of the instance, which keeps the database alive.
/// The schema comes from the EF Core model. With <c>demo</c> on (the default for these tests) the
/// demonstration data is added as with ETKINLIK_DEMO=1 (2 users, 3 events, 3 participants, no
/// registrations); without it the database is empty, as in a default installation.
/// </summary>
public sealed class SqliteDatabase(bool demo = true) : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private ServiceProvider? _provider;

    public FakeTimeProvider Clock { get; } = new(TestServices.Start);

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized.");

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    public EventDbContext CreateContext() =>
        Services.GetRequiredService<IDbContextFactory<EventDbContext>>().CreateDbContext();

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddDataAccess(options => options.UseSqlite(_connection), new DataAccessOptions { SeedDemoData = demo });
        services.AddTestBusinessServices(Clock);
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        await Get<IDatabaseInitializer>().InitializeAsync();
    }

    /// <summary>
    /// Another, independent set of the application's services on the same database, as a second
    /// application instance (another Windows profile or computer) has; the caller disposes it.
    /// </summary>
    public ServiceProvider BuildServices(params IInterceptor[] interceptors) => BuildServices(null, interceptors);

    /// <summary>As <see cref="BuildServices(IInterceptor[])"/>, with registrations of the caller replacing the defaults.</summary>
    public ServiceProvider BuildServices(Action<IServiceCollection>? configure, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddDataAccess(options => options.UseSqlite(_connection).AddInterceptors(interceptors), new DataAccessOptions { SeedDemoData = false });
        services.AddTestBusinessServices(Clock);
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }
}
