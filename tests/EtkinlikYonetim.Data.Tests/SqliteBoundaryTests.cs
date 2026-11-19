using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using Microsoft.EntityFrameworkCore;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// The limits of the critical paths end to end on SQLite: the exact lockout length, the smallest and
/// largest capacity, today against yesterday, and that refused input writes nothing.
/// </summary>
public sealed class SqliteBoundaryTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Today => Start.Date;

    private IAuthService Auth => _db.Get<IAuthService>();

    private IEventService Events => _db.Get<IEventService>();

    private IRegistrationService Registrations => _db.Get<IRegistrationService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    /// <summary>Four failures do not lock; the fifth locks for exactly fifteen minutes.</summary>
    [Fact]
    public async Task Lockout_StartsAtTheFifthFailureAndEndsExactlyAfterFifteenMinutes()
    {
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "yanlis", Ct));
        }

        await Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct); // not locked after four, and the count starts again
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts; i++)
        {
            await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "yanlis", Ct));
        }

        await using (var context = _db.CreateContext())
        {
            var user = await context.Users.SingleAsync(u => u.Id == SeedUserId, Ct);
            Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime + TimeSpan.FromMinutes(15), user.LockoutEnd);
        }

        _db.Clock.Advance(LoginPolicy.LockoutDuration - TimeSpan.FromSeconds(1));
        var locked = await Assert.ThrowsAsync<BusinessRuleException>(() => Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct));
        Assert.Equal(AuthService.CredentialsInvalid, locked.Message);

        _db.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SeedUserId, (await Auth.SignInAsync("ornek", "Demo.Kullanici1", Ct)).UserId);
    }

    /// <summary>An event with the smallest capacity takes one registration; a cancellation frees the seat for another participant.</summary>
    [Fact]
    public async Task Capacity_OneSeat_IsTakenOnceAndFreedByACancellation()
    {
        var user = User(SeedUserId);
        var id = await Events.CreateAsync(Admin(SeedAdminId), "Tek Kişilik", Today.AddDays(10), 1, Ct);

        var first = await Registrations.RegisterAsync(user, id, DenizNationalId, Ct);
        var full = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(user, id, EceNationalId, Ct));
        Assert.Equal(RegistrationService.EventFull, full.Message);

        await Registrations.CancelAsync(user, first.RegistrationId, Ct);
        await Registrations.RegisterAsync(user, id, EceNationalId, Ct);
        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(user, id, DenizNationalId, Ct));
        Assert.Equal(RegistrationService.EventFull, again.Message);

        await using var context = _db.CreateContext();
        var stored = await context.Events.SingleAsync(e => e.Id == id, Ct);
        Assert.Equal(1, stored.RegisteredCount);
        Assert.Equal(1, await context.Registrations.CountAsync(r => r.EventId == id && r.Status == Domain.Entities.RegistrationStatus.Active, Ct));
    }

    /// <summary>Capacity 1 to 1000 and a day from today on are stored; everything outside is refused and writes nothing.</summary>
    [Fact]
    public async Task CreateEvent_OutsideTheCapacityOrDateBounds_WritesNothing()
    {
        var admin = Admin(SeedAdminId);

        Assert.Equal(EventService.CapacityInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CreateAsync(admin, "Sıfır", Today.AddDays(1), 0, Ct))).Message);
        Assert.Equal(EventService.CapacityInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CreateAsync(admin, "Fazla", Today.AddDays(1), 1001, Ct))).Message);
        Assert.Equal(EventService.PastDate, (await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CreateAsync(admin, "Dün", Today.AddDays(-1).AddHours(23), 10, Ct))).Message);
        Assert.Equal(EventService.NameTooLong, (await Assert.ThrowsAsync<BusinessRuleException>(() => Events.CreateAsync(admin, new string('a', 81), Today, 10, Ct))).Message);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Events.CreateAsync(User(SeedUserId), "Yetkisiz", Today, 10, Ct));

        await using (var context = _db.CreateContext())
        {
            Assert.Equal(3, await context.Events.CountAsync(Ct));
        }

        var id = await Events.CreateAsync(admin, "Büyük Salon", Today, 1000, Ct);
        await using var after = _db.CreateContext();
        var stored = await after.Events.SingleAsync(e => e.Id == id, Ct);
        Assert.Equal((DateOnly.FromDateTime(Today), 1000, 0), (stored.Date, stored.Capacity, stored.RegisteredCount));
    }

    /// <summary>A participant or registration that a validator refuses leaves no row behind.</summary>
    [Fact]
    public async Task RefusedParticipantAndRegistration_WriteNothing()
    {
        var user = User(SeedUserId);
        var participants = _db.Get<IParticipantService>();

        Assert.Equal(ParticipantService.NationalIdInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => participants.AddAsync(user, "10000000147", "Yeni Kişi", "yeni@example.com", Ct))).Message);
        Assert.Equal(ParticipantService.FullNameInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => participants.AddAsync(user, NewNationalId, "Yeni  Kişi", "yeni@example.com", Ct))).Message);
        Assert.Equal(ParticipantService.EmailInvalid, (await Assert.ThrowsAsync<BusinessRuleException>(() => participants.AddAsync(user, NewNationalId, "Yeni Kişi", "yeni@example", Ct))).Message);
        Assert.Equal(RegistrationService.NationalIdFormat, (await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(user, ConcertId, "1000000014", Ct))).Message);
        Assert.Equal(RegistrationService.EventRequired, (await Assert.ThrowsAsync<BusinessRuleException>(() => Registrations.RegisterAsync(user, 0, DenizNationalId, Ct))).Message);

        await using var context = _db.CreateContext();
        Assert.Equal(3, await context.Participants.CountAsync(Ct));
        Assert.Equal(0, await context.Registrations.CountAsync(Ct));
        Assert.Equal(0, await context.Tickets.CountAsync(Ct));
    }
}
