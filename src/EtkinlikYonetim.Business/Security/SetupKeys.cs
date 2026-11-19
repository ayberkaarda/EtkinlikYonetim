using System.Security.Cryptography;
using System.Text;

namespace EtkinlikYonetim.Business.Security;

/// <summary>
/// Rules of the setup key: the founder's single use authority over a database that has accounts but no
/// admin. Only the database owner creates keys, with docs/issue-setup-key.sql: 128 random bits shown
/// once as 32 hexadecimal characters, valid for 24 hours, stored as a SHA-256 hash. The application
/// only checks a given key and marks it used; it never creates, logs or stores a key.
/// </summary>
public static class SetupKeyPolicy
{
    /// <summary>Hexadecimal characters of a key: 16 random bytes, 128 bits.</summary>
    public const int KeyLength = 32;

    /// <summary>Random bits a key carries: four per hexadecimal character.</summary>
    public const int EntropyBits = KeyLength * 4;

    /// <summary>
    /// Environment variable that may fill in the key field of the setup form. The value is checked
    /// against the database like a typed key; on its own it grants nothing.
    /// </summary>
    public const string EnvironmentVariable = "ETKINLIK_SETUP_KEY";

    /// <summary>How long a key issued by the script is valid (the script sets the expiry).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Fixed pause after a refused key. With 128 bit keys guessing is hopeless anyway; there is no
    /// attempt counter, so wrong keys can never lock or revoke the founder's key.
    /// </summary>
    public static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Upper case without spaces and dashes; the form (32 hexadecimal characters) is checked after
    /// normalization, so separators never count towards the length.
    /// </summary>
    public static bool TryNormalize(string? key, out string normalized)
    {
        normalized = key is null ? string.Empty : Normalize(key);
        if (normalized.Length != KeyLength)
        {
            return false;
        }

        foreach (var c in normalized)
        {
            if (!char.IsAsciiHexDigitUpper(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>SHA-256 of the normalized key's ASCII text, as the issuing script computes it with HASHBYTES.</summary>
    public static byte[] Hash(string normalizedKey)
    {
        ArgumentNullException.ThrowIfNull(normalizedKey);
        return SHA256.HashData(Encoding.ASCII.GetBytes(normalizedKey));
    }

    /// <summary>The key in groups of four, as the script shows it.</summary>
    public static string Format(string normalizedKey)
    {
        ArgumentNullException.ThrowIfNull(normalizedKey);

        var builder = new StringBuilder(normalizedKey.Length + (normalizedKey.Length / 4));
        for (var i = 0; i < normalizedKey.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
            {
                builder.Append('-');
            }

            builder.Append(normalizedKey[i]);
        }

        return builder.ToString();
    }

    private static string Normalize(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            if (c != '-' && !char.IsWhiteSpace(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// The value of <see cref="SetupKeyPolicy.EnvironmentVariable"/>, read once. It only fills in the form.
/// A set value must have the form of a setup key after normalization; otherwise it is a configuration
/// error and nothing is filled in.
/// </summary>
public sealed class SetupKeyEnvironment
{
    public SetupKeyEnvironment(string? value)
    {
        IsSet = !string.IsNullOrEmpty(value);
        if (IsSet && SetupKeyPolicy.TryNormalize(value, out var key))
        {
            Key = SetupKeyPolicy.Format(key);
        }
    }

    /// <summary>Whether the variable has a value.</summary>
    public bool IsSet { get; }

    /// <summary>Whether the value has the form of a setup key.</summary>
    public bool IsValid => Key is not null;

    /// <summary>The key to fill in, or <see langword="null"/> when the variable is not set or invalid.</summary>
    public string? Key { get; }

    public static SetupKeyEnvironment FromProcess() =>
        new(Environment.GetEnvironmentVariable(SetupKeyPolicy.EnvironmentVariable));
}
