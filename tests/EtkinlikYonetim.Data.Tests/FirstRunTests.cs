using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    /// invitation to become its founder: the first admin needs a single use key issued for this database.
    /// </summary>
    [Fact]
    public async Task Setup_ExistingUsers_RequiresBootstrapAuthority()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();
        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        Assert.True(await setup.IsAdminMissingAsync(Ct));

        // Before a key is issued no value is an authority, also not one in the key format.
        await AssertRefusedAsync(setup, null);
        await AssertRefusedAsync(setup, SetupKeyPolicy.NewKey());
        var key = await TestServices.CreateSetupKeyAsync(_db.Services);
        await AssertRefusedAsync(setup, "YANLIS-ANAHTAR");
        await AssertRefusedAsync(setup, SetupKeyPolicy.NewKey());
        Assert.Equal(0, await CountAdminsAsync());
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct));

        // A taken name is refused before the key is checked, so the key stays valid; the account keeps its role.
        var taken = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "uye", "Baska.Sifre1", "Baska.Sifre1", Ct));
        Assert.Equal(AuthService.UserNameTaken, taken.Message);
        Assert.Equal(UserRole.User, (await _db.Get<IAuthService>().SignInAsync("uye", "Gizli.Sifre1", Ct)).Role);

        // Typed as shown or in lower case without dashes.
        var session = await setup.CreateFirstAdminAsync(" " + key.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) + " ", "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        Assert.True(session.IsAdmin);
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct)).IsAdmin);
        Assert.Equal(1, await CountAdminsAsync());

        // Single use: with the admin gone again the same key is no authority any more.
        await DeleteAdminsAsync();
        await AssertRefusedAsync(setup, key);
        Assert.Equal(0, await CountAdminsAsync());
    }

    /// <summary>Wrong keys are counted in the database; after the limit even the right key is refused until a new one is issued.</summary>
    [Fact]
    public async Task Setup_ExistingUsersAndRepeatedWrongKeys_SpendTheKey()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();
        var key = await TestServices.CreateSetupKeyAsync(_db.Services);

        for (var i = 0; i < SetupKeyPolicy.MaxFailedAttempts; i++)
        {
            await AssertRefusedAsync(setup, i % 2 == 0 ? SetupKeyPolicy.NewKey() : "yanlis");
        }

        // Counted by the database, so another application instance sees the same count.
        await using (var other = _db.BuildServices())
        {
            await AssertRefusedAsync(other.GetRequiredService<ISetupService>(), key);
        }

        await using (var context = _db.CreateContext())
        {
            Assert.Equal(SetupKeyPolicy.MaxFailedAttempts, await context.SetupKeys.Select(k => k.FailedAttempts).SingleAsync(Ct));
        }

        var fresh = await TestServices.CreateSetupKeyAsync(_db.Services);
        Assert.True((await setup.CreateFirstAdminAsync(fresh, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
    }

    /// <summary>A key lives for 24 hours, and issuing a new one withdraws the older unused key.</summary>
    [Fact]
    public async Task Setup_ExpiredOrReplacedKey_IsRefused()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();

        var expired = await TestServices.CreateSetupKeyAsync(_db.Services);
        _db.Clock.Advance(SetupKeyPolicy.Lifetime);
        await AssertRefusedAsync(setup, expired);

        var replaced = await TestServices.CreateSetupKeyAsync(_db.Services);
        var current = await TestServices.CreateSetupKeyAsync(_db.Services);
        await AssertRefusedAsync(setup, replaced);
        _db.Clock.Advance(SetupKeyPolicy.Lifetime - TimeSpan.FromSeconds(1));
        Assert.True((await setup.CreateFirstAdminAsync(current, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
    }

    /// <summary>
    /// A client cannot make itself the founder: while an admin with a password exists no key can be
    /// issued, a value chosen on the client (as ETKINLIK_SETUP_KEY of its own profile) is no authority,
    /// and nothing about the admin account changes.
    /// </summary>
    [Fact]
    public async Task SetupAuthority_UntrustedProfileCannotRecoverAdmin()
    {
        var setup = _db.Get<ISetupService>();
        await setup.CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var before = await AdminRowAsync();

        await using var untrusted = _db.BuildServices();
        var untrustedSetup = untrusted.GetRequiredService<ISetupService>();
        var issue = await Assert.ThrowsAsync<BusinessRuleException>(() => untrustedSetup.CreateSetupKeyAsync(Ct));
        Assert.Equal(SetupService.SetupKeyNotNeeded, issue.Message);
        var ownKey = await Assert.ThrowsAsync<BusinessRuleException>(
            () => untrustedSetup.CreateFirstAdminAsync(SetupKeyPolicy.NewKey(), "kurucu", "Baska.Sifre1", "Baska.Sifre1", Ct));
        Assert.Equal(SetupService.AlreadyDone, ownKey.Message);

        await using (var context = _db.CreateContext())
        {
            Assert.Equal(0, await context.SetupKeys.CountAsync(Ct));
        }

        Assert.Equal(before, await AdminRowAsync());
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct)).IsAdmin);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("kurucu", "Baska.Sifre1", Ct));
    }

    /// <summary>A key used through one application instance cannot be used again through another one (another profile or computer).</summary>
    [Fact]
    public async Task SetupAuthority_KeyUsedOnOneProfile_IsRefusedOnAnother()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var key = await TestServices.CreateSetupKeyAsync(_db.Services);

        await using (var first = _db.BuildServices())
        {
            Assert.True((await first.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
        }

        await DeleteAdminsAsync();
        await using var second = _db.BuildServices();
        await AssertRefusedAsync(second.GetRequiredService<ISetupService>(), key);
        Assert.Equal(0, await CountAdminsAsync());
    }

    /// <summary>The key is used up in the transaction that writes the admin: a failed write leaves it unused.</summary>
    [Fact]
    public async Task Setup_FailedWriteDoesNotSpendAuthority()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var key = await TestServices.CreateSetupKeyAsync(_db.Services);
        var failure = new FailingInsertInterceptor("Kullanicilar");
        await using var provider = _db.BuildServices(failure);
        var setup = provider.GetRequiredService<ISetupService>();

        failure.FailNextInsert();
        await Assert.ThrowsAnyAsync<Exception>(() => setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));

        Assert.Equal(1, failure.Failures);
        Assert.Equal(0, await CountAdminsAsync());
        await using (var context = _db.CreateContext())
        {
            var record = await context.SetupKeys.SingleAsync(Ct);
            Assert.Null(record.ConsumedAt);
            Assert.Equal(0, record.FailedAttempts);
        }

        Assert.True((await setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
        await DeleteAdminsAsync();
        await AssertRefusedAsync(setup, key);
    }

    private static async Task AssertRefusedAsync(ISetupService setup, string? key)
    {
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.SetupKeyInvalid, refused.Message);
    }

    private async Task<int> CountAdminsAsync()
    {
        await using var context = _db.CreateContext();
        return await context.Users.CountAsync(u => u.Role == UserRole.Admin, Ct);
    }

    private async Task DeleteAdminsAsync()
    {
        await using var context = _db.CreateContext();
        await context.Users.Where(u => u.Role == UserRole.Admin).ExecuteDeleteAsync(Ct);
    }

    private async Task<string> AdminRowAsync()
    {
        await using var context = _db.CreateContext();
        var admin = await context.Users.AsNoTracking().SingleAsync(u => u.Role == UserRole.Admin, Ct);
        return admin.Id + "/" + admin.UserName + "/" + admin.PasswordHash + "/" + admin.PasswordFormat;
    }
}