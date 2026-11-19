using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// The entrance for admins: choose the event, then type or scan ticket codes one after the other. A
    /// scanner that types the code and Enter works as is. The screen stays open after every answer, the
    /// code field is emptied at once and the code is kept nowhere. The answer is shown large, on a panel in
    /// the colour of its kind, so it reads from a step away.
    /// </summary>
    public sealed class BiletKontrolForm : AppDialog
    {
        internal const string CheckedInText = "GİRİŞ YAPILDI";
        internal const string AlreadyUsedText = "ZATEN KULLANILDI - Bu bilet daha önce okutuldu.";
        internal const string RejectedText = "GEÇERSİZ BİLET - Bu etkinlik için geçerli bir bilet değil.";
        internal const string NotEventDayText = "ETKİNLİK GÜNÜ DEĞİL - Bilet yalnızca etkinlik günü okutulabilir; bilet kullanılmadı.";

        /// <summary>Shown on the answer panel while no ticket has been checked for the chosen event.</summary>
        internal const string WaitingText = "Bilet kodunu okutun ya da yazıp Enter'a basın; sonuç burada görünür.";

        private readonly IEventService _events;
        private readonly ICheckInService _checkIns;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _check = new();
        private readonly RequestSequence _countRequests = new();
        private bool _checkQueued;

        private readonly ThemedComboBox cmbEtkinlik = FormLayout.Picker(nameof(cmbEtkinlik), 260, "Kapıdaki etkinliği seçin");
        private readonly Label lblSayac = FormLayout.Text(nameof(lblSayac));
        private readonly TextBox txtKod = new() { Name = nameof(txtKod), MaxLength = 64 };
        private readonly ThemedButton btnKontrol = FormLayout.Button(nameof(btnKontrol), "Kontrol et", ButtonKind.Primary, Glyphs.CheckMark);
        private readonly Label lblSonuc = FormLayout.Text(nameof(lblSonuc));
        private readonly Label lblBekleme = FormLayout.Text(nameof(lblBekleme), WaitingText);
        private readonly GlyphBadge _resultBadge = new(Glyphs.Ticket, 48, 18F) { Name = "resultBadge" };
        private CardPanel _resultCard = null!;

        public BiletKontrolForm(IEventService events, ICheckInService checkIns, UserSession session)
            : base("Giriş kontrolü", "Etkinliği seçin, bilet kodlarını art arda okutun; ekran her sonuçtan sonra açık kalır.", Glyphs.CheckMark, new Size(860, 600), new Size(700, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(checkIns);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _checkIns = checkIns;
            _session = session;
            BuildLayout();

            btnKontrol.Click += async (_, _) => await CheckInAsync();
            cmbEtkinlik.SelectedIndexChanged += async (_, _) => await LoadCountAsync();
            Load += async (_, _) => await LoadAsync();
        }

        /// <summary>Loads the events that are not cancelled; the admin chooses the one at the entrance.</summary>
        internal Task LoadAsync() =>
            this.RunAsync(async () =>
            {
                var events = await _events.GetEventsAsync(_session);
                cmbEtkinlik.ReplaceItems(events.Where(e => !e.Cancelled));
            });

        /// <summary>
        /// Checks the typed code in for the chosen event. The code field stays open while a check runs, so a
        /// scanner can type the next code at once; its Enter is remembered and that code is checked as soon
        /// as the running check has ended. Returns <see langword="false"/> for such a remembered call.
        /// </summary>
        internal async Task<bool> CheckInAsync()
        {
            if (_check.IsRunning)
            {
                _checkQueued = true;
                return false;
            }

            var ran = await this.RunExclusiveAsync(_check, CheckOnceAsync, cmbEtkinlik);
            if (ran && _checkQueued && !IsDisposed)
            {
                _checkQueued = false;
                await CheckInAsync();
            }

            return ran;
        }

        private async Task CheckOnceAsync()
        {
            // The code leaves the field before anything else happens, whatever the answer.
            var code = txtKod.Text;
            txtKod.Clear();
            _check.FocusAfterUnlock(txtKod);
            if (cmbEtkinlik.SelectedItem is not EventSummary selected)
            {
                Prompts.Warn("Lütfen önce etkinliği seçin.");
                return;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            var result = await _checkIns.CheckInAsync(_session, selected.Id, code);
            var (text, kind) = result.Outcome switch
            {
                // The name lets the staff compare the ticket with an identity document.
                CheckInOutcome.CheckedIn when result.ParticipantName is { Length: > 0 } name => (CheckedInText + " - " + name, StatusKind.Success),
                CheckInOutcome.CheckedIn => (CheckedInText, StatusKind.Success),
                CheckInOutcome.AlreadyUsed => (AlreadyUsedText, StatusKind.Warning),
                CheckInOutcome.NotEventDay => (NotEventDayText, StatusKind.Warning),
                _ => (RejectedText, StatusKind.Error),
            };
            ShowResult(text, kind);

            // The counter of the check-in is newer than any counter still being loaded for the choice of the
            // event, so such an older answer must not overwrite it.
            _ = _countRequests.Next();
            ShowCount(result.Count);
        }

        /// <summary>Shows the counter of the chosen event; only the answer to the latest choice is shown.</summary>
        internal async Task LoadCountAsync()
        {
            var request = _countRequests.Next();
            lblSayac.Text = string.Empty;
            ShowResult(string.Empty, null);
            if (cmbEtkinlik.SelectedItem is not EventSummary selected)
            {
                return;
            }

            await this.RunAsync(async () =>
            {
                var count = await _checkIns.GetCountAsync(_session, selected.Id);
                if (_countRequests.IsLatest(request) && !IsDisposed)
                {
                    ShowCount(count);
                }
            });
        }

        /// <summary>The kind of the answer on the panel: success, warning, error, or none while waiting for a ticket.</summary>
        internal StatusKind? ResultKind { get; private set; }

        private void ShowCount(CheckInCount count) =>
            lblSayac.Text = string.Create(CultureInfo.InvariantCulture, $"Giriş yapan: {count.CheckedIn} / {count.Seats}");

        /// <summary>
        /// Writes the answer on the panel, in the colours of its kind. Under high contrast the panel keeps the
        /// window colours and the badge's glyph tells the kinds apart.
        /// </summary>
        private void ShowResult(string text, StatusKind? kind)
        {
            ResultKind = kind;
            lblSonuc.Text = text;
            FormLayout.SetShown(lblSonuc, text.Length > 0);
            FormLayout.SetShown(lblBekleme, text.Length == 0);

            var p = Theme.Current;
            var (fill, edge, glyph, glyphColor) = kind switch
            {
                StatusKind.Success => (p.SuccessSubtle, p.Success, Glyphs.Success, p.Success),
                StatusKind.Warning => (p.WarningSubtle, p.Warning, Glyphs.Warning, p.Warning),
                StatusKind.Error => (p.DangerSubtle, p.Danger, Glyphs.Error, p.Danger),
                _ => ((Color?)null, (Color?)null, Glyphs.Ticket, p.AccentText),
            };
            _resultCard.Fill = fill;
            _resultCard.Edge = edge;
            _resultBadge.Glyph = glyph;
            _resultBadge.GlyphColor = glyphColor;
            _resultBadge.Fill = p.Surface;
            _resultBadge.Invalidate();
            _resultCard.Invalidate(invalidateChildren: true);
            lblSonuc.AccessibleName = text;
        }

        private void BuildLayout()
        {
            Name = nameof(BiletKontrolForm);

            // The gate: the event and how many are inside.
            var eventField = FormLayout.Field("&Etkinlik", cmbEtkinlik);
            lblSayac.Font = Theme.Fonts.Subtitle;
            lblSayac.MinimumSize = new Size(160, 0);
            var counter = FormLayout.Field("Sayaç", lblSayac);
            var gate = FormLayout.FieldRow("gateRow", (eventField, 100), (counter, 0));
            var gateCard = FormLayout.Card("gateCard", null, gate, fill: false);

            // The code, typed or scanned, and the check.
            txtKod.Font = Theme.Fonts.Code;
            var codeField = FormLayout.Field("Bilet &kodu", new TextField(txtKod, Sizes.ControlHeight + Spacing.M));
            btnKontrol.MinimumSize = new Size(Sizes.ButtonMinWidth + Spacing.XL, Sizes.ControlHeight + Spacing.M);
            var scan = FormLayout.FieldRow("scanRow", (codeField, 100), (FormLayout.FieldAction(btnKontrol), 0));
            var scanCard = FormLayout.Card("scanCard", null, scan, fill: false);

            // The answer, large.
            lblSonuc.Font = Theme.Fonts.Signal;
            lblSonuc.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            lblSonuc.Margin = Padding.Empty;
            lblBekleme.ForeColor = Theme.Current.TextSecondary;
            lblBekleme.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _resultBadge.Anchor = AnchorStyles.Left;
            _resultBadge.Margin = new Padding(0, 0, Spacing.L, 0);
            var answer = new TableLayoutPanel
            {
                Name = "resultLayout",
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Top,
                Margin = Padding.Empty,
            };
            answer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            answer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            answer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            answer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            answer.Controls.Add(_resultBadge, 0, 0);
            answer.SetRowSpan(_resultBadge, 2);
            answer.Controls.Add(lblSonuc, 1, 0);
            answer.Controls.Add(lblBekleme, 1, 1);
            _resultCard = FormLayout.Card("resultCard", "Sonuç", FormLayout.Page("resultPage", (answer, false)));
            _resultCard.Padding = new Padding(Spacing.XL);
            _resultCard.Margin = Padding.Empty;
            ShowResult(string.Empty, null);

            Content.Controls.Add(FormLayout.Page("checkInPage", (gateCard, false), (scanCard, false), (_resultCard, true)));

            AcceptButton = btnKontrol;
            ActiveControl = txtKod;
            SetFooterNote("Kod hiçbir yerde saklanmaz; alan her okutmadan sonra hemen boşalır.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
