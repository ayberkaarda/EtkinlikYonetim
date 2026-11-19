using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>
/// Setup keys as the owner's script issues them: 128 random bits as 32 hexadecimal characters, typed in
/// any case and grouping, checked for form only after normalization, compared in constant time, never
/// created by the application.
/// </summary>
public sealed class SetupKeyPolicyTests
{
    private static readonly DateTime Now = new(2026, 3, 2, 10, 10, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Key_HasAtLeast128BitsOfEntropy()
    {
        // Every one of the 32 characters is one of 16 hexadecimal digits: 4 bits each.
        var bits = SetupKeyPolicy.KeyLength * Math.Log2(16);
        Assert.True(bits >= 128, $"{bits} bits.");
        Assert.True(SetupKeyPolicy.EntropyBits <= bits);
        Assert.True(SetupKeyPolicy.EntropyBits >= 128);

        // A shorter value never passes as a key.
        Assert.False(SetupKeyPolicy.TryNormalize(new string('A', SetupKeyPolicy.KeyLength - 1), out _));
    }

    [Fact]
    public void Hash_IsSha256OfTheNormalizedAsciiText_AsTheScriptComputesIt()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var shown = SetupKeyPolicy.Format(raw);

        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){7}$", shown);
        Assert.True(SetupKeyPolicy.TryNormalize(" " + shown.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) + "\t", out var normalized));
        Assert.Equal(raw, normalized);
        Assert.Equal(SHA256.HashData(Encoding.ASCII.GetBytes(raw)), SetupKeyPolicy.Hash(normalized));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("--------------------------------")]
    [InlineData("A-------------------------------")]
    [InlineData("A                               -")]
    [InlineData("0123-4567-89AB-CDEF-0123-4567-89AB-CDE")]
    [InlineData("0123-4567-89AB-CDEF-0123-4567-89AB-CDEF0")]
    [InlineData("0123-4567-89AB-CDEF-0123-4567-89AB-CDEG")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX")]
    public void TryNormalize_ValueThatIsNotAKeyAfterNormalization_IsRefused(string? value) =>
        Assert.False(SetupKeyPolicy.TryNormalize(value, out _));

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("yanlis", true, false)]
    [InlineData("----------------------------------", true, false)]
    [InlineData("0123456789abcdef0123456789abcdef", true, true)]
    public void Environment_OnlyFillsInAWellFormedKey(string? value, bool isSet, bool isValid)
    {
        var environment = new SetupKeyEnvironment(value);

        Assert.Equal(isSet, environment.IsSet);
        Assert.Equal(isValid, environment.IsValid);
        Assert.Equal(isValid ? "0123-4567-89AB-CDEF-0123-4567-89AB-CDEF" : null, environment.Key);
    }

    [Fact]
    public void Attempt_MatchesOnlyTheStoredKeyItWasMadeFrom()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var other = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        StoredSetupKey[] stored = [new(3, SetupKeyPolicy.Hash(other)), new(7, SetupKeyPolicy.Hash(raw))];
        var gate = new SetupKeyGate(new FakeTimeProvider(new DateTimeOffset(Now)), new SetupKeyThrottle(TimeSpan.Zero));

        Assert.Null(gate.CreateAttempt(null));
        Assert.Null(gate.CreateAttempt("   "));
        var attempt = gate.CreateAttempt(SetupKeyPolicy.Format(raw).ToLowerInvariant())!;
        Assert.Equal(Now, attempt.Now);
        Assert.Equal(7, attempt.FindMatch(stored));
        Assert.Null(gate.CreateAttempt(raw[..^1])!.FindMatch(stored));
        Assert.Null(gate.CreateAttempt(Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))!.FindMatch(stored));

        // A malformed key is compared with the all-zero placeholder and must not match even a stored zero hash.
        Assert.Null(gate.CreateAttempt("yanlis")!.FindMatch([new StoredSetupKey(1, new byte[32])]));
    }

    [Fact]
    public async Task RefusedKey_IsFollowedByTheFixedPause_AndNothingElse()
    {
        var users = new Mock<IUserRepository>(MockBehavior.Strict);
        users.Setup(u => u.AddFirstAdminAsync(It.IsAny<User>(), It.IsNotNull<SetupKeyAttempt>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirstAdminResult(FirstAdminStatus.NotAuthorized, 0));
        var delay = TimeSpan.FromMilliseconds(300);
        var service = new SetupService(
            users.Object,
            new PasswordHasher(iterations: 1_000),
            new SetupKeyGate(new FakeTimeProvider(new DateTimeOffset(Now)), new SetupKeyThrottle(delay)),
            new SetupKeyEnvironment(null));

        var watch = Stopwatch.StartNew();
        var refused = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateFirstAdminAsync("yanlis", "kurucu", "Gizli.Sifre1", "Gizli.Sifre1", Ct));
        watch.Stop();

        Assert.Equal(SetupService.SetupKeyInvalid, refused.Message);
        Assert.True(watch.Elapsed >= delay - TimeSpan.FromMilliseconds(20), $"Paused {watch.Elapsed}.");
        users.Verify(u => u.AddFirstAdminAsync(It.IsAny<User>(), It.IsNotNull<SetupKeyAttempt>(), It.IsAny<CancellationToken>()), Times.Once);
        users.VerifyNoOtherCalls();
        Assert.Equal(TimeSpan.FromSeconds(1), SetupKeyPolicy.FailureDelay);
    }
}
