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
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Giriş " + round, database.Today, 5, ct);
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

    /// <summary>
    /// The holder of the only seat is scanned at the entrance while the same registration is cancelled from
    /// another instance, many times. Exactly one of the two succeeds: either the participant is inside and
    /// keeps the seat, or the registration is cancelled, its ticket unused, and the one waiting moves up. The
    /// room never holds more people than seats.
    /// </summary>
    [Fact]
    public async Task CheckInRacingWithACancellation_ExactlyOneSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 2, ct);

        for (var round = 0; round < 10; round++)
        {
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Kapı " + round, database.Today, 1, ct);
            var holder = await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct);
            await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[1], ct);
            CheckInOutcome? outcome = null;

            var results = await SqlServerWaitingListTests.RaceAsync(
                ["scan", "cancel"],
                async step =>
                {
                    if (step == "scan")
                    {
                        outcome = (await a.GetRequiredService<ICheckInService>().CheckInAsync(Admin(SeedAdminId), eventId, holder.TicketCode, ct)).Outcome;
                    }
                    else
                    {
                        await b.GetRequiredService<IRegistrationService>().CancelAsync(Admin(SeedAdminId), holder.RegistrationId, ct);
                    }
                },
                ct);

            var cancelled = results[1] is null;
            Assert.Null(results[0]);
            Assert.True(cancelled != (outcome == CheckInOutcome.CheckedIn), $"Round {round}: check-in {outcome}, cancellation {(cancelled ? "succeeded" : "refused")}.");
            if (!cancelled)
            {
                var refused = Assert.IsType<Business.BusinessRuleException>(results[1]);
                Assert.True(refused.Message is RegistrationService.CheckedInCannotCancel or RegistrationService.CancelFailed, refused.Message);
            }

            // Inside people plus the seats still free never exceed the one seat.
            Assert.Equal(1, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 0", ct, ("@id", eventId)));
            Assert.Equal(
                cancelled ? 0 : 1,
                await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Biletler b JOIN dbo.Kayitlar r ON r.Id = b.KayitId WHERE r.EtkinlikId = @id AND b.GirisZamani IS NOT NULL", ct, ("@id", eventId)));
        }
    }
}