using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>The entrance window and the ticket window. No window is shown.</summary>
public sealed class CheckInUiTests
{
    private const string Code = "ETK-ABCDEFGHJKLMNPQRSTUVWXYZ23";

    [Fact]
    public void Events_LeaveOutCancelledOnesAndSelectNothing() => Run(() =>
    {
        var events = new ControlledEventService();
        events.Events.Add(new EventSummary(3, "İptal", new DateOnly(2027, 6, 1), 10, 0, Cancelled: true));
        var checkIns = new ControlledCheckInService();
        using var form = new BiletKontrolForm(events, checkIns, Sessions.Admin) { Prompts = new RecordingPrompts() };

        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        var picker = Find<ComboBox>(form, "cmbEtkinlik");
        Assert.Equal([1, 2], picker.Items.Cast<EventSummary>().Select(e => e.Id));
        Assert.Equal(-1, picker.SelectedIndex);
        Assert.Empty(checkIns.CountRequests);
    });

    /// <summary>
    /// The code leaves the field at once and the field stays open: a scanner types the next code while the
    /// first check runs. Its Enter does not start a second check at the same time; the next code is checked
    /// right after the first one, once, and the screen stays.
    /// </summary>
    [Fact]
    public void CheckIn_WhilePending_KeepsTheNextScanAndChecksItAfterwards() => Run(() =>
    {
        const string Next = "ETK-BCDEFGHJKLMNPQRSTUVWXYZ234";
        var checkIns = new ControlledCheckInService();
        using var form = ChosenEvent(checkIns);
        var code = Find<TextBox>(form, "txtKod");
        code.Text = Code;

        var first = form.CheckInAsync();
        Assert.True(code.Enabled);
        Assert.Equal(string.Empty, code.Text);
        code.Text = Next; // the scanner types while the first check runs
        var second = form.CheckInAsync();
        var third = form.CheckInAsync();

        var call = Assert.Single(checkIns.CheckIns);
        Assert.Equal((1, Code), (call.EventId, call.Code));
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.True(third.IsCompletedSuccessfully && !third.Result);
        Assert.False(Find<ComboBox>(form, "cmbEtkinlik").Enabled);
        Assert.Equal(Next, code.Text);

        checkIns.CheckIns[0].Answer.SetResult(new CheckInResult(CheckInOutcome.CheckedIn, new CheckInCount(1, 5)));

        Assert.Equal(2, checkIns.CheckIns.Count);
        Assert.Equal((1, Next), (checkIns.CheckIns[1].EventId, checkIns.CheckIns[1].Code));
        Assert.Equal(string.Empty, code.Text);
        Assert.False(first.IsCompleted);

        checkIns.CheckIns[1].Answer.SetResult(new CheckInResult(CheckInOutcome.AlreadyUsed, new CheckInCount(1, 5)));

        Assert.True(first.IsCompletedSuccessfully && first.Result);
        Assert.Equal(2, checkIns.CheckIns.Count);
        Assert.Equal(BiletKontrolForm.AlreadyUsedText, Find<Label>(form, "lblSonuc").Text);
        Assert.Equal("Giriş yapan: 1 / 5", Find<Label>(form, "lblSayac").Text);
        Assert.True(Find<ComboBox>(form, "cmbEtkinlik").Enabled);
        Assert.False(form.IsDisposed);
    });

    /// <summary>
    /// The event is chosen and a ticket scanned at once: the counter loaded for the choice answers after the
    /// check-in and must not replace the newer counter of the check-in, nor its result.
    /// </summary>
    [Fact]
    public void Counter_LoadedBeforeACheckInButAnsweredAfterIt_DoesNotOverwriteIt() => Run(() =>
    {
        var checkIns = new ControlledCheckInService();
        using var form = new BiletKontrolForm(new ControlledEventService(), checkIns, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ComboBox>(form, "cmbEtkinlik").SelectedIndex = 0;
        Find<TextBox>(form, "txtKod").Text = Code;

        var run = form.CheckInAsync();
        checkIns.CheckIns[0].Answer.SetResult(new CheckInResult(CheckInOutcome.CheckedIn, new CheckInCount(1, 5)));
        checkIns.CountRequests[0].Answer.SetResult(new CheckInCount(0, 5));

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal("Giriş yapan: 1 / 5", Find<Label>(form, "lblSayac").Text);
        Assert.Equal(BiletKontrolForm.CheckedInText, Find<Label>(form, "lblSonuc").Text);
    });

    /// <summary>A check-in shows the participant's name, so the staff can compare it with an identity document.</summary>
    [Fact]
    public void CheckIn_ShowsTheNameOfTheParticipant() => Run(() =>
    {
        var checkIns = new ControlledCheckInService();
        using var form = ChosenEvent(checkIns);
        Find<TextBox>(form, "txtKod").Text = Code;

        var run = form.CheckInAsync();
        checkIns.CheckIns[0].Answer.SetResult(new CheckInResult(CheckInOutcome.CheckedIn, new CheckInCount(1, 5), "Deniz Örnek"));

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(BiletKontrolForm.CheckedInText + " - Deniz Örnek", Find<Label>(form, "lblSonuc").Text);
    });

    [Theory]
    [InlineData(CheckInOutcome.AlreadyUsed, BiletKontrolForm.AlreadyUsedText)]
    [InlineData(CheckInOutcome.Rejected, BiletKontrolForm.RejectedText)]
    [InlineData(CheckInOutcome.NotEventDay, BiletKontrolForm.NotEventDayText)]
    public void CheckIn_Refusals_AreShownOnTheSameScreen(CheckInOutcome outcome, string text) => Run(() =>
    {
        var checkIns = new ControlledCheckInService();
        var prompts = new RecordingPrompts();
        using var form = ChosenEvent(checkIns, prompts);
        Find<TextBox>(form, "txtKod").Text = Code;

        var run = form.CheckInAsync();
        checkIns.CheckIns[0].Answer.SetResult(new CheckInResult(outcome, new CheckInCount(2, 5)));

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Equal(text, Find<Label>(form, "lblSonuc").Text);
        Assert.Empty(prompts.Messages); // no dialog: the gate keeps scanning
        Assert.DoesNotContain(Code, text, StringComparison.Ordinal);
    });

    [Fact]
    public void CheckIn_WithoutAnEvent_AsksForOneAndForgetsTheCode() => Run(() =>
    {
        var checkIns = new ControlledCheckInService();
        var prompts = new RecordingPrompts();
        using var form = new BiletKontrolForm(new ControlledEventService(), checkIns, Sessions.Admin) { Prompts = prompts };
        Find<TextBox>(form, "txtKod").Text = Code;

        Assert.True(form.CheckInAsync().IsCompletedSuccessfully);

        Assert.Empty(checkIns.CheckIns);
        Assert.Equal(["Lütfen önce etkinliği seçin."], prompts.Messages);
        Assert.Equal(string.Empty, Find<TextBox>(form, "txtKod").Text);
    });

    [Fact]
    public void Counter_AnswersOutOfOrder_ShowOnlyTheLatestEvent() => Run(() =>
    {
        var checkIns = new ControlledCheckInService();
        using var form = new BiletKontrolForm(new ControlledEventService(), checkIns, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        var picker = Find<ComboBox>(form, "cmbEtkinlik");

        picker.SelectedIndex = 0;
        picker.SelectedIndex = 1;
        checkIns.CountRequests[1].Answer.SetResult(new CheckInCount(7, 30));
        checkIns.CountRequests[0].Answer.SetResult(new CheckInCount(1, 150));

        Assert.Equal("Giriş yapan: 7 / 30", Find<Label>(form, "lblSayac").Text);
    });

    [Fact]
    public void TicketView_ShowsTheQrCodeAndTheCodeOfAnActiveRegistration() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        var prompts = new RecordingPrompts();
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User) { Prompts = prompts };
        var list = Find<ListBox>(form, "lstKayitlar");
        list.Items.Add(new RegistrationItem(Sessions.Registration(5) with { TicketCode = Code }));
        list.SelectedIndex = 0;

        using var ticket = form.CreateTicketView();

        Assert.NotNull(ticket);
        Assert.Equal(Code, Find<TextBox>(ticket, "txtKod").Text);
        Assert.True(Find<TextBox>(ticket, "txtKod").ReadOnly);
        var image = Find<PictureBox>(ticket, "picQr").Image;
        Assert.NotNull(image);
        Assert.Equal(image.Width, image.Height);
        Assert.Empty(prompts.Messages);
    });

    [Theory]
    [InlineData(RegistrationStatus.Waiting, "Bu kaydın bileti yok: katılımcı bekleme listesinde ya da kayıt iptal edilmiş.")]
    [InlineData(RegistrationStatus.EventCancelled, "Bu kaydın bileti yok: katılımcı bekleme listesinde ya da kayıt iptal edilmiş.")]
    [InlineData(RegistrationStatus.Active, "Bilet yalnızca katılımcıyı ekleyen hesapta gösterilir.")]
    public void TicketView_WithoutAVisibleTicket_TellsWhy(RegistrationStatus status, string message) => Run(() =>
    {
        var prompts = new RecordingPrompts();
        using var form = new KayitEkleForm(new ControlledEventService(), new ControlledRegistrationService(), Sessions.Admin) { Prompts = prompts };
        var list = Find<ListBox>(form, "lstKayitlar");
        list.Items.Add(new RegistrationItem(Sessions.Registration(5) with { TicketCode = null, Status = status }));
        list.SelectedIndex = 0;

        Assert.Null(form.CreateTicketView());
        Assert.Equal([message], prompts.Messages);
    });

    private static BiletKontrolForm ChosenEvent(ControlledCheckInService checkIns, RecordingPrompts? prompts = null)
    {
        var form = new BiletKontrolForm(new ControlledEventService(), checkIns, Sessions.Admin) { Prompts = prompts ?? new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ComboBox>(form, "cmbEtkinlik").SelectedIndex = 0;
        checkIns.CountRequests[0].Answer.SetResult(new CheckInCount(0, 5));
        return form;
    }
}
