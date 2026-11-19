using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// A default installation on SQLite: no demonstration data, no account with a known password, and
/// the first admin created once by the first-run setup.
/// </summary>
public sealed class FirstRunTests : IAsyncLifetime
{
    private readonly SqliteDatabase _db = new(demo: false);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _db.InitializeAsync();

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Initialize_DefaultConfiguration_CreatesNoAccountAndAsksForSetup()
    {
        await using var context = _db.CreateContext();

        Assert.Equal(0, await context.Users.CountAsync(Ct));
        Assert.Equal(0, await context.Events.CountAsync(Ct));
        Assert.Equal(0, await context.Participants.CountAsync(Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("admin", "Demo.Admin1", Ct));
        Assert.True(await _db.Get<ISetupService>().IsSetupRequiredAsync(Ct));
    }

    [Fact]
    public async Task Setup_CreatesTheFirstAdminWithTheChosenPasswordOnce()
    {
        var setup = _db.Get<ISetupService>();

        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("kurucu", "Gizli.Sifre1", "Gizli.Sifre2", Ct));
        var weak = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("kurucu", "kisa", "kisa", Ct));
        Assert.Equal(AuthService.PasswordsDoNotMatch, mismatch.Message);
        Assert.Equal(AuthService.PasswordInvalid, weak.Message);
        Assert.True(await setup.IsSetupRequiredAsync(Ct));

        var session = await setup.CreateFirstAdminAsync(" kurucu ", "Gizli.Sifre1", "Gizli.Sifre1", Ct);

        Assert.True(session.IsAdmin);
        Assert.Equal("kurucu", session.UserName);
        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        var signedIn = await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct);
        Assert.Equal(session.UserId, signedIn.UserId);
        Assert.True(signedIn.IsAdmin);

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("ikinci", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.AlreadyDone, again.Message);
        await using var context = _db.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(Ct));
    }

    /// <summary>Accounts without the admin role do not finish the setup, and their names stay theirs.</summary>
    [Fact]
    public async Task Setup_WithOnlyUserAccounts_IsStillRequiredAndKeepsTheirNames()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();
        Assert.True(await setup.IsSetupRequiredAsync(Ct));

        var taken = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("uye", "Baska.Sifre1", "Baska.Sifre1", Ct));
        Assert.Equal(AuthService.UserNameTaken, taken.Message);
        Assert.Equal(UserRole.User, (await _db.Get<IAuthService>().SignInAsync("uye", "Gizli.Sifre1", Ct)).Role);

        Assert.True((await setup.CreateFirstAdminAsync("kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
    }
}
