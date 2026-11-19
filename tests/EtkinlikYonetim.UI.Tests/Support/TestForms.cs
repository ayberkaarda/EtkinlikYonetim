using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests.Support;

/// <summary>
/// Creates every screen with test doubles for its dependencies and loaded with long names, so no window,
/// database or message window is involved and the layout is checked with the hardest content.
/// </summary>
internal static class TestForms
{
    /// <summary>A participant name long enough to need two lines on a narrow screen.</summary>
    public const string LongName = "Ayşegül Karamanlıoğlu Büyükçekmeceli Taşdelenoğulları Yıldırımoğlu";

    /// <summary>An event name long enough to need an ellipsis beside its pill.</summary>
    public const string LongEvent = "Bilim ve Teknoloji Festivali Kapanış Paneli: Yapay Zekâ, Uzay ve Geleceğin Meslekleri Üzerine";

    /// <summary>The screens of the application by class name.</summary>
    public static readonly string[] Names =
    [
        nameof(GirisYapForm), nameof(KayitOlForm), nameof(IlkKurulumForm), nameof(Form1), nameof(KatilimciEkleForm),
        nameof(KayitEkleForm), nameof(BiletForm), nameof(BiletKontrolForm), nameof(KatilimciGoruntule),
        nameof(EtkinlikYonetForm), nameof(KullaniciYonetForm),
    ];

    public static AppDialog Create(string name) => name switch
    {
        nameof(GirisYapForm) => new GirisYapForm(new PendingAuthService(), new NoFormsFactory()) { OfferAdminSetup = true },
        nameof(KayitOlForm) => new KayitOlForm(new PendingAuthService()),
        nameof(IlkKurulumForm) => new IlkKurulumForm(new PendingSetupService()) { RequireSetupKey = true },
        nameof(Form1) => AdminPanel(),
        nameof(KatilimciEkleForm) => new KatilimciEkleForm(new ControlledParticipantService(), new NoFormsFactory(), Sessions.User),
        nameof(KayitEkleForm) => Registrations(),
        nameof(BiletForm) => new BiletForm(Sessions.Registration(5) with { EventName = LongEvent, ParticipantName = LongName }),
        nameof(BiletKontrolForm) => CheckIn(),
        nameof(KatilimciGoruntule) => Listing(),
        nameof(EtkinlikYonetForm) => EventManagement(),
        nameof(KullaniciYonetForm) => Accounts(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>Events with a long name, a full one with a waiting list and a cancelled one.</summary>
    public static ControlledEventService Events()
    {
        var events = new ControlledEventService();
        events.Events.Add(new EventSummary(3, LongEvent, new DateOnly(2027, 6, 12), 400, 400, 12));
        events.Events.Add(new EventSummary(4, "Fotoğraf Yürüyüşü", new DateOnly(2027, 6, 26), 25, 9, 0, null, Cancelled: true));
        return events;
    }

    public static Form1 AdminPanel()
    {
        var form = new Form1(Events(), new NoFormsFactory(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        return form;
    }

    public static KayitEkleForm Registrations()
    {
        var registrations = new ControlledRegistrationService();
        registrations.Registrations.Add(Sessions.Registration(5) with { EventName = LongEvent, ParticipantName = LongName });
        registrations.Registrations.Add(Sessions.Registration(6) with { TicketCode = null, Status = RegistrationStatus.Waiting, QueuePosition = 12 });
        registrations.Registrations.Add(Sessions.Registration(7) with { TicketCode = null, Status = RegistrationStatus.EventCancelled });
        var form = new KayitEkleForm(Events(), registrations, Sessions.User) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        return form;
    }

    /// <summary>The entrance right after a ticket of a participant with a very long name was checked in.</summary>
    public static BiletKontrolForm CheckIn()
    {
        var checkIns = new ControlledCheckInService();
        var form = new BiletKontrolForm(Events(), checkIns, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ComboBox>(form, "cmbEtkinlik").SelectedIndex = 0;
        checkIns.CountRequests[0].Answer.SetResult(new CheckInCount(87, 112));
        Find<TextBox>(form, "txtKod").Text = "ETK-ABCDEFGHJKLMNPQRSTUVWXYZ23";
        var check = form.CheckInAsync();
        checkIns.CheckIns[0].Answer.SetResult(new CheckInResult(CheckInOutcome.CheckedIn, new CheckInCount(988, 1000), LongName));
        Assert.True(check.IsCompletedSuccessfully);
        return form;
    }

    public static KatilimciGoruntule Listing()
    {
        var participants = new ControlledParticipantService();
        var form = new KatilimciGoruntule(Events(), participants, new ControlledExportService(), Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        participants.ListRequests[0].Answer.SetResult(
        [
            new ParticipantView(LongName, "100******46", "aysegul.karamanlioglu.buyukcekmeceli@example.com", "Giriş yapıldı"),
            new ParticipantView("Ece Taslak", "100******14", "ece@example.com", "Bekleme listesi, sıra: 1"),
        ]);
        return form;
    }

    public static EtkinlikYonetForm EventManagement()
    {
        var events = Events();
        events.DatabaseToday = new DateOnly(2027, 3, 1);
        var form = new EtkinlikYonetForm(events, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = 2;
        return form;
    }

    public static KullaniciYonetForm Accounts()
    {
        var accounts = new ControlledAccountService();
        accounts.Accounts.Add(new AccountSummary(3, "aysegulkaramanlioglubuyukcekmeceli", UserRole.User, true));
        var form = new KullaniciYonetForm(accounts, Sessions.Admin) { Prompts = new RecordingPrompts() };
        Assert.True(form.LoadAsync().IsCompletedSuccessfully);
        return form;
    }
}
