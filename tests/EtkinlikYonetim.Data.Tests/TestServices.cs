using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Shared registrations and sample values for tests that run the real services on a real database.</summary>
internal static class TestServices
{
    /// <summary>Monday 2 March 2026, 10:10; the fake clock's local time zone is UTC.</summary>
    public static readonly DateTimeOffset Start = new(2026, 3, 2, 10, 10, 0, TimeSpan.Zero);

    // Demonstration data (see DemoData, used with demo mode on): "admin" (1, admin) and "ornek" (2, user); events 1-3 in 2027, event 3
    // with two seats; participants 1 and 2 owned by "ornek", participant 3 owned by "admin".
    public const int SeedAdminId = 1;
    public const int SeedUserId = 2;
    public const int ConcertId = 1;
    public const int SmallEventId = 3;
    public const string DenizNationalId = "10000000146";
    public const string EceNationalId = "10000000214";
    public const string MertNationalId = "10000000382";

    /// <summary>A valid national id that is not in the seed data.</summary>
    public const string NewNationalId = "10000000450";

    /// <summary>
    /// Adds the business services with a fake clock and a cheaper password hash (new hashes only; the
    /// seeded hashes keep their own iteration count).
    /// </summary>
    public static IServiceCollection AddTestBusinessServices(this IServiceCollection services, FakeTimeProvider clock)
    {
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IPasswordHasher>(new PasswordHasher(iterations: 1_000));
        services.AddBusinessServices();
        return services;
    }

    /// <summary>Issues a setup key for the database of <paramref name="provider"/>, as the --create-setup-key command does.</summary>
    public static Task<string> CreateSetupKeyAsync(IServiceProvider provider) =>
        provider.GetRequiredService<ISetupService>().CreateSetupKeyAsync(TestContext.Current.CancellationToken);

    public static UserSession User(int id) => new(id, "kullanici" + id, UserRole.User);

    public static UserSession Admin(int id) => new(id, "yonetici" + id, UserRole.Admin);
}
