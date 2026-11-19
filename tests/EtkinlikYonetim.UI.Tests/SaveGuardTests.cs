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
        using var form = new GirisYapForm(auth, new NoFormsFactory());
        Find<TextBox>(form, "txtKullaniciAdi").Text = "ornek";
        Find<TextBox>(form, "txtSifre").Text = "Demo.Kullanici1";

        var first = form.SignInAsync();
        var second = form.SignInAsync();

        Assert.Equal(1, auth.SignInCalls);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully);
        Assert.Null(second.Result);
        Assert.False(Find<Control>(form, "btnGiris2").Enabled);
        Assert.False(Find<Button>(form, "btnkayitol").Enabled);
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
