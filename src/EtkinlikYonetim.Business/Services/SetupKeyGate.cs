using System.Security.Cryptography;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Pause after a refused setup key; replaceable so tests do not wait.</summary>
public sealed class SetupKeyThrottle(TimeSpan delay)
{
    public TimeSpan Delay { get; } = delay;

    public static SetupKeyThrottle Default { get; } = new(SetupKeyPolicy.FailureDelay);
}

/// <summary>
/// Turns a typed setup key into the <see cref="SetupKeyAttempt"/> a repository checks inside its write
/// transaction. Keys are never created here, and never logged or stored.
/// </summary>
internal sealed class SetupKeyGate(TimeProvider timeProvider, SetupKeyThrottle throttle)
{
    private static readonly byte[] NoKey = new byte[32];

    /// <summary>
    /// The attempt for <paramref name="typed"/>, or <see langword="null"/> when nothing was typed. A key
    /// of the wrong form never matches; every stored key is compared in constant time either way.
    /// </summary>
    public SetupKeyAttempt? CreateAttempt(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return null;
        }

        var wellFormed = SetupKeyPolicy.TryNormalize(typed, out var key);
        var given = wellFormed ? SetupKeyPolicy.Hash(key) : NoKey;
        return new SetupKeyAttempt(
            stored =>
            {
                // No early exit: the time does not depend on which key, if any, matched.
                int? match = null;
                foreach (var candidate in stored)
                {
                    if (CryptographicOperations.FixedTimeEquals(candidate.KeyHash, given) && wellFormed)
                    {
                        match ??= candidate.Id;
                    }
                }

                return match;
            },
            timeProvider.GetUtcNow().UtcDateTime);
    }

    /// <summary>The fixed pause after a refused key, outside any transaction.</summary>
    public Task DelayAfterRefusalAsync(CancellationToken cancellationToken) =>
        throttle.Delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(throttle.Delay, cancellationToken);
}
