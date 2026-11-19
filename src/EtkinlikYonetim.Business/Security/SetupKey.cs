using System.Security.Cryptography;
using System.Text;

namespace EtkinlikYonetim.Business.Security;

/// <summary>
/// The founder's authority over a database that has accounts but no admin: a random, single use key
/// issued for that database by the <c>--create-setup-key</c> command and shown once. The database
/// keeps only its hash (<see cref="Domain.Entities.SetupKeyRecord"/>), so neither a file in a client's
/// profile nor a value a client chooses is an authority; a key typed into the setup or given through
/// <see cref="EnvironmentVariable"/> is checked against that record and used up there.
/// </summary>
public static class SetupKeyPolicy
{
    /// <summary>Environment variable that can supply the issued key to the setup form instead of typing it.</summary>
    public const string EnvironmentVariable = "ETKINLIK_SETUP_KEY";

    /// <summary>Characters of a key after normalization: 20 base32 characters carry 100 bits.</summary>
    public const int KeyLength = 20;

    /// <summary>Wrong keys after which the issued key is no longer accepted and a new one has to be created.</summary>
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    // Crockford base32 without the easily confused I, L, O and U.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>A new random key in groups of five, as it is shown to the operator.</summary>
    public static string NewKey()
    {
        var builder = new StringBuilder(KeyLength + (KeyLength / 5));
        for (var i = 0; i < KeyLength; i++)
        {
            if (i > 0 && i % 5 == 0)
            {
                builder.Append('-');
            }

            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The hash stored for <paramref name="key"/>, or <see langword="null"/> when the value is not a
    /// key at all. The length and the characters are checked after normalization (upper case, without
    /// spaces and dashes), so a value of separators can never pass as a short or empty key.
    /// </summary>
    public static byte[]? Hash(string? key)
    {
        if (key is null)
        {
            return null;
        }

        var normalized = Normalize(key);
        if (normalized.Length != KeyLength || !normalized.All(c => Alphabet.Contains(c, StringComparison.Ordinal)))
        {
            return null;
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
    }

    internal static string Normalize(string key)
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
