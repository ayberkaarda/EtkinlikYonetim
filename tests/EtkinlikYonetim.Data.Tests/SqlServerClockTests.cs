using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Application instances on one SQL Server database (see <see cref="SqlServerTestDatabase"/>) whose
/// computers' clocks are wrong. Which events are past, whether a registration can still be made or
/// cancelled, sign-in locks and the stored times are decided by the database's clock, so a wrong or
/// deliberately changed computer clock changes none of them.
/// </summary>
public sealed class SqlServerClockTests
{
    private const string Password = "Gizli.Sifre1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An event of yesterday by the database's clock is tomorrow by a computer clock three days behind.
    /// That computer neither lists it as open nor registers anybody for it.
    /// </summary>
    [Fact]
    public async Task Register_ClientClockBehind_CannotRegisterForAnEventPastByTheDatabase()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using (var a = database.BuildServices(demo: true))
        {
            await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        }

        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 1, Ct);
        var eventId = await AddEventAsync(database, daysFromToday: -1);
        await using var behind = database.BuildServices(clock: await database.ClockAsync(TimeSpan.FromDays(-3), Ct));

        var open = await behind.GetRequiredService<IEventService>().GetOpenEventsAsync(Admin(SeedAdminId), Ct);
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => behind.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], Ct));

        Assert.DoesNotContain(open, e => e.Id == eventId);
        Assert.Equal(RegistrationService.PastEvent, refused.Message);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id", Ct, ("@id", eventId)));
    }

    /// <summary>
    /// A registration of an event that is past by the database's clock is not cancelled from a computer
    /// whose clock is behind: its seat stays taken and nobody waiting moves up on a closed event.
    /// </summary>
    [Fact]
    public async Task Cancel_ClientClockBehind_CannotCancelARegistrationOfAPastEvent()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices(demo: true);
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 2, Ct);
        var eventId = await a.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Geçecek", database.Today.AddDays(1), 1, Ct);
        var holder = await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[0], Ct);
        await a.GetRequiredService<IRegistrationService>().RegisterAsync(Admin(SeedAdminId), eventId, ids[1], Ct);
        await database.ExecuteAsync("UPDATE dbo.Etkinlikler SET Tarih = DATEADD(DAY, -1, CAST(SYSUTCDATETIME() AS date)) WHERE Id = @id", Ct, ("@id", eventId));
        await using var behind = database.BuildServices(clock: await database.ClockAsync(TimeSpan.FromDays(-3), Ct));

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => behind.GetRequiredService<IRegistrationService>().CancelAsync(Admin(SeedAdminId), holder.RegistrationId, Ct));

        Assert.Equal(RegistrationService.PastCannotCancel, refused.Message);
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE Id = @id AND Durum = 0", Ct, ("@id", holder.RegistrationId)));
        Assert.Equal(1, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND Durum = 2", Ct, ("@id", eventId)));
        Assert.Equal(1, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", Ct, ("@id", eventId)));
    }

    /// <summary>
    /// A computer whose clock is three days ahead creates an event for today by the database (the past by
    /// its own clock), registers, checks in and cancels. Every time written is the database's.
    /// </summary>
    [Fact]
    public async Task StoredTimes_ClientClockAhead_AreTheDatabasesTimes()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using (var a = database.BuildServices(demo: true))
        {
            await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        }

        var ids = await SqlServerWaitingListTests.AddParticipantsAsync(database, 2, Ct);
        await using var ahead = database.BuildServices(clock: await database.ClockAsync(TimeSpan.FromDays(3), Ct));
        var registrations = ahead.GetRequiredService<IRegistrationService>();

        var eventId = await ahead.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Bugün", database.Today, 5, Ct);
        var entering = await registrations.RegisterAsync(Admin(SeedAdminId), eventId, ids[0], Ct);
        var leaving = await registrations.RegisterAsync(Admin(SeedAdminId), eventId, ids[1], Ct);
        var checkIn = await ahead.GetRequiredService<ICheckInService>().CheckInAsync(Admin(SeedAdminId), eventId, entering.TicketCode, Ct);
        await registrations.CancelAsync(Admin(SeedAdminId), leaving.RegistrationId, Ct);

        Assert.Equal(CheckInOutcome.CheckedIn, checkIn.Outcome);
        const string Recent = "BETWEEN DATEADD(MINUTE, -5, SYSUTCDATETIME()) AND SYSUTCDATETIME()";
        Assert.Equal(2, await database.ScalarAsync($"SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id AND KayitZamani {Recent}", Ct, ("@id", eventId)));
        Assert.Equal(2, await database.ScalarAsync($"SELECT COUNT(*) FROM dbo.Biletler b JOIN dbo.Kayitlar r ON r.Id = b.KayitId WHERE r.EtkinlikId = @id AND b.OlusturmaZamani {Recent}", Ct, ("@id", eventId)));
        Assert.Equal(1, await database.ScalarAsync($"SELECT COUNT(*) FROM dbo.Biletler WHERE KayitId = @id AND GirisZamani {Recent}", Ct, ("@id", entering.RegistrationId)));
        Assert.Equal(1, await database.ScalarAsync($"SELECT COUNT(*) FROM dbo.Kayitlar WHERE Id = @id AND IptalZamani {Recent}", Ct, ("@id", leaving.RegistrationId)));
    }

    /// <summary>
    /// A sign-in lock is set by the database's clock and ends by it: a computer whose clock is a day ahead
    /// cannot end it early, and one whose clock is a day behind does not keep an ended lock running.
    /// </summary>
    [Fact]
    public async Task SignIn_ClientClockAheadOrBehind_FollowsTheLockByTheDatabaseClock()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var a = database.BuildServices();
        await a.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        await a.GetRequiredService<IAuthService>().RegisterAsync("kilit", Password, Password, Ct);
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => a.GetRequiredService<IAuthService>().SignInAsync("kilit", "Yanlis.Sifre1", Ct));
        }

        await using var ahead = database.BuildServices(clock: await database.ClockAsync(TimeSpan.FromDays(1), Ct));
        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => ahead.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kullanicilar WHERE KullaniciAdi = N'kilit' AND KilitBitis BETWEEN DATEADD(MINUTE, 14, SYSUTCDATETIME()) AND DATEADD(MINUTE, 16, SYSUTCDATETIME())",
            Ct));

        await database.ExecuteAsync("UPDATE dbo.Kullanicilar SET KilitBitis = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE KullaniciAdi = N'kilit'", Ct);
        await using var behind = database.BuildServices(clock: await database.ClockAsync(TimeSpan.FromDays(-1), Ct));
        Assert.Equal("kilit", (await behind.GetRequiredService<IAuthService>().SignInAsync("kilit", Password, Ct)).UserName);
    }

    /// <summary>
    /// A registration waits for the event's lock. Its date decision and its stored time are taken from the
    /// database's clock after the lock was granted, not from a reading made before it waited.
    /// </summary>
    [Fact]
    public async Task Register_AfterWaitingForTheEventLock_UsesTheTimeOfTheWrite()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices(demo: true);
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var eventId = await AddEventAsync(database, daysFromToday: 3);

        Task<RegistrationReceipt> registration;
        DateTimeOffset released;
        await using (var holder = await HeldAppLock.TakeAsync(database, AppLocks.Event(eventId), Ct))
        {
            registration = app.GetRequiredService<IRegistrationService>().RegisterAsync(User(SeedUserId), eventId, DenizNationalId, Ct);
            await database.WaitForLockWaitAsync(Ct);
            await Task.Delay(TimeSpan.FromSeconds(2), Ct);
            released = await database.ServerUtcNowAsync(Ct);
        }

        var receipt = await registration;

        // A time read before the wait would be two seconds older than the release.
        Assert.Equal(1, await database.ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Kayitlar WHERE Id = @id AND KayitZamani >= DATEADD(MILLISECOND, -500, @released)",
            Ct,
            ("@id", receipt.RegistrationId),
            ("@released", released.UtcDateTime)));
    }

    /// <summary>Stores an event <paramref name="daysFromToday"/> days from today by the database's clock and returns its id.</summary>
    private static Task<int> AddEventAsync(SqlServerTestDatabase database, int daysFromToday) =>
        database.ScalarAsync(
            """
            INSERT dbo.Etkinlikler (EtkinlikAdi, Tarih, Kapasite, KayitSayisi, IptalEdildi, DuzenlemeSurumu)
            VALUES (N'Saat Testi', DATEADD(DAY, @days, CAST(SYSUTCDATETIME() AS date)), 10, 0, 0, 0);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            Ct,
            ("@days", daysFromToday));
}
