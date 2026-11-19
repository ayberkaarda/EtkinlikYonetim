using EtkinlikYonetim.Business.Validation;

namespace EtkinlikYonetim.Business.Tests;

public sealed class NationalIdNumberTests
{
    // Synthetic numbers that satisfy the checksum; they do not belong to anyone.
    [Theory]
    [InlineData("10000000146")]
    [InlineData("10000000214")]
    [InlineData("10000000382")]
    [InlineData("10000000450")]
    public void IsValid_AcceptsNumbersWithCorrectCheckDigits(string value)
    {
        Assert.True(NationalIdNumber.IsValid(value));
    }

    [Theory]
    [InlineData("10000000156", "wrong tenth digit")]
    [InlineData("10000000147", "wrong eleventh digit")]
    [InlineData("00000000000", "starts with zero")]
    [InlineData("01000000146", "starts with zero")]
    [InlineData("1000000014", "ten digits")]
    [InlineData("100000001460", "twelve digits")]
    [InlineData("1000000014a", "letter")]
    [InlineData(" 0000000146", "space")]
    [InlineData("１0000000146", "full width digit")]
    [InlineData("", "empty")]
    [InlineData(null, "null")]
    public void IsValid_RejectsInvalidNumbers(string? value, string reason)
    {
        Assert.False(NationalIdNumber.IsValid(value), reason);
    }

    [Fact]
    public void IsValid_ChecksBothCheckDigitsForEveryPrefix()
    {
        // Exactly one of the hundred possible endings is valid for any nine digit prefix.
        var valid = Enumerable.Range(0, 100)
            .Select(ending => "123456789" + ending.ToString("00", System.Globalization.CultureInfo.InvariantCulture))
            .Where(NationalIdNumber.IsValid)
            .ToList();

        Assert.Equal(["12345678950"], valid);
    }

    [Theory]
    [InlineData("10000000146", "100******46")]
    [InlineData("12345", "*****")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Mask_HidesTheMiddleDigits(string? value, string expected)
    {
        Assert.Equal(expected, NationalIdNumber.Mask(value));
    }
}
