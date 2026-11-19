using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EtkinlikYonetim.Business.Security;

/// <summary>Outcome of <see cref="ISetupKeyStore.TryConsume"/>.</summary>
public enum SetupKeyCheck
{
    /// <summary>The key was right and is used up now.</summary>
    Consumed = 0,

    /// <summary>No key, a wrong key or a key that was already used; the failure was counted.</summary>
    Wrong = 1,

    /// <summary>Too many wrong keys: no key is checked until the lockout ends.</summary>
    LockedOut = 2,
}

/// <summary>
/// The founder's authority: a local, single use key. Whoever can read it on the computer that runs the
/// application may create the first admin of a database that has accounts but no admin. Having no admin
/// in the database is not such an authority.
/// </summary>
public interface ISetupKeyStore
{
    /// <summary>
    /// File that holds the key, shown to the installer, or <see langword="null"/> when the key comes
    /// from the <see cref="FileSetupKeyStore.EnvironmentVariable"/> environment variable.
    /// </summary>
    string? KeyFilePath { get; }

    /// <summary>Writes a new random key to <see cref="KeyFilePath"/> unless an unused key exists.</summary>
    void EnsureIssued();

    /// <summary>
    /// Checks <paramref name="candidate"/> and, when it is the current key, uses it up in the same
    /// step: of any number of calls with the right key, also concurrent ones, exactly one returns
    /// <see cref="SetupKeyCheck.Consumed"/>. Wrong keys are counted; after
    /// <see cref="SetupKeyPolicy.MaxFailedAttempts"/> of them no key is accepted for
    /// <see cref="SetupKeyPolicy.LockoutDuration"/>.
    /// </summary>
    SetupKeyCheck TryConsume(string? candidate);
}

/// <summary>Brute force protection of the setup key.</summary>
public static class SetupKeyPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Keeps the setup key in a file of the application's local data folder (readable by the user who
/// runs the application), or takes it from <see cref="EnvironmentVariable"/>. A used file key is
/// deleted; a used environment key is recorded by its hash, so it stays invalid until another value is
/// set. The failed attempts are kept in the same folder, so restarting the application does not reset
/// them. The key itself is never logged and never part of an exception message.
/// </summary>
public sealed class FileSetupKeyStore : ISetupKeyStore
{
    /// <summary>Environment variable with a setup key chosen by the operator (at least <see cref="MinimumEnvironmentKeyLength"/> characters).</summary>
    public const string EnvironmentVariable = "ETKINLIK_SETUP_KEY";

    public const int MinimumEnvironmentKeyLength = 16;

    internal const string KeyFileName = "setup-key.txt";
    internal const string AttemptsFileName = "setup-key-attempts.txt";

    // Crockford base32 without the easily confused I, L, O and U: 20 characters carry 100 bits.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int KeyLength = 20;

    private readonly string _directory;
    private readonly string? _environmentKey;
    private readonly TimeProvider _timeProvider;

    public FileSetupKeyStore(string directory, string? environmentKey, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _directory = directory;
        _timeProvider = timeProvider;
        var normalized = environmentKey is null ? string.Empty : Normalize(environmentKey);
        _environmentKey = normalized.Length >= MinimumEnvironmentKeyLength ? normalized : null;
        KeyFilePath = _environmentKey is null ? Path.Combine(directory, KeyFileName) : null;
    }

    public string? KeyFilePath { get; }

    /// <summary>The key folder is %LOCALAPPDATA%\EtkinlikYonetim; the environment variable takes precedence.</summary>
    public static FileSetupKeyStore CreateDefault(TimeProvider timeProvider) =>
        new(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EtkinlikYonetim"),
            Environment.GetEnvironmentVariable(EnvironmentVariable),
            timeProvider);

    public void EnsureIssued()
    {
        if (KeyFilePath is null)
        {
            return;
        }

        Directory.CreateDirectory(_directory);
        try
        {
            // OpenOrCreate under an exclusive lock: an unused key, also one written concurrently, is
            // kept; an emptied (used) file gets a new key.
            using var stream = new FileStream(KeyFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (ReadAll(stream).Length > 0)
            {
                return;
            }

            stream.SetLength(0);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(NewCode(KeyLength));
        }
        catch (IOException) when (File.Exists(KeyFilePath))
        {
            // Being written or consumed by another instance right now.
        }
    }

    public SetupKeyCheck TryConsume(string? candidate)
    {
        Directory.CreateDirectory(_directory);

        // The attempts file is held exclusively for the whole check, so concurrent checks, also from
        // other application instances, are serialized: no failure is lost and no key is used twice.
        using var attempts = OpenExclusive(Path.Combine(_directory, AttemptsFileName), FileMode.OpenOrCreate);
        if (attempts is null)
        {
            return SetupKeyCheck.Wrong;
        }

        var now = _timeProvider.GetUtcNow();
        var (failures, lockoutEnd) = ReadAttempts(attempts);
        if (lockoutEnd > now)
        {
            return SetupKeyCheck.LockedOut;
        }

        var given = candidate is null ? string.Empty : Normalize(candidate);
        var consumed = given.Length > 0
            && (_environmentKey is not null ? TryConsumeEnvironmentKey(given) : TryConsumeFileKey(given));
        if (consumed)
        {
            WriteAttempts(attempts, 0, null);
            return SetupKeyCheck.Consumed;
        }

        failures++;
        if (failures >= SetupKeyPolicy.MaxFailedAttempts)
        {
            WriteAttempts(attempts, 0, now + SetupKeyPolicy.LockoutDuration);
            return SetupKeyCheck.LockedOut;
        }

        WriteAttempts(attempts, failures, null);
        return SetupKeyCheck.Wrong;
    }

    /// <summary>Upper case, without spaces and dashes, so the key can be typed as it is shown.</summary>
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

    /// <summary>A random code of <paramref name="length"/> base32 characters in groups of five.</summary>
    internal static string NewCode(int length)
    {
        var builder = new StringBuilder(length + (length / 5));
        for (var i = 0; i < length; i++)
        {
            if (i > 0 && i % 5 == 0)
            {
                builder.Append('-');
            }

            builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>Compares the hashes, so the time taken depends on neither the content nor the length.</summary>
    private static bool SameKey(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));

    private static string ReadAll(FileStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return Normalize(reader.ReadToEnd());
    }

    /// <summary>Opens <paramref name="path"/> without sharing, waiting a moment while another instance holds it.</summary>
    private static FileStream? OpenExclusive(string path, FileMode mode)
    {
        const int Attempts = 100;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
            {
                return null;
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(20);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    /// <summary>Failures and lockout end as "count;unix-milliseconds"; an unreadable file counts as no failures.</summary>
    private static (int Failures, DateTimeOffset? LockoutEnd) ReadAttempts(FileStream stream)
    {
        var parts = ReadAll(stream).Split(';');
        var failures = parts.Length > 0 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;
        DateTimeOffset? lockoutEnd = parts.Length > 1 && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
        return (failures, lockoutEnd);
    }

    private static void WriteAttempts(FileStream stream, int failures, DateTimeOffset? lockoutEnd)
    {
        var text = failures.ToString(CultureInfo.InvariantCulture)
            + (lockoutEnd is { } end ? ";" + end.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : string.Empty);
        stream.SetLength(0);
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An emptied key file is no key; EnsureIssued replaces it.
        }
    }

    /// <summary>Reads and empties the key file; the emptied file is then deleted.</summary>
    private bool TryConsumeFileKey(string given)
    {
        using (var stream = OpenExclusive(KeyFilePath!, FileMode.Open))
        {
            if (stream is null)
            {
                return false;
            }

            var stored = ReadAll(stream);
            if (stored.Length == 0 || !SameKey(stored, given))
            {
                return false;
            }

            stream.SetLength(0);
            stream.Flush(flushToDisk: true);
        }

        TryDelete(KeyFilePath!);
        return true;
    }

    private bool TryConsumeEnvironmentKey(string given)
    {
        if (!SameKey(_environmentKey!, given))
        {
            return false;
        }

        // The marker is named by the key's hash; creating it fails when it exists, so a value of the
        // environment variable can be used once.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_environmentKey!)));
        var marker = Path.Combine(_directory, "setup-key-used-" + hash);
        try
        {
            using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
