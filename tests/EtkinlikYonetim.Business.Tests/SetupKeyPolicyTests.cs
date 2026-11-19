using EtkinlikYonetim.Business.Security;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Setup keys: random, typed as shown, and checked for length only after normalization.</summary>
public sealed class SetupKeyPolicyTests
{
    [Fact]
    public void NewKey_IsGroupedBase32AndRandom()
    {
        var key = SetupKeyPolicy.NewKey();

        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){3}$", key);
        Assert.NotEqual(key, SetupKeyPolicy.NewKey());
        Assert.Equal(32, SetupKeyPolicy.Hash(key)!.Length);
    }

    [Fact]
    public void Hash_IgnoresCaseSpacesAndDashes()
    {
        var key = SetupKeyPolicy.NewKey();

        Assert.Equal(SetupKeyPolicy.Hash(key), SetupKeyPolicy.Hash(" " + key.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal) + "\t"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("----------------")]
    [InlineData("A---------------")]
    [InlineData("A                    -")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVW")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWXY")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVIU")]
    public void Hash_ValueThatIsNotAKeyAfterNormalization_IsRefused(string? value) =>
        Assert.Null(SetupKeyPolicy.Hash(value));
}
