using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Every write checks its account again in the transaction that writes, not only when the operation
/// starts. Here the account is disabled right after the service's session check passed, the moment a
/// concurrent account change could land in; each write must then change nothing and end the session.
/// </summary>
public sealed class ActorRevalidationTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    public static TheoryData<string> Writes { get; } =
    [
        "event-create",
        "event-update",
        "event-cancel",
        "participant-add",
        "register",
        "registration-cancel",
        "check-in",
        "account-change",
    ];

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task AccountDisabledAfterTheSessionCheck_TheWriteChangesNothing(string write)
    {
        var guard = new RevokeAfterCheckGuard(_db);
        await using var app = _db.BuildServices(services => services.AddSingleton<ISessionGuard>(guard));
        var admin = Admin(SeedAdminId);
        var user = User(SeedUserId);

        // Prepared before the guard is armed: an event of today with one seat taken by the user.
        var today = await app.GetRequiredService<IEventService>().CreateAsync(admin, "Bugün", Start.UtcDateTime.Date, 5, Ct);
        var receipt = await app.GetRequiredService<IRegistrationService>().RegisterAsync(user, today, DenizNationalId, Ct);
        var concert = (await app.GetRequiredService<IEventService>().GetEventsAsync(admin, Ct)).Single(e => e.Id == ConcertId);
        var acting = write is "event-create" or "event-update" or "event-cancel" or "check-in" or "account-change" ? SeedAdminId : SeedUserId;
        var before = await SnapshotAsync(acting);

        guard.Armed = true;
        Func<Task> operation = write switch
        {
            "event-create" => () => app.GetRequiredService<IEventService>().CreateAsync(admin, "Yeni", Start.UtcDateTime.Date.AddDays(3), 5, Ct),
            "event-update" => () => app.GetRequiredService<IEventService>().UpdateAsync(admin, concert, concert.Date.ToDateTime(TimeOnly.MinValue), concert.Capacity + 1, "Değişti", Ct),
            "event-cancel" => () => app.GetRequiredService<IEventService>().CancelAsync(admin, ConcertId, Ct),
            "participant-add" => () => app.GetRequiredService<IParticipantService>().AddAsync(user, NewNationalId, "Yeni Kişi", "yeni@example.com", Ct),
            "register" => () => app.GetRequiredService<IRegistrationService>().RegisterAsync(user, ConcertId, EceNationalId, Ct),
            "registration-cancel" => () => app.GetRequiredService<IRegistrationService>().CancelAsync(user, receipt.RegistrationId, Ct),
            "check-in" => () => app.GetRequiredService<ICheckInService>().CheckInAsync(admin, today, receipt.TicketCode, Ct),
            _ => () => app.GetRequiredService<IAccountService>().SetEnabledAsync(admin, SeedUserId, false, Ct),
        };

        await Assert.ThrowsAsync<SessionExpiredException>(operation);

        Assert.True(guard.Revoked);
        Assert.Equal(before, await SnapshotAsync(acting));
    }

    /// <summary>
    /// The account changes of an admin check the session version too: an admin disabled and enabled again
    /// since the session was issued is an enabled admin, but not the one the session was issued for.
    /// </summary>
    [Fact]
    public async Task AccountChange_ActorDisabledAndEnabledAgain_IsRefused()
    {
        var accounts = _db.Get<IAccountService>();
        await accounts.ChangeRoleAsync(Admin(SeedAdminId), SeedUserId, UserRole.Admin, Ct);
        var second = new UserSession(SeedUserId, "ornek", UserRole.Admin, sessionVersion: 1);
        await accounts.SetEnabledAsync(second, SeedAdminId, false, Ct);
        await accounts.SetEnabledAsync(second, SeedAdminId, true, Ct);

        var status = await _db.Get<IUserRepository>().SetDisabledAsync(Access.ActorOf(Admin(SeedAdminId)), SeedUserId, true, Ct);

        Assert.Equal(AccountChangeStatus.ActorNotAuthorized, status);
        await using var context = _db.CreateContext();
        Assert.False((await context.Users.SingleAsync(u => u.Id == SeedUserId, Ct)).Disabled);
    }

    /// <summary>Every row a write could have changed, compared before and after.</summary>
    private async Task<string> SnapshotAsync(int actingUserId)
    {
        await using var context = _db.CreateContext();
        var events = await context.Events.OrderBy(e => e.Id).Select(e => $"{e.Id}:{e.Name}:{e.Date}:{e.Capacity}:{e.RegisteredCount}:{e.Description}:{e.Cancelled}").ToListAsync(Ct);
        var participants = await context.Participants.CountAsync(Ct);
        var registrations = await context.Registrations.OrderBy(r => r.Id).Select(r => $"{r.Id}:{r.Status}").ToListAsync(Ct);
        var tickets = await context.Tickets.OrderBy(t => t.Id).Select(t => $"{t.Id}:{t.CheckedInAt}").ToListAsync(Ct);
        // The acting account is the one disabled on purpose; every other account must stay as it was.
        var users = await context.Users.OrderBy(u => u.Id).Where(u => u.Id != actingUserId).Select(u => $"{u.Id}:{u.Role}:{u.Disabled}:{u.SessionVersion}").ToListAsync(Ct);
        return string.Join('|', events) + "#" + participants + "#" + string.Join('|', registrations) + "#" + string.Join('|', tickets) + "#" + string.Join('|', users);
    }

    /// <summary>
    /// The real session check; when armed, the account it just accepted is disabled before the operation
    /// goes on, as an admin's change committed in that moment would.
    /// </summary>
    private sealed class RevokeAfterCheckGuard(SqliteDatabase db) : ISessionGuard
    {
        public bool Armed { get; set; }

        public bool Revoked { get; private set; }

        public async Task EnsureCurrentAsync(UserSession session, CancellationToken cancellationToken)
        {
            await new SessionGuard(db.Get<IUserRepository>(), NullAuditLog.Instance).EnsureCurrentAsync(session, cancellationToken);
            if (!Armed)
            {
                return;
            }

            Armed = false;
            Revoked = true;
            await using var context = db.CreateContext();
            await context.Users
                .Where(u => u.Id == session.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Disabled, true).SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1), cancellationToken);
        }
    }
}
