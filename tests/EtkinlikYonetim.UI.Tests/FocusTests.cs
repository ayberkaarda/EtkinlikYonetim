using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>
/// The focus is moved only after the guarded controls are enabled again; a disabled control cannot
/// take it. Forms get window handles but are never shown.
/// </summary>
public sealed class FocusTests
{
    [Fact]
    public void FocusAfterUnlock_SelectsTheTargetOnlyOnceItIsEnabledAgain() => Run(() =>
    {
        using var form = new Form();
        using var button = new Button();
        using var field = new TextBox();
        form.Controls.AddRange([button, field]);
        CreateHandles(form);
        var operation = new ExclusiveOperation();
        var pending = new TaskCompletionSource();

        var run = operation.RunAsync(
            async () =>
            {
                await pending.Task;
                Assert.False(field.Enabled);
                operation.FocusAfterUnlock(field);
                Assert.NotSame(field, form.ActiveControl);
            },
            button,
            field);
        pending.SetResult();

        Assert.True(run.IsCompletedSuccessfully);
        Assert.True(field.Enabled);
        Assert.Same(field, form.ActiveControl);
    });

    [Fact]
    public void FocusAfterUnlock_TargetOfAnEarlierRun_IsNotReused() => Run(() =>
    {
        using var form = new Form();
        using var first = new TextBox();
        using var second = new TextBox();
        form.Controls.AddRange([first, second]);
        CreateHandles(form);
        var operation = new ExclusiveOperation();

        _ = operation.RunAsync(() =>
        {
            operation.FocusAfterUnlock(first);
            return Task.CompletedTask;
        });
        second.Select();
        _ = operation.RunAsync(() => Task.CompletedTask);

        Assert.Same(second, form.ActiveControl);
    });

    [Fact]
    public void RefusedSignIn_ClearsAndSelectsThePasswordAfterUnlock() => Run(() =>
    {
        var auth = new PendingAuthService();
        using var form = new GirisYapForm(auth, new NoFormsFactory());
        CreateHandles(form);
        var password = Find<TextBox>(form, "txtSifre");
        Find<TextBox>(form, "txtKullaniciAdi").Text = "ornek";
        password.Text = "yanlis";
        Find<Button>(form, "btnKayitOl").Select();

        var signIn = form.SignInAsync();
        auth.RefuseSignIn();

        Assert.True(signIn.IsCompletedSuccessfully);
        Assert.Null(signIn.Result);
        Assert.True(Find<Control>(form, "btnGiris").Enabled);
        Assert.Empty(password.Text);
        Assert.Same(password, form.ActiveControl);
    });

    [Fact]
    public void SuccessfulSignUp_SelectsTheUserNameAfterUnlock() => Run(() =>
    {
        var auth = new PendingAuthService();
        var prompts = new RecordingPrompts();
        using var form = new KayitOlForm(auth) { Prompts = prompts };
        CreateHandles(form);
        var name = Find<TextBox>(form, "txtKullaniciAdi");
        name.Text = "yeniuye";
        Find<TextBox>(form, "txtSifre").Text = "Gizli.Sifre1";
        Find<TextBox>(form, "txtSifreTekrar").Text = "Gizli.Sifre1";
        Find<Button>(form, "btnKayitOl").Select();

        var save = form.SaveAsync();
        auth.CompleteRegister();

        Assert.True(save.IsCompletedSuccessfully && save.Result);
        Assert.Empty(name.Text);
        Assert.Same(name, form.ActiveControl);
        Assert.Equal(["Kullanıcı başarıyla eklendi."], prompts.Messages);
    });

    [Fact]
    public void AddedParticipant_SelectsTheNationalIdAfterUnlock() => Run(() =>
    {
        var participants = new ControlledParticipantService();
        using var form = new KatilimciEkleForm(participants, new NoFormsFactory(), Sessions.User) { Prompts = new RecordingPrompts() };
        CreateHandles(form);
        var nationalId = Find<TextBox>(form, "txtTC");
        nationalId.Text = "10000000146";
        Find<Button>(form, "btnKatilimciEkle").Select();

        var save = form.SaveAsync();
        participants.CompleteAdd();

        Assert.True(save.IsCompletedSuccessfully && save.Result);
        Assert.Same(nationalId, form.ActiveControl);
    });

    [Fact]
    public void CreatedEvent_SelectsTheNameAfterUnlock() => Run(() =>
    {
        var events = new ControlledEventService();
        using var form = new Form1(events, new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        CreateHandles(form);
        var name = Find<TextBox>(form, "txtEtkinlikAd");
        name.Text = "Konser";
        Find<Button>(form, "btnEtkinlikEkle").Select();

        var save = form.CreateEventAsync();
        events.CompleteCreate();

        Assert.True(save.IsCompletedSuccessfully && save.Result);
        Assert.Same(name, form.ActiveControl);
    });

    [Fact]
    public void Registration_ShowsTheTicketAndSelectsTheNationalIdAfterUnlock() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        var prompts = new RecordingPrompts();
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User) { Prompts = prompts };
        CreateHandles(form);
        var nationalId = Find<TextBox>(form, "txtKatilimciTC");
        nationalId.Text = "10000000146";
        Find<Button>(form, "btnKayit").Select();

        var save = form.RegisterAsync();
        registrations.CompleteRegister();

        Assert.True(save.IsCompletedSuccessfully && save.Result);
        Assert.Equal(["Kayıt tamamlandı. Bilet kodu: ETK-ABCDEFGHJK"], prompts.Messages);
        Assert.Same(nationalId, form.ActiveControl);
    });

    [Fact]
    public void Registration_OnAFullEvent_ShowsTheWaitingPlace() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        var prompts = new RecordingPrompts();
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User) { Prompts = prompts };
        CreateHandles(form);
        Find<TextBox>(form, "txtKatilimciTC").Text = "10000000146";

        var save = form.RegisterAsync();
        registrations.CompleteRegisterOnTheWaitingList(3);

        Assert.True(save.IsCompletedSuccessfully && save.Result);
        var message = Assert.Single(prompts.Messages);
        Assert.StartsWith("Etkinlik dolu. Katılımcı bekleme listesine alındı (sıra: 3).", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Bilet kodu", message, StringComparison.Ordinal);
    });

    private static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateHandles(child);
        }
    }
}
