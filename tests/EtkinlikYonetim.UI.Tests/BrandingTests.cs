using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>
/// The screens use the neutral product name and the application's own artwork; nothing of the
/// third-party ticketing brand the original application showed is left. The input fields have the
/// limits of the services.
/// </summary>
public sealed class BrandingTests
{
    private static readonly string[] ForbiddenTexts = ["BuBilet", "Bubilet"];

    [Fact]
    public void Forms_UseTheNeutralTitleAndOwnIcon() => Run(() =>
    {
        var auth = new PendingAuthService();
        var events = new ControlledEventService();
        var participants = new ControlledParticipantService();
        var registrations = new ControlledRegistrationService();
        using var signIn = new GirisYapForm(auth, new NoFormsFactory());
        using var signUp = new KayitOlForm(auth);
        using var panel = new Form1(events, new NoFormsFactory(), Sessions.Admin);
        using var addParticipant = new KatilimciEkleForm(participants, new NoFormsFactory(), Sessions.User);
        using var register = new KayitEkleForm(events, registrations, Sessions.User);
        using var listing = new KatilimciGoruntule(events, participants, new ControlledExportService(), Sessions.Admin);
        using var setup = new IlkKurulumForm(new PendingSetupService());

        foreach (Form form in new Form[] { signIn, signUp, panel, addParticipant, register, listing, setup })
        {
            Assert.StartsWith(AppText.Title + " - ", form.Text, StringComparison.Ordinal);
            Assert.Same(AppAssets.Icon, form.Icon);
            foreach (var text in AllTexts(form))
            {
                Assert.All(ForbiddenTexts, forbidden => Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase));
            }
        }

        Assert.Null(signIn.BackgroundImage);
        Assert.Null(signUp.BackgroundImage);
        Assert.Null(panel.BackgroundImage);
    });

    [Fact]
    public void EmbeddedResources_AreOnlyTheApplicationsOwnArtwork()
    {
        var images = typeof(AppAssets).Assembly.GetManifestResourceNames()
            .Where(n => !n.EndsWith(".resources", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ["EtkinlikYonetim.UI.Assets.app.ico", "EtkinlikYonetim.UI.Assets.logo-256.png"],
            images);
        Assert.DoesNotContain(typeof(AppAssets).Assembly.GetManifestResourceNames(), n => n.EndsWith(".resources", StringComparison.Ordinal));
        Assert.Equal(new Size(256, 256), AppAssets.Logo.Size);
    }

    [Fact]
    public void Fields_FollowTheServiceLimits() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var signIn = new GirisYapForm(auth, new NoFormsFactory());
        using var signUp = new KayitOlForm(auth);
        using var addParticipant = new KatilimciEkleForm(new ControlledParticipantService(), new NoFormsFactory(), Sessions.User);

        // Sign-in accepts every password an existing account can have, sign-up only new ones.
        var longest = new string('x', PasswordPolicy.SignInMaxLength);
        Find<TextBox>(signIn, "txtKullaniciAdi").Text = "ornek";
        Find<TextBox>(signIn, "txtSifre").Text = longest;
        _ = signIn.SignInAsync();
        Assert.Equal(longest, auth.LastSignInPassword);
        Assert.Equal(PasswordPolicy.SignInMaxLength, Find<TextBox>(signIn, "txtSifre").MaxLength);
        Assert.Equal(PasswordPolicy.MaxLength, Find<TextBox>(signUp, "txtSifre").MaxLength);
        Assert.Equal(InputRules.UserNameMaxLength, Find<TextBox>(signUp, "txtKullaniciAdi").MaxLength);
        using var setup = new IlkKurulumForm(new PendingSetupService());
        Assert.Equal(InputRules.UserNameMaxLength, Find<TextBox>(setup, "txtKullaniciAdi").MaxLength);
        Assert.Equal(PasswordPolicy.MaxLength, Find<TextBox>(setup, "txtSifre").MaxLength);
        Assert.Equal(PasswordPolicy.MaxLength, Find<TextBox>(setup, "txtSifreTekrar").MaxLength);
        Assert.Equal(NationalIdNumber.Length, Find<TextBox>(addParticipant, "txtTC").MaxLength);
        Assert.Equal(InputRules.PersonNameMaxLength, Find<TextBox>(addParticipant, "txtAdSoyad").MaxLength);
    });

    private static IEnumerable<string> AllTexts(Control root)
    {
        yield return root.Text;
        foreach (Control child in root.Controls)
        {
            foreach (var text in AllTexts(child))
            {
                yield return text;
            }
        }
    }
}
