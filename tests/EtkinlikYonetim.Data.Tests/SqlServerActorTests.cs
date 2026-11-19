using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Data.Persistence;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// The acting account is checked again inside every write, after its lock, on a real SQL Server instance
/// (see <see cref="SqlServerTestDatabase"/>). The tests hold the event's lock, let a write pass the session
/// check and wait for the lock, change the account meanwhile, and only then let the lock go.
/// </summary>
public sealed class SqlServerActorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An admin changes an event; while the change waits for the event's lock another admin disables the
    /// first one. The account change does not wait for the event, and the pending change writes nothing.
    /// </summary>
    [Fact]
    public async Task EventChange_AdminDisabledWhileWaitingForTheLock_WritesNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices(demo: true);
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var events = app.GetRequiredService<IEventService>();
        var accounts = app.GetRequiredService<IAccountService>();
        var eventId = await events.CreateAsync(Admin(SeedAdminId), "Bekleyen", database.Today.AddDays(3), 5, Ct);
        var loaded = (await events.GetEventsAsync(Admin(SeedAdminId), Ct)).Single(e => e.Id == eventId);
        await accounts.ChangeRoleAsync(Admin(SeedAdminId), SeedUserId, UserRole.Admin, Ct);
        var second = new UserSession(SeedUserId, "ornek", UserRole.Admin, sessionVersion: 1);

        Task change;
        await using (var holder = await HeldAppLock.TakeAsync(database, AppLocks.Event(eventId), Ct))
        {
            change = events.UpdateAsync(Admin(SeedAdminId), loaded, database.Today.AddDays(3), 9, "Değişti", Ct);
            await database.WaitForLockWaitAsync(Ct);

            await accounts.SetEnabledAsync(second, SeedAdminId, false, Ct);
        }

        await Assert.ThrowsAsync<SessionExpiredException>(() => change);
        Assert.Equal(5, await database.ScalarAsync("SELECT Kapasite FROM dbo.Etkinlikler WHERE Id = @id", Ct, ("@id", eventId)));
        Assert.Null(await database.TextAsync("SELECT Aciklama FROM dbo.Etkinlikler WHERE Id = @id", Ct, ("@id", eventId)));
    }

    /// <summary>
    /// A user registers a participant; while the registration waits for the event's lock an admin disables
    /// the user. The registration writes nothing: no seat, no ticket, no waiting place.
    /// </summary>
    [Fact]
    public async Task Registration_UserDisabledWhileWaitingForTheLock_WritesNothing()
    {
        await using var database = await SqlServerTestDatabase.ReserveAsync(Ct);
        await using var app = database.BuildServices(demo: true);
        await app.GetRequiredService<IDatabaseInitializer>().InitializeAsync(Ct);
        var eventId = await app.GetRequiredService<IEventService>().CreateAsync(Admin(SeedAdminId), "Kayıt", database.Today.AddDays(3), 5, Ct);

        Task registration;
        await using (var holder = await HeldAppLock.TakeAsync(database, AppLocks.Event(eventId), Ct))
        {
            registration = app.GetRequiredService<IRegistrationService>().RegisterAsync(User(SeedUserId), eventId, DenizNationalId, Ct);
            await database.WaitForLockWaitAsync(Ct);

            await app.GetRequiredService<IAccountService>().SetEnabledAsync(Admin(SeedAdminId), SeedUserId, false, Ct);
        }

        await Assert.ThrowsAsync<SessionExpiredException>(() => registration);
        Assert.Equal(0, await database.ScalarAsync("SELECT COUNT(*) FROM dbo.Kayitlar WHERE EtkinlikId = @id", Ct, ("@id", eventId)));
        Assert.Equal(0, await database.ScalarAsync("SELECT KayitSayisi FROM dbo.Etkinlikler WHERE Id = @id", Ct, ("@id", eventId)));
    }
}
