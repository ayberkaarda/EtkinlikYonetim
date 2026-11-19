using System.Globalization;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Tests.Support;
using EtkinlikYonetim.UI.Theming;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>The event management window: binding, editable states and exclusive changes. No window is shown.</summary>
public sealed class EventManagementUiTests
{
    [Fact]
    public void Selection_ShowsTheFieldsOfTheEvent() => Run(() =>
    {
        var events = EventsWithStates();
        using var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        var list = Find<ListBox>(form, "lstEtkinlikler");

        // Loading selects nothing on its own: the admin chooses the event.
        Assert.Equal(-1, list.SelectedIndex);
        Assert.False(Find<Button>(form, "btnKaydet").Enabled);

        list.SelectedIndex = 0;

        Assert.Equal(new DateTime(2027, 4, 17), Find<DateTimePicker>(form, "dtpTarih").Value);
        Assert.Equal(150, Find<NumericUpDown>(form, "numKapasite").Value);
        Assert.Equal("Açık hava", Find<TextBox>(form, "txtAciklama").Text);
        Assert.Equal("Kayıtlı: 10 / 150, bekleme listesi: 0", Find<Label>(form, "lblDoluluk").Text);
        Assert.True(Find<Button>(form, "btnKaydet").Enabled);
        Assert.True(Find<Button>(form, "btnIptalEt").Enabled);
    });

    /// <summary>Past and cancelled events are shown read-only; the service would refuse them anyway.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PastOrCancelledEvent_CannotBeEdited(int index) => Run(() =>
    {
        using var form = new EtkinlikYonetForm(EventsWithStates(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = index;

        Assert.False(Find<Button>(form, "btnKaydet").Enabled);
        Assert.False(Find<Button>(form, "btnIptalEt").Enabled);
        Assert.False(Find<DateTimePicker>(form, "dtpTarih").Enabled);
        Assert.True(Find<TextBox>(form, "txtAciklama").ReadOnly);
    });

    /// <summary>
    /// Whether an event can still be changed is decided by the database's day, not by this computer's clock
    /// (which stands somewhere between the two cases): an event years in the computer's past is still
    /// current when the database's day is before it, so a clock far ahead does not lock it; an event years in
    /// the computer's future has passed when the database's day is after it, so a clock far behind does not
    /// open it.
    /// </summary>
    [Theory]
    [InlineData("2020-06-01", "2020-05-01", true)]
    [InlineData("2090-06-01", "2090-06-02", false)]
    public void Editability_FollowsTheDatabaseDayNotTheComputerClock(string eventDay, string databaseDay, bool editable) => Run(() =>
    {
        var events = new ControlledEventService();
        events.Events.Clear();
        events.Events.Add(new EventSummary(1, "Saat Testi", DateOnly.Parse(eventDay, CultureInfo.InvariantCulture), 50, 0));
        events.DatabaseToday = DateOnly.Parse(databaseDay, CultureInfo.InvariantCulture);
        using var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);

        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = 0;

        Assert.Equal(editable, Find<Button>(form, "btnKaydet").Enabled);
        Assert.Equal(editable, Find<Button>(form, "btnIptalEt").Enabled);
        Assert.Equal(editable, Find<DateTimePicker>(form, "dtpTarih").Enabled);
        Assert.Equal(!editable, Find<TextBox>(form, "txtAciklama").ReadOnly);
    });

    [Fact]
    public void Save_WhilePending_IsNotSentTwiceAndLocksBothButtons() => Run(() =>
    {
        var events = EventsWithStates();
        using var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = 0;
        Find<NumericUpDown>(form, "numKapasite").Value = 120;

        var first = form.SaveAsync();
        var second = form.CancelEventAsync();

        Assert.Equal(1, events.UpdateCalls);
        Assert.Equal(0, events.CancelCalls);
        Assert.Equal((1, new DateTime(2027, 4, 17), 120, "Açık hava"), Assert.Single(events.Updates));
        Assert.False(first.IsCompleted);
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(Find<Button>(form, "btnKaydet").Enabled);
        Assert.False(Find<Button>(form, "btnIptalEt").Enabled);
    });

    [Fact]
    public void Cancel_AsksForConfirmationOnce() => Run(() =>
    {
        var events = EventsWithStates();
        var prompts = new RecordingPrompts();
        using var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = prompts };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = 0;

        _ = form.CancelEventAsync();

        Assert.Equal(1, events.CancelCalls);
        Assert.StartsWith("Seçilen etkinlik iptal edilsin mi?", Assert.Single(prompts.Messages), StringComparison.Ordinal);
    });

    [Fact]
    public void Change_WithoutASelection_AsksForOneAndCallsNothing() => Run(() =>
    {
        var events = EventsWithStates();
        var prompts = new RecordingPrompts();
        using var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = prompts };

        Assert.True(form.SaveAsync().IsCompletedSuccessfully);

        Assert.Equal(0, events.UpdateCalls);
        Assert.Equal(["Lütfen bir etkinlik seçin."], prompts.Messages);
    });

    /// <summary>
    /// The window sends the event as it loaded it. When another admin changed it in the meantime the service
    /// refuses; the window then shows the event as it is now and the message, and keeps nothing of the old form.
    /// </summary>
    [Fact]
    public void Save_EventChangedElsewhere_ReloadsTheEventAndSaysSo() => Run(() =>
    {
        var events = EventsWithStates();
        using var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = 0;
        var loaded = events.Events[0];
        Find<NumericUpDown>(form, "numKapasite").Value = 120;

        // Another admin moves the event a week later while this form is open.
        events.Events[0] = loaded with { Date = new DateOnly(2027, 4, 24) };
        events.UpdateFailure = new StaleDataException("Etkinlik başka biri tarafından değiştirildi.");
        var dialogs = NoDialogs.Requested.Count;

        Assert.True(form.SaveAsync().IsCompletedSuccessfully);

        Assert.Same(loaded, Assert.Single(events.UpdatedOriginals));
        Assert.Equal(new DateTime(2027, 4, 24), Find<DateTimePicker>(form, "dtpTarih").Value);
        Assert.Equal(150, Find<NumericUpDown>(form, "numKapasite").Value);
        Assert.Empty(NoDialogs.Requested.Skip(dialogs));
        Assert.Equal((StatusKind.Warning, "Etkinlik başka biri tarafından değiştirildi."), (form.StatusBar.Kind, form.StatusBar.Text));
        Assert.True(Find<Button>(form, "btnKaydet").Enabled);
    });

    private static ControlledEventService EventsWithStates()
    {
        var events = new ControlledEventService();
        events.Events.Clear();
        events.Events.Add(new EventSummary(1, "Bahar Konseri", new DateOnly(2027, 4, 17), 150, 10, 0, "Açık hava"));
        events.Events.Add(new EventSummary(2, "Kış Konseri", new DateOnly(2027, 1, 10), 50, 50));
        events.Events.Add(new EventSummary(3, "Yaz Şenliği", new DateOnly(2027, 7, 1), 80, 0, 0, null, Cancelled: true));
        return events;
    }
}
