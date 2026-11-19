using System.Globalization;
using EtkinlikYonetim.UI;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.Screenshots;

/// <summary>The screens shown in the README, each prepared with synthetic data as an admin or a user would see it.</summary>
internal static class Screens
{
    /// <summary>File name (kebab-case, without extension) and how to build the screen.</summary>
    public static IReadOnlyList<(string Name, Func<SyntheticFestival, Form> Build)> All { get; } =
    [
        ("giris", festival => new GirisYapForm(festival, new UnusedFormFactory())),
        ("kayit-ol", festival => new KayitOlForm(festival)),
        ("ilk-kurulum", festival => new IlkKurulumForm(festival)),
        ("yonetim-paneli", AdminPanel),
        ("katilimci-ekle", AddParticipant),
        ("kayitlar-ve-biletler", Registrations),
        ("bilet", festival => new BiletForm(festival.GetMyRegistrationsAsync(festival.User).Result[0])),
        ("giris-kontrolu", CheckIn),
        ("katilimci-listesi", Listing),
        ("etkinlik-yonetimi", EventManagement),
        ("hesaplar", Accounts),
        ("onay-penceresi", Confirmation),
    ];

    /// <summary>The participants of the first event: inside, not yet inside and waiting.</summary>
    private static KatilimciGoruntule Listing(SyntheticFestival festival)
    {
        var form = new KatilimciGoruntule(festival, festival, festival, festival.Admin);
        Run(form.LoadAsync());
        return form;
    }

    /// <summary>Event management with an upcoming event selected; the database's day stands before the season.</summary>
    private static EtkinlikYonetForm EventManagement(SyntheticFestival festival)
    {
        var form = new EtkinlikYonetForm(festival, festival.Admin);
        Run(form.LoadAsync());
        Find<ListBox>(form, "lstEtkinlikler").SelectedIndex = 0;
        return form;
    }

    private static KullaniciYonetForm Accounts(SyntheticFestival festival)
    {
        var form = new KullaniciYonetForm(festival, festival.Admin);
        Run(form.LoadAsync());
        Find<ListBox>(form, "lstHesaplar").SelectedIndex = 4;
        return form;
    }

    /// <summary>The question asked before an event is cancelled.</summary>
    private static MessageDialog Confirmation(SyntheticFestival festival) => new(
        "Seçilen etkinlik iptal edilsin mi? Tüm kayıtlar ve bekleme listesi kapanır, biletler geçersiz olur. Bu işlem geri alınamaz.",
        "Onay",
        MessageBoxButtons.YesNo,
        MessageBoxIcon.Question);

    /// <summary>The entrance right after a ticket was scanned: the participant's name and the counter.</summary>
    private static BiletKontrolForm CheckIn(SyntheticFestival festival)
    {
        var form = new BiletKontrolForm(festival, festival, festival.Admin);
        Run(form.LoadAsync());
        Find<ComboBox>(form, "cmbEtkinlik").SelectedIndex = 0;
        Find<TextBox>(form, "txtKod").Text = SyntheticFestival.SampleTicket;
        Run(form.CheckInAsync());
        return form;
    }

    /// <summary>A user's main screen with a participant being typed and the e-mail address checked.</summary>
    private static KatilimciEkleForm AddParticipant(SyntheticFestival festival)
    {
        var form = new KatilimciEkleForm(festival, new UnusedFormFactory(), festival.User);
        Find<TextBox>(form, "txtTC").Text = "10000000146";
        Find<TextBox>(form, "txtAdSoyad").Text = "Selin Ateş";
        Find<TextBox>(form, "txtEmail").Text = "selin.ates@example.com";
        form.ShowEmailCheck();
        return form;
    }

    /// <summary>The registrations after a participant was put on the waiting list of a full event.</summary>
    private static KayitEkleForm Registrations(SyntheticFestival festival)
    {
        var form = new KayitEkleForm(festival, festival, festival.User);
        Run(form.LoadAsync());
        Find<ComboBox>(form, "cmbEtkinlikler").SelectedIndex = 1;
        Find<TextBox>(form, "txtKatilimciTC").Text = "31800000072";
        Run(form.RegisterAsync());
        Find<ComboBox>(form, "cmbEtkinlikler").SelectedIndex = 1;
        Find<ListBox>(form, "lstKayitlar").SelectedIndex = 2;
        return form;
    }

    /// <summary>The admin panel with the season's events and a new event being typed.</summary>
    private static Form1 AdminPanel(SyntheticFestival festival)
    {
        var form = new Form1(festival, new UnusedFormFactory(), festival.Admin);
        Run(form.LoadAsync());
        Find<TextBox>(form, "txtEtkinlikAd").Text = "Caz Akşamı";
        Find<NumericUpDown>(form, "numKapasite").Value = 120;
        return form;
    }

    /// <summary>The in-memory services complete synchronously; anything else is a bug in the tool.</summary>
    internal static void Run(Task task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException("A screen did not finish loading synchronously.");
        }

        task.GetAwaiter().GetResult();
    }

    internal static T Find<T>(Control root, string name)
        where T : Control =>
        root.Controls.Find(name, searchAllChildren: true).OfType<T>().SingleOrDefault()
        ?? throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Control '{0}' was not found.", name));

    /// <summary>No other window is opened while a screen is drawn.</summary>
    private sealed class UnusedFormFactory : IFormFactory
    {
        public T Create<T>(params object[] arguments)
            where T : Form => throw new InvalidOperationException("Screens are not opened while capturing.");
    }
}
