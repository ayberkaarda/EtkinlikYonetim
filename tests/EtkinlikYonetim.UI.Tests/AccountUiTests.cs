using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>The account management form and the end of a session the database no longer accepts.</summary>
public sealed class AccountUiTests
{
    private static readonly string[] ChangeButtons = ["btnEtkinlestir", "btnDevreDisi", "btnYoneticiYap", "btnKullaniciYap"];

    [Fact]
    public void AccountChange_WhilePending_IsNotSentTwiceAndLocksEveryChangeButton() => Run(() =>
    {
        var accounts = new ControlledAccountService();
        var prompts = new RecordingPrompts();
        using var form = new KullaniciYonetForm(accounts, Sessions.Admin) { Prompts = prompts };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ListBox>(form, "lstHesaplar").SelectedIndex = 1;

        var first = form.SetEnabledAsync(false);
        var second = form.ChangeRoleAsync(UserRole.Admin);

        Assert.Equal(1, accounts.ChangeCalls);
        Assert.Equal([(2, "disable")], accounts.Changes);
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.All(
            ChangeButtons,
            name => Assert.False(Find<Button>(form, name).Enabled));
    });

    [Fact]
    public void AccountChange_WithoutASelection_AsksForOneAndCallsNothing() => Run(() =>
    {
        var accounts = new ControlledAccountService();
        var prompts = new RecordingPrompts();
        using var form = new KullaniciYonetForm(accounts, Sessions.Admin) { Prompts = prompts };

        Assert.True(form.ChangeRoleAsync(UserRole.User).IsCompletedSuccessfully);

        Assert.Equal(0, accounts.ChangeCalls);
        Assert.Equal(["Lütfen bir hesap seçin."], prompts.Messages);
    });

    [Fact]
    public void Accounts_ShowNameRoleAndState() => Run(() =>
    {
        var accounts = new ControlledAccountService();
        accounts.Accounts.Add(new AccountSummary(3, "pasif", UserRole.User, true));
        using var form = new KullaniciYonetForm(accounts, Sessions.Admin);

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Assert.Equal(
            ["admin - Yönetici - Etkin", "ornek - Kullanıcı - Etkin", "pasif - Kullanıcı - Devre dışı"],
            Find<ListBox>(form, "lstHesaplar").Items.Cast<object>().Select(i => i.ToString()));
        Assert.Equal(-1, Find<ListBox>(form, "lstHesaplar").SelectedIndex);
    });

    /// <summary>A refused session in a dialog of the admin panel ends the panel's session; no window is shown.</summary>
    [Fact]
    public void ExpiredSession_InADialog_EndsThePanelsSession() => Run(() =>
    {
        using var panel = new Form1(new ControlledEventService(), new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        using var dialog = new KullaniciYonetForm(new ControlledAccountService { Expired = true }, Sessions.Admin) { Owner = panel };

        Assert.True(dialog.LoadAsync().IsCompletedSuccessfully);

        Assert.True(panel.SignedOut);
        Assert.Contains(NoDialogs.Requested, r => r.EndsWith(Business.SessionExpiredException.DefaultMessage, StringComparison.Ordinal));
    });
}
