using System.Globalization;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Concurrency on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>): many
/// registrations race for the last seats, one participant races itself, and failed sign-ins arrive at
/// the same time.
/// </summary>
public sealed class SqlServerConcurrencyTests
{
    private const int Contenders = 16;

    [Fact]
    public async Task Register_ManyParticipantsRaceForTheLastSeats_CapacityHolds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var nationalIds = await AddParticipantsAsync(database, Contenders, ct);
        var service = provider.GetRequiredService<IRegistrationService>();
        var events = provider.GetRequiredService<IEventService>();

        // Several rounds make it likely that the registrations really overlap inside their transactions.
        for (var round = 0; round < 4; round++)
        {
            const int Seats = 3;
            var eventId = await events.CreateAsync(Admin(SeedAdminId), "Yarış " + round, new DateTime(2026, 4, 1), Seats, ct);

            var results = await RaceAsync(nationalIds, id => service.RegisterAsync(Admin(SeedAdminId), eventId, id, ct), ct);

            Assert.Equal(Seats, results.Count(r => r is null));
            Assert.All(
                results.Where(r => r is not null),
                r => Assert.Equal("Etkinliğin kontenjanı dolu.", Assert.IsType<BusinessRuleException>(r).Message));
            Assert.Equal(Seats, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 0", ct, ("@id", eventId)));
            Assert.Equal(Seats, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Biletler b JOIN dbo.Kayitlar r ON r.Id = b.KayitId WHERE r.EtkinlikId = @id", ct, ("@id", eventId)));
            Assert.Equal(Seats, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
        }
    }

    [Fact]
    public async Task Register_OneParticipantRacesItself_ExactlyOneRegistration()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var service = provider.GetRequiredService<IRegistrationService>();

        for (var round = 0; round < 4; round++)
        {
            var eventId = await provider.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Tekrar " + round, new DateTime(2026, 4, 1), 100, ct);

            var results = await RaceAsync(
                Enumerable.Repeat(DenizNationalId, Contenders).ToList(),
                id => service.RegisterAsync(User(SeedUserId), eventId, id, ct),
                ct);

            Assert.Single(results, r => r is null);
            Assert.All(
                results.Where(r => r is not null),
                r => Assert.Equal("Bu katılımcı bu etkinliğe zaten kayıtlı.", Assert.IsType<BusinessRuleException>(r).Message));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id", ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
        }
    }

    [Fact]
    public async Task Database_RejectsDuplicatesAndOverbookingWrittenPastTheServices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        const string Insert = "INSERT dbo.Kayitlar (KatilimciId, EtkinlikId, KayitZamani, Durum) VALUES (1, 1, SYSUTCDATETIME(), @status)";
        await database.ExecuteAsync(Insert, ct, ("@status", 0));
        await database.ExecuteAsync(Insert, ct, ("@status", 1)); // cancelled rows do not count

        var duplicate = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(Insert, ct, ("@status", 0)));
        Assert.Contains("IX_Kayitlar_KatilimciId_EtkinlikId", duplicate.Message, StringComparison.Ordinal);
        var overbooked = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("UPDATE dbo.Etkinlikler SET KayitSayisi = 3 WHERE Id = 3", ct));
        Assert.Contains("CK_Etkinlikler_KayitSayisi", overbooked.Message, StringComparison.Ordinal);
        var ticket = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync("INSERT dbo.Biletler (KayitId, BiletKodu, OlusturmaZamani) VALUES (999, N'ETK-X', SYSUTCDATETIME())", ct));
        Assert.Contains("FK_Biletler_Kayitlar", ticket.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignIn_ConcurrentFailures_AreAllCountedAndLockTheAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var auth = provider.GetRequiredService<IAuthService>();

        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(
                async () =>
                {
                    start.Wait(ct);
                    await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "yanlis", ct));
                },
                ct))
            .ToList();
        start.Set();
        await Task.WhenAll(attempts);

        // A read-modify-write counter would lose increments here and leave the account open.
        const string Locked = "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'ornek' AND KilitBitis IS NOT NULL AND BasarisizGiris = 0";
        Assert.Equal(1, await database.ScalarAsync(Locked, ct));
        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("ornek", "Demo.Kullanici1", ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);
        Assert.Equal(1, await database.ScalarAsync(Locked, ct));
    }

    /// <summary>
    /// Two application instances sign in to an account with four failures: B with the right password,
    /// A with a wrong one. B is held after its password check, A's fifth failure locks the account,
    /// then B continues. B must be refused and the lock must stay.
    /// </summary>
    [Fact]
    public async Task SignIn_SuccessRacingWithFifthFailure_DoesNotClearLock()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var instanceA = database.BuildServices(demo: true);
        await instanceA.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var gate = new SuccessWriteGate();
        await using var instanceB = database.BuildServices(services => services.HoldSuccessfulSignInWrites(gate));
        await database.ExecuteAsync("UPDATE dbo.Kullanicilar SET BasarisizGiris = 4 WHERE KullaniciAdi = N'ornek'", ct);
        const string LockState = "SELECT CONVERT(nvarchar(40), KilitBitis, 126) + N'/' + CONVERT(nvarchar(10), BasarisizGiris) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'ornek'";

        var authB = instanceB.GetRequiredService<IAuthService>();
        var signInB = Task.Run(() => authB.SignInAsync("ornek", "Demo.Kullanici1", ct), ct);
        await gate.Reached.WaitAsync(TimeSpan.FromSeconds(30), ct);

        var failureA = await Assert.ThrowsAsync<BusinessRuleException>(
            () => instanceA.GetRequiredService<IAuthService>().SignInAsync("ornek", "yanlis", ct));
        var lockedByA = await database.TextAsync(LockState, ct);
        Assert.NotNull(lockedByA);
        Assert.EndsWith("/0", lockedByA, StringComparison.Ordinal);

        gate.Release();
        var failureB = await Assert.ThrowsAsync<BusinessRuleException>(() => signInB);

        Assert.Equal(failureA.Message, failureB.Message);
        Assert.Equal(lockedByA, await database.TextAsync(LockState, ct));
    }

    /// <summary>
    /// Two application instances run the first-run setup at the same time with different names: exactly
    /// one admin is created, every other attempt is told that the setup is done.
    /// </summary>
    [Fact]
    public async Task Setup_ConcurrentFirstRuns_CreateExactlyOneAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var first = database.BuildServices();
        await first.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var second = database.BuildServices();
        var providers = new[] { first, second };

        for (var round = 0; round < 4; round++)
        {
            await database.ExecuteAsync("DELETE dbo.Kullanicilar", ct);
            var names = Enumerable.Range(0, 8).Select(i => "kurucu" + (char)('a' + round) + (char)('a' + i)).ToList();

            var results = await RaceAsync(
                names,
                name => providers[name[^1] % 2].GetRequiredService<ISetupService>().CreateFirstAdminAsync(null, name, "Gizli.Sifre1", "Gizli.Sifre1", ct),
                ct);

            Assert.Single(results, r => r is null);
            Assert.All(
                results.Where(r => r is not null),
                r => Assert.Equal(SetupService.AlreadyDone, Assert.IsType<BusinessRuleException>(r).Message));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", ct));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin' AND SifreBicimi = 0", ct));
        }

        Assert.False(await second.GetRequiredService<ISetupService>().IsSetupRequiredAsync(ct));
    }

    /// <summary>
    /// A database with a user but no admin: two application instances race with the setup key issued
    /// for the database and with wrong ones. Exactly one admin is created, the key is used once, and
    /// a wrong key never creates anything.
    /// </summary>
    [Fact]
    public async Task Setup_ExistingUsersConcurrentKeyedRuns_CreateExactlyOneAdmin()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var first = database.BuildServices();
        await first.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);

        for (var round = 0; round < 4; round++)
        {
            await database.ExecuteAsync("DELETE dbo.Kullanicilar; DELETE dbo.KurulumAnahtarlari", ct);
            await first.GetRequiredService<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", ct);

            // The key is issued for the database; the two instances stand for two profiles or computers.
            var key = await AddSetupKeyAsync(first, database.Clock.GetUtcNow());
            await using var a = database.BuildServices();
            await using var b = database.BuildServices();
            var providers = new[] { a, b };
            var names = Enumerable.Range(0, 8).Select(i => "kurucu" + (char)('a' + round) + (char)('a' + i)).ToList();

            // Every third attempt uses a wrong key; wrong keys write nothing.
            var results = await RaceAsync(
                names,
                name => providers[name[^1] % 2].GetRequiredService<ISetupService>()
                    .CreateFirstAdminAsync(name[^1] % 3 == 1 ? "YANLIS-" + name : key, name, "Gizli.Sifre1", "Gizli.Sifre1", ct),
                ct);

            Assert.Single(results, r => r is null);
            Assert.All(
                results.Where(r => r is not null),
                r => Assert.Contains(Assert.IsType<BusinessRuleException>(r).Message, new[] { SetupService.AlreadyDone, SetupService.SetupKeyInvalid }));
            Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar", ct));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kullanicilar WHERE Rol = N'Admin' AND SifreBicimi = 0", ct));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.KurulumAnahtarlari WHERE Tuketildi = 1 AND TuketimTarihi IS NOT NULL", ct));
        }
    }

    /// <summary>
    /// One account adds the same national id many times at once while another account adds it too:
    /// the unique index keeps one per account, the losers of the same account are told it is already
    /// in their list, and the other account is not affected.
    /// </summary>
    [Fact]
    public async Task AddParticipant_SameNationalIdRacesWithinAndAcrossAccounts_OnePerAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var provider = database.BuildServices(demo: true);
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var service = provider.GetRequiredService<IParticipantService>();

        var attempts = Enumerable.Repeat(SeedAdminId, Contenders / 2).Concat(Enumerable.Repeat(SeedUserId, Contenders / 2))
            .Select(id => id.ToString(CultureInfo.InvariantCulture))
            .ToList();
        var results = await RaceAsync(
            attempts,
            owner => service.AddAsync(owner == "1" ? Admin(SeedAdminId) : User(SeedUserId), NewNationalId, "Yarış Katılımcısı", "yaris@example.com", ct),
            ct);

        Assert.Equal(2, results.Count(r => r is null));
        Assert.All(
            results.Where(r => r is not null),
            r => Assert.Equal(ParticipantService.NationalIdTaken, Assert.IsType<BusinessRuleException>(r).Message));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Katilimcilar WHERE TcKimlikNo = @tc AND KullaniciId = 1", ct, ("@tc", NewNationalId)));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Katilimcilar WHERE TcKimlikNo = @tc AND KullaniciId = 2", ct, ("@tc", NewNationalId)));
    }

    /// <summary>Starts every attempt at once; each result is null on success or the exception.</summary>
    private static async Task<Exception?[]> RaceAsync<T>(IReadOnlyList<string> inputs, Func<string, Task<T>> attempt, CancellationToken ct)
    {
        using var start = new ManualResetEventSlim(false);
        var attempts = inputs
            .Select(input => Task.Run(
                async () =>
                {
                    start.Wait(ct);
                    try
                    {
                        await attempt(input);
                        return (Exception?)null;
                    }
#pragma warning disable CA1031 // Collected and asserted by the caller.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        return ex;
                    }
                },
                ct))
            .ToList();
        start.Set();
        return await Task.WhenAll(attempts);
    }

    private static async Task<IReadOnlyList<string>> AddParticipantsAsync(SqlServerTestDatabase database, int count, CancellationToken ct)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            // Format-only placeholder ids; the database does not check the checksum and the lookup
            // only needs the format.
            var nationalId = "2000000" + i.ToString("0000", CultureInfo.InvariantCulture);
            await database.ExecuteAsync(
                "INSERT dbo.Katilimcilar (TcKimlikNo, AdSoyad, Email, KullaniciId) VALUES (@tc, @name, N'yaris@example.com', 1)",
                ct,
                ("@tc", nationalId),
                ("@name", "Yarış Katılımcısı " + i));
            ids.Add(nationalId);
        }

        return ids;
    }
}
