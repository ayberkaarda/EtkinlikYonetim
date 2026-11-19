using System.Security.Cryptography;
using System.Text;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
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
        services.AddSingleton(new SetupKeyThrottle(TimeSpan.Zero));
        services.AddSingleton(new SetupKeyEnvironment(null));
        services.AddBusinessServices();
        return services;
    }

    /// <summary>
    /// Stores a new setup key for the database of <paramref name="provider"/> as the database owner's
    /// script does (16 random bytes, SHA-256 of the 32 hexadecimal characters, 24 hours from
    /// <paramref name="now"/>) and returns it in groups of four. On SQL Server the tests run the script
    /// itself; this stands in for it where there is no T-SQL.
    /// </summary>
    public static async Task<string> AddSetupKeyAsync(IServiceProvider provider, DateTimeOffset now)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await using var db = await provider.GetRequiredService<IDbContextFactory<EventDbContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
        db.SetupKeys.Add(new SetupKey
        {
            KeyHash = SHA256.HashData(Encoding.ASCII.GetBytes(key)),
            CreatedAt = now.UtcDateTime,
            ExpiresAt = now.UtcDateTime + SetupKeyPolicy.Lifetime,
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return SetupKeyPolicy.Format(key);
    }

    public static UserSession User(int id) => new(id, "kullanici" + id, UserRole.User);

    public static UserSession Admin(int id) => new(id, "yonetici" + id, UserRole.Admin);
}
