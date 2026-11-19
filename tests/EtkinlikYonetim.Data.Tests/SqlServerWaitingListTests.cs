using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// The waiting list under concurrency on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>),
/// with two application instances, so only the per-event application lock and the transactions keep the
/// order: a freed seat always goes to the first one waiting, nobody waits twice and the counter equals
/// the seats held.
/// </summary>
public sealed class SqlServerWaitingListTests
{
    private const int Rounds = 6;
    private const string Active = "SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 0";
    private const string Waiting = "SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 2";
    private const string Counter = "SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id";
    private const string TicketsOfActive = "SELECT COUNT(*) FROM dbo.Biletler b JOIN dbo.Kayitlar r ON r.Id = b.KayitId WHERE r.EtkinlikId = @id AND r.Durum = 0";

    /// <summary>
    /// One seat, A holds it and B waits. A cancels while C registers. Whatever the order, B gets the seat
    /// and a ticket, and C waits behind: a new registration never takes a seat past the waiting list.
    /// </summary>
    [Fact]
    public async Task CancelRacingWithANewRegistration_SeatGoesToTheOneWaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await AddParticipantsAsync(database, 3, ct);

        for (var round = 0; round < Rounds; round++)
        {
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Tek koltuk " + round, database.Today.AddDays(30), 1, ct);
            var holder = await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct);
            var waiting = await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[1], ct);
            Assert.Equal(1, waiting.QueuePosition);

            var results = await RaceAsync(
                ["cancel", "register"],
                step => step == "cancel"
                    ? a.GetRequiredService<IRegistrationService>().CancelAsync(Admin(SeedAdminId), holder.RegistrationId, ct)
                    : b.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[2], ct),
                ct);

            Assert.All(results, Assert.Null);
            Assert.Equal(waiting.RegistrationId, await database.ScalarAsync("SELECT Id FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 0", ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync(TicketsOfActive, ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync(Waiting, ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync(Counter, ct, ("@id", eventId)));
        }
    }

    /// <summary>Two seats are cancelled at the same moment: both waiting participants move up, in order, each with a ticket.</summary>
    [Fact]
    public async Task TwoCancellationsAtOnce_PromoteTheTwoWaiting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await AddParticipantsAsync(database, 5, ct);

        for (var round = 0; round < Rounds; round++)
        {
            var service = a.GetRequiredService<IRegistrationService>();
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "İki koltuk " + round, database.Today.AddDays(30), 2, ct);
            var first = await service.RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct);
            var second = await service.RegisterAsync(Admin(SeedAdminId), eventId, ids[1], ct);
            var third = await service.RegisterAsync(Admin(SeedAdminId), eventId, ids[2], ct);
            var fourth = await service.RegisterAsync(Admin(SeedAdminId), eventId, ids[3], ct);
            var fifth = await service.RegisterAsync(Admin(SeedAdminId), eventId, ids[4], ct);

            var results = await RaceAsync(
                ["a", "b"],
                instance => (instance == "a" ? a : b).GetRequiredService<IRegistrationService>()
                    .CancelAsync(Admin(SeedAdminId), instance == "a" ? first.RegistrationId : second.RegistrationId, ct),
                ct);

            Assert.All(results, Assert.Null);
            Assert.Equal(
                third.RegistrationId + fourth.RegistrationId,
                await database.ScalarAsync("SELECT SUM(Id) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 0", ct, ("@id", eventId)));
            Assert.Equal(fifth.RegistrationId, await database.ScalarAsync("SELECT Id FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 2", ct, ("@id", eventId)));
            Assert.Equal(2, await database.ScalarAsync(TicketsOfActive, ct, ("@id", eventId)));
            Assert.Equal(2, await database.ScalarAsync(Counter, ct, ("@id", eventId)));
        }
    }

    /// <summary>A full event gets two registrations at the same moment: both wait, with the next two numbers.</summary>
    [Fact]
    public async Task TwoRegistrationsOnAFullEvent_WaitWithDistinctPlaces()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await AddParticipantsAsync(database, 3, ct);

        for (var round = 0; round < Rounds; round++)
        {
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Dolu " + round, database.Today.AddDays(30), 1, ct);
            await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct);
            var places = new List<int>();

            var results = await RaceAsync(
                ["a", "b"],
                async instance =>
                {
                    var receipt = await (instance == "a" ? a : b).GetRequiredService<IRegistrationService>()
                        .RegisterAsync(Admin(SeedAdminId), eventId, instance == "a" ? ids[1] : ids[2], ct);
                    lock (places)
                    {
                        places.Add(receipt.QueuePosition ?? 0);
                    }
                },
                ct);

            Assert.All(results, Assert.Null);
            Assert.Equal([1, 2], places.Order());
            Assert.Equal(1, await database.ScalarAsync(Active, ct, ("@id", eventId)));
            Assert.Equal(2, await database.ScalarAsync("SELECT COUNT(DISTINCT SiraNo) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 2", ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync(Counter, ct, ("@id", eventId)));
        }
    }

    internal static async Task<Exception?[]> RaceAsync(IReadOnlyList<string> inputs, Func<string, Task> attempt, CancellationToken ct)
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

    /// <summary>Participants owned by the seeded admin with format-only national ids (the lookup checks the format only).</summary>
    internal static async Task<IReadOnlyList<string>> AddParticipantsAsync(SqlServerTestDatabase database, int count, CancellationToken ct)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var nationalId = "3000000" + i.ToString("0000", CultureInfo.InvariantCulture);
            await database.ExecuteAsync(
                "INSERT dbo.Katilimcilar (TcKimlikNo, AdSoyad, Email, KullaniciId) VALUES (@tc, @name, N'sira@example.com', 1)",
                ct,
                ("@tc", nationalId),
                ("@name", "Sıra Katılımcısı " + i));
            ids.Add(nationalId);
        }

        return ids;
    }
}
