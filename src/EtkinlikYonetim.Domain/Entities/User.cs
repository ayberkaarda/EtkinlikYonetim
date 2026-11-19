namespace EtkinlikYonetim.Domain.Entities;

/// <summary>An account that can sign in (table: Kullanicilar).</summary>
public sealed class User
{
    public int Id { get; set; }

    /// <summary>Sign-in name; unique.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2 hash of the password. Rows created by the original application hold the plain text
    /// password until the first successful sign-in replaces it with a hash; <see cref="PasswordFormat"/>
    /// tells which of the two it is.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>How <see cref="PasswordHash"/> is stored.</summary>
    public PasswordFormat PasswordFormat { get; set; }

    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>Failed sign-in attempts since the last success or lockout.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>UTC time until which sign-in is refused, or <see langword="null"/>.</summary>
    public DateTime? LockoutEnd { get; set; }

    /// <summary>
    /// Set by an admin to stop the account from signing in. Every operation of an open session checks
    /// it in the database, so a disabled account's session can no longer do anything.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Raised by one on every change of the account's access (disabled, enabled, role changed). A session
    /// carries the value it was issued with, and every operation compares it with this column, so a session
    /// opened before such a change never works again, even after the account is enabled or promoted back.
    /// </summary>
    public int SessionVersion { get; set; }
}

/// <summary>What an account may do (column Rol, stored as "Kullanici" or "Admin").</summary>
public enum UserRole
{
    /// <summary>Adds own participants, registers them to events and sees only their own registrations.</summary>
    User = 0,

    /// <summary>Creates events and sees every participant and registration.</summary>
    Admin = 1,
}

/// <summary>
/// How an account's stored password value has to be checked (column SifreBicimi). It is recorded
/// explicitly instead of being read from the value, because the original application let users choose
/// any text as a password, including text that looks like a hash.
/// </summary>
public enum PasswordFormat
{
    /// <summary>A PBKDF2 hash written by this application. Also the default for new accounts.</summary>
    Pbkdf2 = 0,

    /// <summary>
    /// The plain text password kept from a database of the original application. Replaced, together
    /// with the format, by a hash on the account's next successful sign-in.
    /// </summary>
    LegacyPlainText = 1,
}
