using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>Every write button is exclusive: a second click while the first call is pending is ignored.</summary>
public sealed class SaveGuardTests
{
    [Fact]
    public void SignIn_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new GirisYapForm(auth, new NoFormsFactory()) { OfferAdminSetup = true };
        Find<TextBox>(form, "txtKullaniciAdi").Text = "ornek";
        Find<TextBox>(form, "txtSifre").Text = "Demo.Kullanici1";

        var first = form.SignInAsync();
        var second = form.SignInAsync();

        Assert.Equal(1, auth.SignInCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Null(second.Result);
        Assert.False(Find<Control>(form, "btnGiris").Enabled);
        Assert.False(Find<Button>(form, "btnKayitOl").Enabled);
        Assert.True(form.OfferAdminSetup);
        Assert.False(Find<Button>(form, "btnKurulum").Enabled);
    });

    [Fact]
    public void SignUp_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new KayitOlForm(auth) { Prompts = new RecordingPrompts() };
        Find<TextBox>(form, "txtKullaniciAdi").Text = "yeniuye";
        Find<TextBox>(form, "txtSifre").Text = "Gizli.Sifre1";
        Find<TextBox>(form, "txtSifreTekrar").Text = "Gizli.Sifre1";

        var first = form.SaveAsync();
        var second = form.SaveAsync();

        Assert.Equal(1, auth.RegisterCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(Find<Button>(form, "btnKayitOl").Enabled);
    });

    [Fact]
    public void FirstRunSetup_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var setup = new PendingSetupService();
        using var form = new IlkKurulumForm(setup) { Prompts = new RecordingPrompts() };
        Find<TextBox>(form, "txtKullaniciAdi").Text = "kurucu";
        Find<TextBox>(form, "txtSifre").Text = "Gizli.Sifre1";
        Find<TextBox>(form, "txtSifreTekrar").Text = "Gizli.Sifre1";

        var first = form.CreateAsync();
        var second = form.CreateAsync();

        Assert.Equal(1, setup.CreateCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Null(second.Result);
        Assert.False(Find<Button>(form, "btnOlustur").Enabled);
        Assert.False(Find<Button>(form, "btnIptal").Enabled);
    });

    /// <summary>An empty database sends no key; a database without admins asks for it and tells how the founder issues one.</summary>
    [Fact]
    public void FirstRunSetup_SetupKeyMode_SendsTheTypedKeyOnly() => Run(() =>
    {
        var setup = new PendingSetupService();
        using var form = new IlkKurulumForm(setup) { Prompts = new RecordingPrompts() };
        Assert.True(FormLayout.IsHidden(Find<TextBox>(form, "txtAnahtar")));
        Find<TextBox>(form, "txtAnahtar").Text = "ABCDE-FGHJK";
        Find<TextBox>(form, "txtKullaniciAdi").Text = "kurucu";
        _ = form.CreateAsync();
        Assert.Null(setup.LastSetupKey);

        using var keyed = new IlkKurulumForm(setup) { Prompts = new RecordingPrompts(), RequireSetupKey = true };
        Find<TextBox>(keyed, "txtAnahtar").Text = "ABCDE-FGHJK";
        Find<TextBox>(keyed, "txtKullaniciAdi").Text = "kurucu";
        _ = keyed.CreateAsync();

        Assert.Equal("ABCDE-FGHJK", setup.LastSetupKey);
        Assert.Contains("issue-setup-key.sql", Find<Label>(keyed, "lblAciklama").Text, StringComparison.Ordinal);
        Assert.DoesNotContain("--create-setup-key", Find<Label>(keyed, "lblAciklama").Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDE", Find<Label>(keyed, "lblAciklama").Text, StringComparison.Ordinal);
        Assert.False(FormLayout.IsHidden(Find<TextBox>(keyed, "txtAnahtar")));
        Assert.True(Find<TextBox>(keyed, "txtAnahtar").UseSystemPasswordChar);
    });

    /// <summary>ETKINLIK_SETUP_KEY only fills in the key field; a malformed value fills in nothing and becomes a configuration warning.</summary>
    [Fact]
    public void FirstRunSetup_EnvironmentKey_OnlyPrefillsAWellFormedKey() => Run(() =>
    {
        var setup = new PendingSetupService { Prefill = new("0123-4567-89AB-CDEF-0123-4567-89AB-CDEF", false) };
        using var prefilled = new IlkKurulumForm(setup) { Prompts = new RecordingPrompts(), RequireSetupKey = true };
        Assert.Equal("0123-4567-89AB-CDEF-0123-4567-89AB-CDEF", Find<TextBox>(prefilled, "txtAnahtar").Text);
        Assert.Null(prefilled.EnvironmentKeyWarning);

        setup.Prefill = new(null, true);
        using var invalid = new IlkKurulumForm(setup) { Prompts = new RecordingPrompts(), RequireSetupKey = true };
        Assert.Equal(string.Empty, Find<TextBox>(invalid, "txtAnahtar").Text);
        Assert.Equal(IlkKurulumForm.EnvironmentKeyInvalid, invalid.EnvironmentKeyWarning);

        // An empty database asks for no key, so the environment is not even read.
        using var empty = new IlkKurulumForm(setup) { Prompts = new RecordingPrompts() };
        Assert.Null(empty.EnvironmentKeyWarning);
        Assert.Equal(string.Empty, Find<TextBox>(empty, "txtAnahtar").Text);
    });

    [Fact]
    public void CreateEvent_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var events = new ControlledEventService();
        using var form = new Form1(events, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Find<TextBox>(form, "txtEtkinlikAd").Text = "Konser";

        var first = form.CreateEventAsync();
        var second = form.CreateEventAsync();

        Assert.Equal(1, events.CreateCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(Find<Button>(form, "btnEtkinlikEkle").Enabled);
    });

    [Fact]
    public void AddParticipant_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var participants = new ControlledParticipantService();
        using var form = new KatilimciEkleForm(participants, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        Find<TextBox>(form, "txtTC").Text = "10000000146";

        var first = form.SaveAsync();
        var second = form.SaveAsync();

        Assert.Equal(1, participants.AddCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(Find<Button>(form, "btnKatilimciEkle").Enabled);
    });

    [Fact]
    public void Register_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User) { Prompts = new RecordingPrompts() };
        Find<TextBox>(form, "txtKatilimciTC").Text = "10000000146";

        var first = form.RegisterAsync();
        var second = form.RegisterAsync();

        Assert.Equal(1, registrations.RegisterCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(Find<Button>(form, "btnKayit").Enabled);
    });

    [Fact]
    public void CancelRegistration_WhilePending_IsNotSentTwice() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        var prompts = new RecordingPrompts();
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User) { Prompts = prompts };
        var list = Find<ListBox>(form, "lstKayitlar");
        list.Items.Add(new RegistrationItem(Sessions.Registration(5)));
        list.SelectedIndex = 0;

        var first = form.CancelAsync();
        var second = form.CancelAsync();

        Assert.Equal(1, registrations.CancelCalls);
        Assert.Single(prompts.Messages); // one confirmation only
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(Find<Button>(form, "btnKayitIptal").Enabled);
    });

    [Fact]
    public void CancelRegistration_WithoutASelection_AsksForOneAndCallsNothing() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        var prompts = new RecordingPrompts();
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User) { Prompts = prompts };

        var run = form.CancelAsync();

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(0, registrations.CancelCalls);
        Assert.Equal(["Lütfen iptal etmek istediğiniz kaydı seçin."], prompts.Messages);
    });
}
