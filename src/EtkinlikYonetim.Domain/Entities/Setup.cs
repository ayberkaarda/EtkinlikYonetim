namespace EtkinlikYonetim.Domain.Entities;

/// <summary>
/// A single use setup key (table KurulumAnahtarlari). Keys are created only by the database owner with
/// docs/issue-setup-key.sql, which shows the key once and stores its SHA-256 hash. The application's
/// login may read the rows and mark a key used (<see cref="Consumed"/>, <see cref="ConsumedAt"/>),
/// nothing else: it can neither create, replace, extend nor delete a key. A key lets the first admin be
/// created on a database that has accounts but no admin, once, before it expires.
/// </summary>
public sealed class SetupKey
{
    public int Id { get; set; }

    /// <summary>SHA-256 of the normalized key (32 upper case hexadecimal characters, ASCII).</summary>
    public byte[] KeyHash { get; set; } = [];

    /// <summary>UTC time of issue.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC time after which the key is no longer accepted.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Set in the same transaction as the admin write the key authorized; the issuing script also sets
    /// it to revoke a key.
    /// </summary>
    public bool Consumed { get; set; }

    /// <summary>UTC time the key was used or revoked, or <see langword="null"/> while it is unused.</summary>
    public DateTime? ConsumedAt { get; set; }
}
