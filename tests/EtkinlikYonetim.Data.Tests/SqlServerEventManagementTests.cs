using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>Changes of an event racing with registrations on a real SQL Server instance (see <see cref="SqlServerTestDatabase"/>).</summary>
public sealed class SqlServerEventManagementTests
{
    private const int Rounds = 6;

    /// <summary>
    /// Three seats, two held. The admin lowers the capacity to two while a third participant registers.
    /// Either the capacity changes and the participant waits, or the participant takes the seat and the
    /// change is refused; the capacity never ends up below the seats held.
    /// </summary>
    [Fact]
    public async Task LoweringTheCapacityRacingWithARegistration_NeverGoesBelowTheSeatsHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 3, ct);

        for (var round = 0; round < Rounds; round++)
        {
            var registrations = a.GetRequiredService<IRegistrationService>();
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Küçülen " + round, database.Today.AddDays(30), 3, ct);
            await registrations.RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct);
            await registrations.RegisterAsync(Admin(SeedAdminId), eventId, ids[1], ct);
            var loaded = (await a.GetRequiredService<IEventService>().GetEventsAsync(Admin(SeedAdminId), ct)).Single(e => e.Id == eventId);

            var results = await SqlServerWaitingListTests.RaceAsync(
                ["update", "register"],
                step => step == "update"
                    ? a.GetRequiredService<IEventService>().UpdateAsync(Admin(SeedAdminId), loaded, database.Today.AddDays(30), 2, null, ct)
                    : b.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[2], ct),
                ct);

            Assert.Null(results[1]); // the registration always succeeds, with a seat or a waiting place
            var capacity = await database.ScalarAsync("SELECT Kapasite FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId));
            var seats = await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 0", ct, ("@id", eventId));
            if (results[0] is { } refused)
            {
                Assert.Equal(EventService.CapacityBelowRegistrations, Assert.IsType<BusinessRuleException>(refused).Message);
                Assert.Equal((3, 3), (capacity, seats));
            }
            else
            {
                Assert.Equal((2, 2), (capacity, seats));
                Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 2", ct, ("@id", eventId)));
            }

            Assert.Equal(seats, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
        }
    }

    /// <summary>
    /// The admin cancels an event while a participant registers: either the registration comes first and
    /// is closed with the event, or it is refused. No seat or waiting place stays open on a cancelled event.
    /// </summary>
    [Fact]
    public async Task CancellingAnEventRacingWithARegistration_LeavesNothingOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.ReserveAsync(ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        await using var b = database.BuildServices();
        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 1, ct);

        for (var round = 0; round < Rounds; round++)
        {
            var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "İptal " + round, database.Today.AddDays(30), 5, ct);

            var results = await SqlServerWaitingListTests.RaceAsync(
                ["cancel", "register"],
                step => step == "cancel"
                    ? a.GetRequiredService<IEventService>().CancelAsync(Admin(SeedAdminId), eventId, ct)
                    : b.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], ct),
                ct);

            Assert.Null(results[0]);
            if (results[1] is { } refused)
            {
                var message = Assert.IsType<BusinessRuleException>(refused).Message;
                Assert.True(message is RegistrationService.CancelledEvent or RegistrationService.PastEvent, message);
            }

            Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum IN (0, 2)", ct, ("@id", eventId)));
            Assert.Equal(0, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
            Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Etkinlikler WHERE Id = @id AND IptalEdildi = 1", ct, ("@id", eventId)));
        }
    }

    /// <summary>
    /// On the case-insensitive collation of the application's database, admin B changes the description
    /// "Salon" to "SALON" only. Admin A's form, loaded before, changes the capacity: comparing values would
    /// find "Salon" equal to "SALON" and silently write the old text back. The edit version tells the two
    /// apart: A's change is refused and B's text stays.
    /// </summary>
    [Fact]
    public async Task StaleForm_AfterACaseOnlyDescriptionChange_IsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await SqlServerTestDatabase.CreateAsync(ct);
        await using var app = database.BuildServices(demo: true);
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(ct);
        var events = app.GetRequiredService<IEventService>();
        var day = database.Today.AddDays(10);
        var eventId = await events.CreateAsync(Admin(SeedAdminId), "Salon Konseri", day, 5, ct);
        var created = (await events.GetEventsAsync(Admin(SeedAdminId), ct)).Single(e => e.Id == eventId);
        await events.UpdateAsync(Admin(SeedAdminId), created, day, 5, "Salon", ct);
        var loadedByA = (await events.GetEventsAsync(Admin(SeedAdminId), ct)).Single(e => e.Id == eventId);
        var loadedByB = loadedByA;

        await events.UpdateAsync(Admin(SeedAdminId), loadedByB, day, 5, "SALON", ct);
        var stale = await Assert.ThrowsAsync<StaleDataException>(() => events.UpdateAsync(Admin(SeedAdminId), loadedByA, day, 9, "Salon", ct));

        Assert.Equal(EventService.ChangedElsewhere, stale.Message);
        Assert.Equal("SALON", await database.TextAsync("SELECT Aciklama FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
        Assert.Equal(5, await database.ScalarAsync("SELECT Kapasite FROM dbo.Etkinlikler WHERE Id = @id", ct, ("@id", eventId)));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM sys.databases WHERE name = DB_NAME() AND collation_name = N'Turkish_CI_AS'", ct));
    }
}
