using System.Text.RegularExpressions;

namespace EtkinlikYonetim.Business.Validation;

/// <summary>
/// Input rules that the original forms checked while typing (letters only, digits only, e-mail shape),
/// now enforced by the services so that no screen can skip them.
/// </summary>
public static partial class InputRules
{
    /// <summary>Longest user name; the sign-in and registration fields allow the same.</summary>
    public const int UserNameMaxLength = 32;

    /// <summary>Longest user name an existing account can have (column size of the original application).</summary>
    public const int StoredUserNameMaxLength = 50;

    public const int PersonNameMaxLength = 64;

    public const int EmailMaxLength = 100;

    public const int EventNameMaxLength = 80;

    public const int EventDescriptionMaxLength = 500;

    public const int MinCapacity = 1;

    public const int MaxCapacity = 1000;

    /// <summary>User names consist of letters only, as the original sign-up form allowed.</summary>
    public static bool IsValidUserName(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= UserNameMaxLength && value.All(char.IsLetter);

    /// <summary>
    /// A person's name: letters separated by single spaces. The original form accepted letters only,
    /// which made a name with a surname impossible to type; spaces between words are allowed now.
    /// </summary>
    public static bool IsValidPersonName(string? value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= PersonNameMaxLength
        && value.All(c => char.IsLetter(c) || c == ' ')
        && !value.StartsWith(' ')
        && !value.EndsWith(' ')
        && !value.Contains("  ", StringComparison.Ordinal);

    /// <summary>The e-mail shape the original form checked: something@something.something, no spaces.</summary>
    public static bool IsValidEmail(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= EmailMaxLength && EmailPattern().IsMatch(value);

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailPattern();
}
