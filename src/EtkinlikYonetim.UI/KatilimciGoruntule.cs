using System.Globalization;
using EtkinlikYonetim.Business.Exports;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Admin view of the participants of an event with their state; national ids are shown masked. The
    /// list of the chosen event can be saved as CSV or as a printable HTML page, without ticket codes.
    /// </summary>
    public sealed class KatilimciGoruntule : AppDialog
    {
        internal const string ChooseAnEvent = "Katılımcıları görmek için bir etkinlik seçin.";
        internal const string NoParticipants = "Bu etkinliğe kayıtlı katılımcı yok.";

        private const string CsvFilter = "CSV dosyası (*.csv)|*.csv";
        private const string HtmlFilter = "HTML sayfası (*.html)|*.html";

        private readonly IEventService _events;
        private readonly IParticipantService _participants;
        private readonly IParticipantExportService _exports;
        private readonly UserSession _session;
        private readonly RequestSequence _participantRequests = new();
        private readonly ExclusiveOperation _export = new();

        private readonly ThemedComboBox cmbEtkinlikSec = FormLayout.Picker(nameof(cmbEtkinlikSec), 280, "Etkinlik seçin");
        private readonly Label lblOzet = FormLayout.Text(nameof(lblOzet));
        private readonly ThemedListBox lstKatilimcilar = new() { Name = nameof(lstKatilimcilar) };
        private readonly ThemedButton btnCsv = FormLayout.Button(nameof(btnCsv), "CSV olarak kaydet", ButtonKind.Secondary, Glyphs.Download);
        private readonly ThemedButton btnHtml = FormLayout.Button(nameof(btnHtml), "Yazdırılabilir liste (HTML)", ButtonKind.Secondary, Glyphs.Document);

        public KatilimciGoruntule(IEventService events, IParticipantService participants, IParticipantExportService exports, UserSession session)
            : base("Katılımcı listesi", "Bir etkinliğin katılımcılarını ve giriş durumlarını görün; listeyi kaydedin.", Glyphs.List, new Size(900, 660), new Size(740, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(participants);
            ArgumentNullException.ThrowIfNull(exports);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _participants = participants;
            _exports = exports;
            _session = session;
            SavePrompt = new SaveFileDialogPrompt(this);
            BuildLayout();

            cmbEtkinlikSec.SelectedIndexChanged += async (_, _) => await LoadParticipantsAsync();
            btnCsv.Click += async (_, _) => await ExportCsvAsync();
            btnHtml.Click += async (_, _) => await ExportHtmlAsync();
            Load += async (_, _) => await LoadAsync();
        }

        /// <summary>The save dialog; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal ISaveFilePrompt SavePrompt { get; set; }

        /// <summary>Saves the list of the chosen event as CSV; ignored while an export is running.</summary>
        internal Task<bool> ExportCsvAsync() => ExportAsync(_exports.ExportCsvAsync, CsvFilter);

        /// <summary>Saves the list of the chosen event as a printable HTML page; ignored while an export is running.</summary>
        internal Task<bool> ExportHtmlAsync() => ExportAsync(_exports.ExportHtmlAsync, HtmlFilter);

        private Task<bool> ExportAsync(Func<UserSession, int, CancellationToken, Task<ExportFile>> export, string filter) =>
            this.RunExclusiveAsync(
                _export,
                async () =>
                {
                    if (cmbEtkinlikSec.SelectedItem is not EventSummary selected)
                    {
                        Prompts.Warn("Lütfen önce bir etkinlik seçin.");
                        return;
                    }

                    var file = await export(_session, selected.Id, CancellationToken.None);
                    if (SavePrompt.AskSavePath(file.SuggestedName, filter) is not { } path)
                    {
                        return;
                    }

                    await ExportFileWriter.WriteAsync(path, file.Content);
                    Prompts.Inform("Liste kaydedildi.");
                },
                btnCsv,
                btnHtml);

        /// <summary>Loads every event; selecting the first one lists its participants.</summary>
        internal Task LoadAsync() =>
            this.RunAsync(async () =>
            {
                var events = await _events.GetEventsAsync(_session);
                cmbEtkinlikSec.ReplaceItems(events);
                if (cmbEtkinlikSec.Items.Count > 0)
                {
                    cmbEtkinlikSec.SelectedIndex = 0;
                }
            });

        /// <summary>
        /// Lists the participants of the selected event. Selections can change faster than the answers
        /// arrive, so only the answer to the latest request is shown.
        /// </summary>
        internal async Task LoadParticipantsAsync()
        {
            var request = _participantRequests.Next();
            lstKatilimcilar.Items.Clear();
            lblOzet.Text = string.Empty;
            if (cmbEtkinlikSec.SelectedItem is not EventSummary selected)
            {
                lstKatilimcilar.IsLoading = false;
                lstKatilimcilar.EmptyText = ChooseAnEvent;
                return;
            }

            lstKatilimcilar.IsLoading = true;
            await this.RunAsync(async () =>
            {
                try
                {
                    var participants = await _participants.GetByEventAsync(_session, selected.Id);
                    if (_participantRequests.IsLatest(request) && !IsDisposed)
                    {
                        var items = participants.Select(p => new ParticipantItem(p)).ToList();
                        lstKatilimcilar.ReplaceItems(items);
                        lstKatilimcilar.EmptyText = NoParticipants;
                        lblOzet.Text = string.Create(
                            CultureInfo.InvariantCulture,
                            $"{items.Count} katılımcı · {items.Count(item => item.CheckedIn)} giriş yaptı");
                    }
                }
                finally
                {
                    if (_participantRequests.IsLatest(request) && !IsDisposed)
                    {
                        lstKatilimcilar.IsLoading = false;
                    }
                }
            });
        }

        private void BuildLayout()
        {
            Name = nameof(KatilimciGoruntule);

            lblOzet.Font = Theme.Fonts.BodyStrong;
            lblOzet.ForeColor = Theme.Current.TextSecondary;
            lblOzet.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            var eventField = FormLayout.Field("&Etkinlik", cmbEtkinlikSec);
            var summary = FormLayout.Field("Özet", lblOzet);
            var top = FormLayout.Card("filterCard", null, FormLayout.FieldRow("eventRow", (eventField, 60), (summary, 40)), fill: false);

            lstKatilimcilar.Describe = item => item is ParticipantItem participant ? participant.Row() : new ListRow(item.ToString() ?? string.Empty);
            lstKatilimcilar.EmptyText = ChooseAnEvent;
            btnCsv.Margin = new Padding(0, 0, Spacing.S, 0);
            btnHtml.Margin = Padding.Empty;
            var actions = FormLayout.Bar("exportActions", (btnCsv, false), (btnHtml, false), (FormLayout.Spacer(), true));
            actions.Margin = new Padding(0, Spacing.M, 0, 0);
            var card = FormLayout.Card("participantsCard", "Katılımcılar", FormLayout.Page("participantsPage", (lstKatilimcilar, true), (actions, false)));
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("listingPage", (top, false), (card, true)));

            SetFooterNote("T.C. kimlik no maskelenir; bilet kodları listede ve dışa aktarılan dosyada yer almaz.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
