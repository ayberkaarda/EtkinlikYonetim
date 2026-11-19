using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Security;
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
        Assert.True(await _db.Get<ISetupService>().IsAdminMissingAsync(Ct));
    }

    [Fact]
    public async Task Setup_CreatesTheFirstAdminWithTheChosenPasswordOnce()
    {
        var setup = _db.Get<ISetupService>();

        var mismatch = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Gizli.Sifre2", Ct));
        var weak = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", "kisa", "kisa", Ct));
        Assert.Equal(AuthService.PasswordsDoNotMatch, mismatch.Message);
        Assert.Equal(AuthService.PasswordInvalid, weak.Message);
        Assert.True(await setup.IsSetupRequiredAsync(Ct));

        var session = await setup.CreateFirstAdminAsync(null, " kurucu ", "Gizli.Sifre1", "Gizli.Sifre1", Ct);

        Assert.True(session.IsAdmin);
        Assert.Equal("kurucu", session.UserName);
        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        var signedIn = await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct);
        Assert.Equal(session.UserId, signedIn.UserId);
        Assert.True(signedIn.IsAdmin);

        var again = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "ikinci", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.AlreadyDone, again.Message);
        await using var context = _db.CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(Ct));
    }

    /// <summary>
    /// A database with accounts but no admin (an upgraded or emptied one) keeps its sign-in and is no
    /// invitation to become its founder: the first admin needs the local, single use setup key.
    /// </summary>
    [Fact]
    public async Task Setup_ExistingUsers_RequiresBootstrapAuthority()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();
        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        Assert.True(await setup.IsAdminMissingAsync(Ct));
        var key = TestServices.ReadSetupKey(_db.Services);

        var withoutKey = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        var wrongKey = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("YANLIS-ANAHTAR", "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.SetupKeyInvalid, withoutKey.Message);
        Assert.Equal(SetupService.SetupKeyInvalid, wrongKey.Message);
        Assert.Equal(0, await CountAdminsAsync());
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct));

        // A taken name is refused before the key is asked for, so the key stays valid; the account keeps its role.
        var taken = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "uye", "Baska.Sifre1", "Baska.Sifre1", Ct));
        Assert.Equal(AuthService.UserNameTaken, taken.Message);
        Assert.Equal(UserRole.User, (await _db.Get<IAuthService>().SignInAsync("uye", "Gizli.Sifre1", Ct)).Role);

        var session = await setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        Assert.True(session.IsAdmin);
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct)).IsAdmin);
        Assert.Equal(1, await CountAdminsAsync());

        // Single use: with the admin gone again the same key is no authority any more.
        await using (var context = _db.CreateContext())
        {
            await context.Users.Where(u => u.Role == UserRole.Admin).ExecuteDeleteAsync(Ct);
        }

        var reused = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "ikinci", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.SetupKeyInvalid, reused.Message);
        Assert.Equal(0, await CountAdminsAsync());
    }

    /// <summary>Wrong setup keys are counted; after the limit even the right key waits for the lockout.</summary>
    [Fact]
    public async Task Setup_ExistingUsersAndRepeatedWrongKeys_LocksTheKey()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();
        var key = TestServices.ReadSetupKey(_db.Services);

        for (var i = 1; i < SetupKeyPolicy.MaxFailedAttempts; i++)
        {
            var wrong = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("YANLIS" + i, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
            Assert.Equal(SetupService.SetupKeyInvalid, wrong.Message);
        }

        var last = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("YANLIS", "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        var right = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.SetupKeyLocked, last.Message);
        Assert.Equal(SetupService.SetupKeyLocked, right.Message);
        Assert.Equal(0, await CountAdminsAsync());

        _db.Clock.Advance(SetupKeyPolicy.LockoutDuration);
        Assert.True((await setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
        Assert.Equal(1, await CountAdminsAsync());
    }

    private async Task<int> CountAdminsAsync()
    {
        await using var context = _db.CreateContext();
        return await context.Users.CountAsync(u => u.Role == UserRole.Admin, Ct);
    }
}