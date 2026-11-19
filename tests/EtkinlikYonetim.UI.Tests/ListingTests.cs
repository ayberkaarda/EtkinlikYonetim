using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

public sealed class ListingTests
{
    private static readonly ParticipantView Deniz = new("Deniz Örnek", "100******46", "deniz@example.com", "ETK-ABCDEFGHJK");
    private static readonly ParticipantView Ece = new("Ece Taslak", "100******14", "ece@example.com", "ETK-BCDEFGHJKL");

    [Fact]
    public void Participants_AnswersOutOfOrder_ShowOnlyTheLatestSelection() => Run(() =>
    {
        var participants = new ControlledParticipantService();
        var events = new ControlledEventService();
        using var form = new KatilimciGoruntule(events, participants, Sessions.Admin);
        var picker = Find<ComboBox>(form, "cmbEtkinlikSec");
        var list = Find<ListBox>(form, "lstKatilimcilar");
        picker.Items.Add(events.Events[0]);
        picker.Items.Add(events.Events[1]);

        picker.SelectedIndex = 0; // request for event 1
        picker.SelectedIndex = 1; // request for event 2
        Assert.Equal([1, 2], participants.ListRequests.Select(r => r.EventId));

        // The newer answer arrives first, then the stale one.
        participants.ListRequests[1].Answer.SetResult([Ece]);
        participants.ListRequests[0].Answer.SetResult([Deniz]);

        Assert.Equal(["Ece Taslak - 100******14 - ece@example.com - ETK-BCDEFGHJKL"], list.Items.Cast<object>().Select(i => i.ToString()));
    });

    [Fact]
    public void Participants_NewSelection_ClearsTheOldListImmediately() => Run(() =>
    {
        var participants = new ControlledParticipantService();
        var events = new ControlledEventService();
        using var form = new KatilimciGoruntule(events, participants, Sessions.Admin);
        var picker = Find<ComboBox>(form, "cmbEtkinlikSec");
        var list = Find<ListBox>(form, "lstKatilimcilar");
        picker.Items.Add(events.Events[0]);
        picker.Items.Add(events.Events[1]);
        picker.SelectedIndex = 0;
        participants.ListRequests[0].Answer.SetResult([Deniz]);
        Assert.Single(list.Items.Cast<object>());

        picker.SelectedIndex = 1;

        Assert.Empty(list.Items.Cast<object>());
    });

    [Fact]
    public void Items_ShowEventDateParticipantAndTicket()
    {
        Assert.Equal(
            "Bahar Konseri - 17.04.2027 - Deniz Örnek - Bilet: ETK-ABCDEFGHJK",
            new RegistrationItem(Sessions.Registration(5)).ToString());
        Assert.Equal(
            "Eski Etkinlik - 01.02.2026 - Ad Bilinmiyor - Bilet: -",
            new RegistrationItem(new RegistrationDetails(6, 2, "Eski Etkinlik", new DateOnly(2026, 2, 1), 3, null, null)).ToString());
        Assert.Equal("Bahar Konseri - 17.04.2027", new EventSummary(1, "Bahar Konseri", new DateOnly(2027, 4, 17), 150, 0).ToString());
        Assert.Equal(
            "Bahar Konseri - 17.04.2027 - Deniz Örnek - Bekleme listesi, sıra: 2",
            new RegistrationItem(Sessions.Registration(5) with { TicketCode = null, Status = RegistrationStatus.Waiting, QueuePosition = 2 }).ToString());
    }

    [Fact]
    public void RegistrationForm_LoadsOpenEventsAndTheAccountsRegistrations() => Run(() =>
    {
        var registrations = new ControlledRegistrationService();
        registrations.Registrations.Add(Sessions.Registration(5));
        using var form = new KayitEkleForm(new ControlledEventService(), registrations, Sessions.User);

        var load = form.LoadAsync();

        Assert.True(load.IsCompletedSuccessfully);
        Assert.Equal(0, Find<ComboBox>(form, "cmbEtkinlikler").SelectedIndex);
        Assert.Single(Find<ListBox>(form, "lstKayitlar").Items.Cast<object>());
    });
}
