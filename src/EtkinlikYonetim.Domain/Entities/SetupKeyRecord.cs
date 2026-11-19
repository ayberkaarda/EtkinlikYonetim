namespace EtkinlikYonetim.Domain.Entities;

/// <summary>
/// A setup key issued for this database (table KurulumAnahtarlari). Only the SHA-256 hash of the key is
/// stored; the key itself is shown once to the operator who created it. A key lets the first admin be
/// created on a database with accounts but no admin, once, before it expires and while it has had
/// fewer than the allowed wrong attempts.
/// </summary>
public sealed class SetupKeyRecord
{
    public int Id { get; set; }

    /// <summary>SHA-256 hash of the normalized key.</summary>
    public byte[] KeyHash { get; set; } = [];

    /// <summary>UTC time of issue.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC time after which the key is no longer accepted.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>UTC time the key was used, or <see langword="null"/> while it is unused.</summary>
    public DateTime? ConsumedAt { get; set; }

    /// <summary>Wrong keys entered while this key was the active one.</summary>
    public int FailedAttempts { get; set; }
}