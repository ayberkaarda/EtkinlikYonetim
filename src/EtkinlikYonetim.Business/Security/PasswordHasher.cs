using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Business.Security;

/// <summary>Outcome of checking a password against its stored form.</summary>
public enum PasswordVerificationResult
{
    Failed = 0,
    Success = 1,

    /// <summary>The password is correct but stored in plain text or with fewer iterations; store a new hash.</summary>
    SuccessRehashNeeded = 2,
}

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Checks <paramref name="password"/> against the stored value, read as <paramref name="format"/>.
    /// A value is never reinterpreted in another format: a hash that does not verify fails.
    /// </summary>
    PasswordVerificationResult Verify(string password, string stored, PasswordFormat format);

    /// <summary>
    /// Spends about as much time as a real verification without checking anything, so a sign-in for an
    /// unknown account cannot be told apart from a wrong password by its duration.
    /// </summary>
    void SimulateVerification(string password);
}

/// <summary>
/// PBKDF2 (HMAC-SHA256) password hashes stored as
/// <c>PBKDF2-SHA256${iterations}${salt base64}${hash base64}</c>. Plain text passwords written by the
/// original application (<see cref="PasswordFormat.LegacyPlainText"/>) verify once and are then rehashed.
/// The format comes from the account, never from the shape of the stored value.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    /// <summary>OWASP recommendation for PBKDF2-HMAC-SHA256.</summary>
    public const int DefaultIterations = 600_000;

    internal const string Prefix = "PBKDF2-SHA256$";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MinimumIterations = 1_000;

    private readonly int _iterations;
    private readonly byte[] _dummySalt = RandomNumberGenerator.GetBytes(SaltSize);

    public PasswordHasher()
        : this(DefaultIterations)
    {
    }

    /// <summary>Lower iteration counts are meant for tests only.</summary>
    public PasswordHasher(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, MinimumIterations);
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, _iterations);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public PasswordVerificationResult Verify(string password, string stored, PasswordFormat format)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(stored);

        return format switch
        {
            PasswordFormat.Pbkdf2 => Verify(password, stored),
            PasswordFormat.LegacyPlainText => VerifyLegacyPlainText(password, stored),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown password format."),
        };
    }

    /// <summary>Checks <paramref name="password"/> against a stored PBKDF2 hash.</summary>
    public PasswordVerificationResult Verify(string password, string stored)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(stored);

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Not a hash of this application; costs the same as a real check.
            SimulateVerification(password);
            return PasswordVerificationResult.Failed;
        }

        var parts = stored[Prefix.Length..].Split('$');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations < MinimumIterations)
        {
            return PasswordVerificationResult.Failed;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expected = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }

        if (expected.Length != HashSize)
        {
            return PasswordVerificationResult.Failed;
        }

        var actual = Derive(password, salt, iterations);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < _iterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    public void SimulateVerification(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        _ = Derive(password, _dummySalt, _iterations);
    }

    /// <summary>
    /// The original application stored passwords as entered (trimmed) and compared them as is. The
    /// comparison is done in constant time and a hash is derived anyway, so the duration does not
    /// reveal that the account still has a plain text password.
    /// </summary>
    private PasswordVerificationResult VerifyLegacyPlainText(string password, string stored)
    {
        SimulateVerification(password);

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(password.Trim()),
            Encoding.UTF8.GetBytes(stored));
        return matches && stored.Length > 0
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Failed;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
}
