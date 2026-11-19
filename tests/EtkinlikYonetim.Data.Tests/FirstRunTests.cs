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
    /// invitation to become its founder: the first admin needs a single use key that the database owner
    /// created for this database.
    /// </summary>
    [Fact]
    public async Task Setup_ExistingUsers_RequiresBootstrapAuthority()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();
        Assert.False(await setup.IsSetupRequiredAsync(Ct));
        Assert.True(await setup.IsAdminMissingAsync(Ct));

        // Without a key, and before the owner created one, no value is an authority, also not one in the key format.
        var missing = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(null, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.SetupKeyRequired, missing.Message);
        await AssertRefusedAsync(setup, NewKey());
        var key = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        await AssertRefusedAsync(setup, "YANLIS-ANAHTAR");
        await AssertRefusedAsync(setup, NewKey());
        await AssertRefusedAsync(setup, key[..^1]);
        Assert.Equal(0, await CountAdminsAsync());
        await Assert.ThrowsAsync<BusinessRuleException>(() => _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct));

        // A taken name is refused before the key is checked, so the key stays valid; the account keeps its role.
        var taken = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(key, "uye", "Baska.Sifre1", "Baska.Sifre1", Ct));
        Assert.Equal(AuthService.UserNameTaken, taken.Message);
        Assert.Equal(UserRole.User, (await _db.Get<IAuthService>().SignInAsync("uye", "Gizli.Sifre1", Ct)).Role);

        // Typed as shown or in lower case with spaces instead of dashes.
        var session = await setup.CreateFirstAdminAsync(" " + key.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) + " ", "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        Assert.True(session.IsAdmin);
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct)).IsAdmin);
        Assert.Equal(1, await CountAdminsAsync());
        await using (var context = _db.CreateContext())
        {
            var used = await context.SetupKeys.SingleAsync(Ct);
            Assert.True(used.Consumed);
            Assert.Equal(_db.Clock.GetUtcNow().UtcDateTime, used.ConsumedAt);
        }

        // Single use: with the admin gone again the same key is no authority any more.
        await DeleteAdminsAsync();
        await AssertRefusedAsync(setup, key);
        Assert.Equal(0, await CountAdminsAsync());
    }

    /// <summary>
    /// Wrong keys from another application instance (someone at the setup form who does not know the
    /// key) are refused without writing anything: there is no attempt counter, so they can never
    /// revoke or lock the founder's key.
    /// </summary>
    [Fact]
    public async Task Setup_WrongAttemptsFromAnotherClientDoNotRevokeFounderKey()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var key = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        var before = await KeyRowsAsync();

        await using (var attacker = _db.BuildServices())
        {
            var setup = attacker.GetRequiredService<ISetupService>();
            string[] wrong = ["yanlis", " - - ", NewKey(), NewKey().ToLowerInvariant(), key[..^1] + (key[^1] == '0' ? '1' : '0')];
            for (var i = 0; i < 20; i++)
            {
                await AssertRefusedAsync(setup, wrong[i % wrong.Length]);
            }
        }

        Assert.Equal(before, await KeyRowsAsync());
        Assert.Equal(0, await CountAdminsAsync());

        await using var founder = _db.BuildServices();
        Assert.True((await founder.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
    }

    /// <summary>A key lives for 24 hours; the application never revokes or replaces a key, every unexpired one works once.</summary>
    [Fact]
    public async Task Setup_ExpiredKey_IsRefused()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var setup = _db.Get<ISetupService>();

        var expired = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        _db.Clock.Advance(SetupKeyPolicy.Lifetime);
        await AssertRefusedAsync(setup, expired);

        var older = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        var newer = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        _db.Clock.Advance(SetupKeyPolicy.Lifetime - TimeSpan.FromSeconds(1));
        Assert.True((await setup.CreateFirstAdminAsync(older, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);

        // The other key is still unused, but an admin exists now: no key is accepted any more.
        var completed = await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync(newer, "ikinci", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(SetupService.AlreadyDone, completed.Message);
        await using var context = _db.CreateContext();
        Assert.Equal(1, await context.SetupKeys.CountAsync(k => k.Consumed, Ct));
    }

    /// <summary>
    /// A client cannot make itself the founder: while an admin with a password exists even an unused
    /// key of the owner is not accepted, a value chosen on the client (ETKINLIK_SETUP_KEY of its own
    /// profile) is no authority, and nothing about the admin account or the keys changes.
    /// </summary>
    [Fact]
    public async Task SetupAuthority_UntrustedProfileCannotRecoverAdmin()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var ownerKey = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        var setup = _db.Get<ISetupService>();
        await setup.CreateFirstAdminAsync(ownerKey, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var spare = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        var before = await AdminRowAsync();
        var keys = await KeyRowsAsync();

        await using var untrusted = _db.BuildServices(s => s.AddSingleton(new SetupKeyEnvironment(NewKey())));
        var untrustedSetup = untrusted.GetRequiredService<ISetupService>();
        var prefill = untrustedSetup.GetEnvironmentKey();
        Assert.False(prefill.Invalid);
        foreach (var value in new[] { prefill.Key, spare })
        {
            var refused = await Assert.ThrowsAsync<BusinessRuleException>(
                () => untrustedSetup.CreateFirstAdminAsync(value, "ikinci", "Baska.Sifre1", "Baska.Sifre1", Ct));
            Assert.Equal(SetupService.AlreadyDone, refused.Message);
        }

        Assert.Equal(keys, await KeyRowsAsync());
        Assert.Equal(before, await AdminRowAsync());
        Assert.Equal(1, await CountAdminsAsync());
        Assert.True((await _db.Get<IAuthService>().SignInAsync("kurucu", "Gizli.Sifre1", Ct)).IsAdmin);
    }

    /// <summary>
    /// A database without an admin and an untrusted profile: a key it invents (or sets in its own
    /// ETKINLIK_SETUP_KEY) is no authority, and a key used through one application instance cannot be
    /// used again through another one (another profile or computer), also not from the environment.
    /// </summary>
    [Fact]
    public async Task SetupAuthority_KeyUsedOnOneProfile_IsRefusedOnAnother()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var key = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());

        await using (var untrusted = _db.BuildServices(s => s.AddSingleton(new SetupKeyEnvironment(NewKey()))))
        {
            var invented = untrusted.GetRequiredService<ISetupService>();
            await AssertRefusedAsync(invented, invented.GetEnvironmentKey().Key);
        }

        await using (var first = _db.BuildServices())
        {
            Assert.True((await first.GetRequiredService<ISetupService>().CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
        }

        await DeleteAdminsAsync();
        await using var second = _db.BuildServices(s => s.AddSingleton(new SetupKeyEnvironment(key.ToLowerInvariant())));
        var secondSetup = second.GetRequiredService<ISetupService>();
        Assert.Equal(key, secondSetup.GetEnvironmentKey().Key);
        await AssertRefusedAsync(secondSetup, secondSetup.GetEnvironmentKey().Key);
        Assert.Equal(0, await CountAdminsAsync());
    }

    /// <summary>The key is used up in the transaction that writes the admin: a failed insert or commit leaves it unused.</summary>
    [Fact]
    public async Task Setup_FailedWriteDoesNotSpendAuthority()
    {
        await _db.Get<IAuthService>().RegisterAsync("uye", "Gizli.Sifre1", "Gizli.Sifre1", Ct);
        var key = await TestServices.AddSetupKeyAsync(_db.Services, _db.Clock.GetUtcNow());
        var insertFailure = new FailingInsertInterceptor("Kullanicilar");
        var commitFailure = new CommitFailure();
        await using var provider = _db.BuildServices(insertFailure, commitFailure);
        var setup = provider.GetRequiredService<ISetupService>();

        insertFailure.FailNextInsert();
        await Assert.ThrowsAnyAsync<Exception>(() => setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.Equal(1, insertFailure.Failures);
        Assert.Equal(0, await CountAdminsAsync());
        Assert.Equal(0, await ConsumedKeysAsync());

        commitFailure.Arm();
        await Assert.ThrowsAnyAsync<Exception>(() => setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        Assert.True(commitFailure.Fired);
        Assert.Equal(0, await CountAdminsAsync());
        Assert.Equal(0, await ConsumedKeysAsync());

        Assert.True((await setup.CreateFirstAdminAsync(key, "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct)).IsAdmin);
        Assert.Equal(1, await ConsumedKeysAsync());
        await DeleteAdminsAsync();
        await AssertRefusedAsync(setup, key);
    }

    /// <summary>A random key of the right form that was never stored.</summary>
    private static string NewKey() => SetupKeyPolicy.Format(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)));

    private async Task<int> ConsumedKeysAsync()
    {
        await using var context = _db.CreateContext();
        return await context.SetupKeys.CountAsync(k => k.Consumed || k.ConsumedAt != null, Ct);
    }

    /// <summary>Every stored key with all of its columns, to show that nothing changed.</summary>
    private async Task<string> KeyRowsAsync()
    {
        await using var context = _db.CreateContext();
        var rows = await context.SetupKeys.AsNoTracking().OrderBy(k => k.Id).ToListAsync(Ct);
        return string.Join(
            ";",
            rows.Select(k => FormattableString.Invariant($"{k.Id}/{Convert.ToHexString(k.KeyHash)}/{k.CreatedAt:O}/{k.ExpiresAt:O}/{k.Consumed}/{k.ConsumedAt:O}")));
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