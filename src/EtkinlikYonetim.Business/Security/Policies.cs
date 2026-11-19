namespace EtkinlikYonetim.Business.Security;

/// <summary>Brute force protection: an account is locked for a while after repeated failures.</summary>
public static class LoginPolicy
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}

/// <summary>Password length rules.</summary>
public static class PasswordPolicy
{
    /// <summary>Shortest password that can be chosen at registration.</summary>
    public const int MinLength = 8;

    /// <summary>Longest password that can be chosen at registration.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// Longest password accepted at sign-in. The original application stored passwords of any length
    /// up to its NVARCHAR(100) column, so existing accounts may have passwords longer than
    /// <see cref="MaxLength"/>; sign-in must keep accepting them, before and after they are rehashed.
    /// </summary>
    public const int SignInMaxLength = 100;
}
