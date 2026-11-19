namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Column sizes and value rules enforced by the database. The business layer applies the same or
/// stricter rules; the legacy upgrade checks existing rows against these values.
/// </summary>
internal static class SchemaLimits
{
    /// <summary>Sizes of the original columns (NVARCHAR(50) and NVARCHAR(100)).</summary>
    public const int UserNameLength = 50;
    public const int EventNameLength = 100;
    public const int PersonNameLength = 100;
    public const int EmailLength = 100;
    public const int NationalIdLength = 11;

    /// <summary>Room for a PBKDF2 hash; the original column held plain text in 100 characters.</summary>
    public const int PasswordLength = 256;

    public const int RoleLength = 20;
    public const int TicketCodeLength = 20;

    /// <summary>Stored values of <see cref="Domain.Entities.UserRole"/>, as the original application wrote them.</summary>
    public const string UserRoleValue = "Kullanici";
    public const string AdminRoleValue = "Admin";

    // Plain literals: the same text has to work on SQLite, which has no N'' prefix.
    public const string RoleCheck = "[Rol] IN ('Kullanici', 'Admin')";
    public const string FailedLoginCheck = "[BasarisizGiris] >= 0";

    /// <summary>Length of a SHA-256 hash of a setup key.</summary>
    public const int SetupKeyHashLength = 32;

    public const string SetupKeyFailedAttemptsCheck = "[BasarisizDeneme] >= 0";

    /// <summary>Values of <see cref="Domain.Entities.PasswordFormat"/>: 0 PBKDF2 hash, 1 legacy plain text.</summary>
    public const string PasswordFormatCheck = "[SifreBicimi] IN (0, 1)";

    public const string CapacityCheck = "[Kapasite] > 0";

    /// <summary>The seat counter can neither go negative nor exceed the capacity: no event is ever overbooked.</summary>
    public const string RegisteredCountCheck = "[KayitSayisi] >= 0 AND [KayitSayisi] <= [Kapasite]";

    public const string StatusCheck = "[Durum] IN (0, 1)";

    /// <summary>Only active registrations take a seat and count as a registration; cancelled ones are history.</summary>
    public const string ActiveRegistrationFilter = "[Durum] = 0";
}
