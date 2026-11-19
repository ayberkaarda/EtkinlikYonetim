using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Admin panel: creates events, lists the events with their seats and opens the participant,
    /// registration, entrance, listing and account screens. When the database no longer accepts the admin's
    /// session, or the admin signs out, the panel closes and the sign-in returns.
    /// </summary>
    public sealed class Form1 : AppDialog, ISessionPanel
    {
        private readonly IEventService _events;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _create = new();
        private readonly RequestSequence _listRequests = new();

        private readonly TextBox txtEtkinlikAd = new() { Name = nameof(txtEtkinlikAd), MaxLength = InputRules.EventNameMaxLength };
        private readonly DatePicker dtpTarih = new() { Name = nameof(dtpTarih), Format = DateTimePickerFormat.Short, Width = 130 };
        private readonly NumericUpDown numKapasite = new() { Name = nameof(numKapasite), Minimum = InputRules.MinCapacity, Maximum = InputRules.MaxCapacity };
        private readonly ThemedButton btnEtkinlikEkle = FormLayout.Button(nameof(btnEtkinlikEkle), "Etkinliği ekle", ButtonKind.Primary, Glyphs.Add);
        private readonly ThemedListBox lstOzet = new() { Name = nameof(lstOzet) };
        private readonly ThemedButton btnEtkinlikYonet = Shortcut(nameof(btnEtkinlikYonet), "Etkinlik yönetimi", Glyphs.Calendar);
        private readonly ThemedButton btnKatilimciEkle = Shortcut(nameof(btnKatilimciEkle), "Katılımcı ekle", Glyphs.AddPerson);
        private readonly ThemedButton btnKayit = Shortcut(nameof(btnKayit), "Kayıt ve bilet", Glyphs.QrCode);
        private readonly ThemedButton btnBiletKontrol = Shortcut(nameof(btnBiletKontrol), "Giriş kontrolü", Glyphs.CheckMark);
        private readonly ThemedButton btnKatilimciGoruntule = Shortcut(nameof(btnKatilimciGoruntule), "Katılımcı listesi", Glyphs.List);
        private readonly ThemedButton btnKullanicilar = Shortcut(nameof(btnKullanicilar), "Hesaplar", Glyphs.People);
        private readonly ThemedButton btnCikis = FormLayout.Button(nameof(btnCikis), "Oturumu kapat", ButtonKind.Secondary);

        public Form1(IEventService events, IFormFactory forms, UserSession session)
            : base("Yönetim paneli", "Etkinlik oluşturun; kayıtları, biletleri, girişi ve hesapları yönetin.", Glyphs.Calendar, new Size(1040, 640), new Size(900, 560))
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _forms = forms;
            _session = session;
            dtpTarih.MinDate = DateTime.Today;
            BuildLayout(session);

            btnEtkinlikEkle.Click += async (_, _) => await CreateEventAsync();
            btnEtkinlikYonet.Click += (_, _) => Open<EtkinlikYonetForm>();
            btnKatilimciEkle.Click += (_, _) => Open<KatilimciEkleForm>();
            btnKayit.Click += (_, _) => Open<KayitEkleForm>();
            btnBiletKontrol.Click += (_, _) => Open<BiletKontrolForm>();
            btnKatilimciGoruntule.Click += (_, _) => Open<KatilimciGoruntule>();
            btnKullanicilar.Click += (_, _) => Open<KullaniciYonetForm>();
            btnCikis.Click += (_, _) => EndSession();
            Load += async (_, _) => await LoadAsync();
        }

        /// <inheritdoc/>
        public bool SignedOut { get; private set; }

        /// <inheritdoc/>
        public void EndSession()
        {
            SignedOut = true;
            SessionEnd.Close(this);
        }

        /// <summary>Lists the events with their seats.</summary>
        internal Task LoadAsync() => this.RunAsync(RefreshEventsAsync);

        /// <summary>Creates the event; ignored while a previous save is still running.</summary>
        internal Task<bool> CreateEventAsync() =>
            this.RunExclusiveAsync(
                _create,
                async () =>
                {
                    await _events.CreateAsync(_session, txtEtkinlikAd.Text, dtpTarih.Value.Date, (int)numKapasite.Value);
                    Prompts.Inform("Etkinlik başarıyla eklendi.");

                    txtEtkinlikAd.Clear();
                    numKapasite.Value = InputRules.MinCapacity;
                    dtpTarih.Value = DateTime.Today;
                    _create.FocusAfterUnlock(txtEtkinlikAd);
                    await RefreshEventsAsync();
                },
                btnEtkinlikEkle);

        private async Task RefreshEventsAsync()
        {
            var request = _listRequests.Next();
            lstOzet.IsLoading = true;
            try
            {
                var events = await _events.GetEventsAsync(_session);
                if (_listRequests.IsLatest(request) && !IsDisposed)
                {
                    lstOzet.ReplaceItems(events);
                }
            }
            finally
            {
                if (_listRequests.IsLatest(request) && !IsDisposed)
                {
                    lstOzet.IsLoading = false;
                }
            }
        }

        /// <summary>Opens a screen of the session as a dialog; seats may have changed when it closes.</summary>
        private void Open<T>()
            where T : Form
        {
            using (var form = _forms.Create<T>(_session))
            {
                form.ShowDialog(this);
            }

            if (!SignedOut && !IsDisposed)
            {
                _ = LoadAsync();
            }
        }

        private static ThemedButton Shortcut(string name, string text, string glyph)
        {
            var button = FormLayout.Button(name, text, ButtonKind.Secondary, glyph);
            button.TextAlign = ContentAlignment.MiddleLeft;
            return button;
        }

        private void BuildLayout(UserSession session)
        {
            Name = nameof(Form1);
            var user = FormLayout.Text("lblOturum", session.UserName + " · Yönetici");
            user.ForeColor = Theme.Current.TextSecondary;
            SetHeaderAside(user);

            // A new event: name, then date and capacity side by side, then the action.
            var name = FormLayout.Field("Etkinlik &adı", FormLayout.Input(txtEtkinlikAd));
            name.Margin = new Padding(0, 0, 0, Spacing.M);
            var when = FormLayout.FieldRow(
                "eventRow",
                (FormLayout.Field("&Tarih", FormLayout.Date(dtpTarih)), 50),
                (FormLayout.Field("&Kapasite", FormLayout.Number(numKapasite), "Kişi"), 50));
            btnEtkinlikEkle.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            btnEtkinlikEkle.Margin = new Padding(0, Spacing.XS, 0, 0);
            var newCard = FormLayout.Card("newEventCard", "Yeni etkinlik", FormLayout.Stack("newEventForm", name, when, btnEtkinlikEkle), fill: false);

            lstOzet.Describe = item => item is EventSummary summary ? EventRows.Describe(summary) : new ListRow(item.ToString() ?? string.Empty);
            lstOzet.EmptyText = "Henüz etkinlik yok. Yukarıdaki formla ilk etkinliği ekleyin.";
            lstOzet.TabStop = false;
            var listCard = FormLayout.Card("eventsCard", "Etkinlikler", lstOzet);
            listCard.Margin = Padding.Empty;
            var left = FormLayout.Page("leftColumn", (newCard, false), (listCard, true));

            // The other screens, each with one line on what it is for.
            var screens = new TableLayoutPanel
            {
                Name = "screens",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            screens.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            screens.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            (ThemedButton Button, string Description)[] items =
            [
                (btnEtkinlikYonet, "Tarihi, kapasiteyi ve açıklamayı değiştirin ya da etkinliği iptal edin."),
                (btnKatilimciEkle, "T.C. kimlik no, ad soyad ve e-postayla yeni bir katılımcı ekleyin."),
                (btnKayit, "Katılımcıyı etkinliğe kaydedin ve QR kodlu bileti gösterin; dolu etkinlikte bekleme listesi."),
                (btnBiletKontrol, "Kapıda bilet kodunu okutun; sonuç ve içerideki kişi sayısı hemen görünür."),
                (btnKatilimciGoruntule, "Etkinliğin katılımcılarını görün; CSV ya da yazdırılabilir liste olarak kaydedin."),
                (btnKullanicilar, "Hesapları etkinleştirin, devre dışı bırakın ve rollerini değiştirin."),
            ];
            for (var i = 0; i < items.Length; i++)
            {
                var (button, description) = items[i];
                screens.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                button.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
                button.Margin = new Padding(0, 0, Spacing.L, Spacing.M);
                var text = FormLayout.Hint(description);
                text.Name = button.Name + "Description";
                text.Font = Theme.Fonts.Body;
                text.ForeColor = Theme.Current.TextSecondary;
                text.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                text.Margin = new Padding(0, 0, 0, Spacing.M);
                screens.Controls.Add(button, 0, i);
                screens.Controls.Add(text, 1, i);
            }

            // The card fills its column, so both columns end on one line; the shortcuts stay at its top.
            var screensCard = FormLayout.Card("screensCard", "Ekranlar", FormLayout.Page("screensPage", (screens, false)));
            screensCard.Margin = Padding.Empty;
            var right = FormLayout.Page("rightColumn", (screensCard, true));
            Content.Controls.Add(FormLayout.Columns("layout", left, 44, right));

            AcceptButton = btnEtkinlikEkle;
            CancelButton = btnCikis;
            SetFooterNote("Oturumu kapatınca giriş ekranı açılır; pencereyi kapatmak uygulamayı kapatır.");
            SetFooterButtons(btnCikis);
            FinishLayout();
        }
    }
}
