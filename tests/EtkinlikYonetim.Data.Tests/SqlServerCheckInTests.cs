using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>The entrance under concurrency on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>).</summary>
public sealed class SqlServerCheckInTests
{
    /// <summary>
    /// The same ticket is scanned at two entrances (two application instances) at the same moment, many
    /// times: exactly one scan checks it in, the other says it was already used, and the counter is one.
    /// </summary>
    [Fact]
    public async Task SameTicketAtTwoEntrances_IsCheckedInExactlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 1, ct);

        for (var round = 0; round < 8; round++)
        {
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Giriş " + round, new DateTime(2026, 3, 2), 5, ct);
            var receipt = await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct);
            var outcomes = new List<CheckInOutcome>();

            var results = await SqlServerWaitingListTests.RaceAsync(
                ["a", "b", "a", "b"],
                async instance =>
                {
                    var result = await (instance == "a" ? a : b).GetRequiredService<ICheckInService>()
                        .CheckInAsync(Admin(SeedAdminId), eventId, receipt.TicketCode, ct);
                    lock (outcomes)
                    {
                        outcomes.Add(result.Outcome);
                    }
                },
                ct);

            Assert.All(results, Assert.Null);
            Assert.Single(outcomes, o => o == CheckInOutcome.CheckedIn);
            Assert.Equal(3, outcomes.Count(o => o == CheckInOutcome.AlreadyUsed));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Biletler b JOIN dbo.Kayitlar r ON r.Id = b.KayitId WHERE r.EtkinlikId = @id AND b.GirisZamani IS NOT NULL", ct, ("@id", eventId)));
            Assert.Equal(new Domain.Entities.CheckInCount(1, 1), await a.GetRequiredService<ICheckInService>().GetCountAsync(Admin(SeedAdminId), eventId, ct));
        }
    }
}