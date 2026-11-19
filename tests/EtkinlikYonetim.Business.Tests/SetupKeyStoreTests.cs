using EtkinlikYonetim.Business.Security;
using Microsoft.Extensions.Time.Testing;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>The local setup key: random, single use, typed as shown, and limited against guessing.</summary>
public sealed class SetupKeyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "etkinlik-setup-key-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 2, 10, 10, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void EnsureIssued_WritesOneRandomKeyAndKeepsAnUnusedOne()
    {
        var store = new FileSetupKeyStore(_directory, null, _clock);

        store.EnsureIssued();
        var key = File.ReadAllText(store.KeyFilePath!).Trim();
        store.EnsureIssued();

        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){3}$", key);
        Assert.Equal(key, File.ReadAllText(store.KeyFilePath!).Trim());
        Assert.NotEqual(key, FileSetupKeyStore.NewCode(20));
    }

    [Fact]
    public void TryConsume_RightKey_WorksOnceAndDeletesTheFile()
    {
        var store = new FileSetupKeyStore(_directory, null, _clock);
        store.EnsureIssued();
        var key = File.ReadAllText(store.KeyFilePath!).Trim();

        Assert.Equal(SetupKeyCheck.Consumed, store.TryConsume(" " + key.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) + " "));
        Assert.False(File.Exists(store.KeyFilePath));
        Assert.Equal(SetupKeyCheck.Wrong, store.TryConsume(key));

        // A new start issues a new key; the used one stays invalid.
        store.EnsureIssued();
        Assert.NotEqual(key, File.ReadAllText(store.KeyFilePath!).Trim());
        Assert.Equal(SetupKeyCheck.Wrong, store.TryConsume(key));
    }

    [Fact]
    public void TryConsume_WrongKeys_AreCountedAndLockEvenTheRightKey()
    {
        var store = new FileSetupKeyStore(_directory, null, _clock);
        store.EnsureIssued();
        var key = File.ReadAllText(store.KeyFilePath!).Trim();

        for (var i = 1; i < SetupKeyPolicy.MaxFailedAttempts; i++)
        {
            Assert.Equal(SetupKeyCheck.Wrong, store.TryConsume("YANLIS-ANAHTAR-" + i));
        }

        Assert.Equal(SetupKeyCheck.LockedOut, store.TryConsume(string.Empty));

        // The count survives a restart (a new store on the same folder) and the key is kept.
        var restarted = new FileSetupKeyStore(_directory, null, _clock);
        Assert.Equal(SetupKeyCheck.LockedOut, restarted.TryConsume(key));
        _clock.Advance(SetupKeyPolicy.LockoutDuration - TimeSpan.FromSeconds(1));
        Assert.Equal(SetupKeyCheck.LockedOut, restarted.TryConsume(key));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SetupKeyCheck.Consumed, restarted.TryConsume(key));
    }

    [Fact]
    public async Task TryConsume_ConcurrentRightKeys_ExactlyOneWins()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new FileSetupKeyStore(_directory, null, _clock);
        store.EnsureIssued();
        var key = File.ReadAllText(store.KeyFilePath!).Trim();

        using var start = new ManualResetEventSlim(false);
        var attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(
                () =>
                {
                    start.Wait(ct);
                    return new FileSetupKeyStore(_directory, null, _clock).TryConsume(key);
                },
                ct))
            .ToList();
        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r == SetupKeyCheck.Consumed);
    }

    [Fact]
    public void TryConsume_EnvironmentKey_WorksOnceAndWritesNoKeyFile()
    {
        const string Key = "operator-chosen-setup-key";
        var store = new FileSetupKeyStore(_directory, Key, _clock);
        store.EnsureIssued();

        Assert.Null(store.KeyFilePath);
        Assert.False(File.Exists(Path.Combine(_directory, FileSetupKeyStore.KeyFileName)));
        Assert.Equal(SetupKeyCheck.Wrong, store.TryConsume("operator-chosen-setup-kez"));
        Assert.Equal(SetupKeyCheck.Consumed, store.TryConsume(Key));
        Assert.Equal(SetupKeyCheck.Wrong, new FileSetupKeyStore(_directory, Key, _clock).TryConsume(Key));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(_directory),
            file => File.ReadAllText(file).Contains(FileSetupKeyStore.Normalize(Key), StringComparison.Ordinal));
    }

    [Fact]
    public void Constructor_ShortEnvironmentKey_IsIgnoredInFavourOfTheFile()
    {
        var store = new FileSetupKeyStore(_directory, "kisa", _clock);

        Assert.NotNull(store.KeyFilePath);
        Assert.Equal(SetupKeyCheck.Wrong, store.TryConsume("kisa"));
    }
}
