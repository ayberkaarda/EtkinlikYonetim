using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static EtkinlikYonetim.Data.Tests.TestServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// Account management and database checked sessions end to end on SQLite: a session keeps working only
/// while its account exists, is enabled and still has the role it signed in with.
/// </summary>
public sealed class AccountManagementTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IAccountService Accounts => _db.Get<IAccountService>();

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task DemotedAdminSession_CannotManageEvents()
    {
        var demoted = await PromoteAndSignInAsync("ornek", "Demo.Kullanici1");
        var admin = await _db.Get<IAuthService>().SignInAsync("admin", "Demo.Admin1", Ct);
        await _db.Get<IEventService>().CreateAsync(demoted, "Önce", Start.Date.AddDays(5), 10, Ct);

        await Accounts.ChangeRoleAsync(admin, demoted.UserId, UserRole.User, Ct);

        await Assert.ThrowsAsync<SessionExpiredException>(() => _db.Get<IEventService>().CreateAsync(demoted, "Sonra", Start.Date.AddDays(5), 10, Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(() => _db.Get<IParticipantService>().GetByEventAsync(demoted, ConcertId, Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(() => Accounts.SetEnabledAsync(demoted, admin.UserId, false, Ct));
        await using var context = _db.CreateContext();
        Assert.Equal(["Önce"], await context.Events.Where(e => e.Name == "Önce" || e.Name == "Sonra").Select(e => e.Name).ToListAsync(Ct));
        Assert.False((await context.Users.SingleAsync(u => u.Id == admin.UserId, Ct)).Disabled);
    }

    [Fact]
    public async Task DisabledAccount_CannotSignInAndItsOpenSessionCannotCancel()
    {
        var user = await _db.Get<IAuthService>().SignInAsync("ornek", "Demo.Kullanici1", Ct);
        var receipt = await _db.Get<IRegistrationService>().RegisterAsync(user, ConcertId, DenizNationalId, Ct);

        await Accounts.SetEnabledAsync(Admin(SeedAdminId), user.UserId, false, Ct);

        await Assert.ThrowsAsync<SessionExpiredException>(() => _db.Get<IRegistrationService>().CancelAsync(user, receipt.RegistrationId, Ct));
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("ornek", "Demo.Kullanici1", Ct));
        Assert.Equal(AuthService.CredentialsInvalid, refused.Message);

        await Accounts.SetEnabledAsync(Admin(SeedAdminId), user.UserId, true, Ct);
        await _db.Get<IRegistrationService>().CancelAsync(user, receipt.RegistrationId, Ct);
    }

    [Fact]
    public async Task DeletedAccountSession_IsRejected()
    {
        var ghost = User(99);

        await Assert.ThrowsAsync<SessionExpiredException>(() => _db.Get<IRegistrationService>().GetMyRegistrationsAsync(ghost, Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(() => _db.Get<IEventService>().CreateAsync(Admin(98), "Hayalet", Start.Date.AddDays(5), 10, Ct));
    }

    [Fact]
    public async Task User_CannotRaiseTheirOwnRole()
    {
        var user = await _db.Get<IAuthService>().SignInAsync("ornek", "Demo.Kullanici1", Ct);

        await Assert.ThrowsAsync<AccessDeniedException>(() => Accounts.ChangeRoleAsync(user, user.UserId, UserRole.Admin, Ct));

        await using var context = _db.CreateContext();
        Assert.Equal(UserRole.User, (await context.Users.SingleAsync(u => u.Id == user.UserId, Ct)).Role);
    }

    /// <summary>
    /// The demonstration database has one admin. A second one can be made; then the first can be made
    /// a user, but the remaining one can neither be disabled nor demoted, not even by itself.
    /// </summary>
    [Fact]
    public async Task LastEnabledAdmin_IsKept()
    {
        var second = await PromoteAndSignInAsync("ornek", "Demo.Kullanici1");

        await Accounts.ChangeRoleAsync(second, SeedAdminId, UserRole.User, Ct);

        var own = await Assert.ThrowsAsync<BusinessRuleException>(() => Accounts.ChangeRoleAsync(second, second.UserId, UserRole.User, Ct));
        Assert.Equal(AccountService.OwnAccount, own.Message);
        await using (var context = _db.CreateContext())
        {
            Assert.Equal(1, await context.Users.CountAsync(u => u.Role == UserRole.Admin && !u.Disabled, Ct));
        }

        // The repository itself keeps the last one, whoever asks.
        var users = _db.Get<Domain.Repositories.IUserRepository>();
        Assert.Equal(Domain.Repositories.AccountChangeStatus.LastActiveAdmin, await users.SetDisabledAsync(second.UserId, second.UserId, true, Ct));
        Assert.Equal(Domain.Repositories.AccountChangeStatus.ActorNotAuthorized, await users.SetRoleAsync(SeedAdminId, second.UserId, UserRole.User, Ct));
        Assert.Equal(Domain.Repositories.AccountChangeStatus.NotFound, await users.SetRoleAsync(second.UserId, 77, UserRole.User, Ct));
    }

    [Fact]
    public async Task Accounts_AreListedForAdminsOnly()
    {
        var accounts = await Accounts.GetAccountsAsync(Admin(SeedAdminId), Ct);

        Assert.Equal([("admin", UserRole.Admin, false), ("ornek", UserRole.User, false)], accounts.Select(a => (a.UserName, a.Role, a.Disabled)));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Accounts.GetAccountsAsync(User(SeedUserId), Ct));
    }

    private async Task<UserSession> PromoteAndSignInAsync(string userName, string password)
    {
        var user = await _db.Get<IAuthService>().SignInAsync(userName, password, Ct);
        await Accounts.ChangeRoleAsync(Admin(SeedAdminId), user.UserId, UserRole.Admin, Ct);
        return await _db.Get<IAuthService>().SignInAsync(userName, password, Ct);
    }
}
