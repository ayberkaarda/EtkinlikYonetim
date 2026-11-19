namespace EtkinlikYonetim.Business.Validation;

/// <summary>
/// Turkish national identity number (T.C. Kimlik No) rules. The number is personal data: it is shown
/// masked and never written to a log.
/// </summary>
public static class NationalIdNumber
{
    public const int Length = 11;

    /// <summary>Whether <paramref name="value"/> is exactly 11 ASCII digits.</summary>
    public static bool HasValidFormat(string? value) =>
        value is { Length: Length } && value.All(char.IsAsciiDigit);

    /// <summary>
    /// Checks the format and the checksum: the first digit is not zero, the tenth digit is
    /// ((d1 + d3 + d5 + d7 + d9) * 7 - (d2 + d4 + d6 + d8)) mod 10 and the eleventh digit is the sum
    /// of the first ten digits mod 10.
    /// </summary>
    public static bool IsValid(string? value)
    {
        if (!HasValidFormat(value) || value![0] == '0')
        {
            return false;
        }

        Span<int> d = stackalloc int[Length];
        for (var i = 0; i < Length; i++)
        {
            d[i] = value[i] - '0';
        }

        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = (((odd * 7) - even) % 10 + 10) % 10;
        if (d[9] != tenth)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            sum += d[i];
        }

        return d[10] == sum % 10;
    }

    /// <summary>
    /// Masks all but the first three and last two digits, for example "123******01". Values that are not
    /// 11 characters long are masked completely.
    /// </summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length != Length)
        {
            return new string('*', value.Length);
        }

        return string.Concat(value.AsSpan(0, 3), new string('*', Length - 5), value.AsSpan(Length - 2));
    }
}
