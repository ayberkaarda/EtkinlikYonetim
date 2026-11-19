using EtkinlikYonetim.Business.Exports;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Admin view of the participants of an event with their state; national ids are shown masked. The
    /// list of the chosen event can be saved as CSV or as a printable HTML page, without ticket codes.
    /// </summary>
    public partial class KatilimciGoruntule : Form
    {
        private const string CsvFilter = "CSV dosyası (*.csv)|*.csv";
        private const string HtmlFilter = "HTML sayfası (*.html)|*.html";

        private readonly IEventService _events;
        private readonly IParticipantService _participants;
        private readonly IParticipantExportService _exports;
        private readonly UserSession _session;
        private readonly RequestSequence _participantRequests = new();
        private readonly ExclusiveOperation _export = new();

        public KatilimciGoruntule(IEventService events, IParticipantService participants, IParticipantExportService exports, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(participants);
            ArgumentNullException.ThrowIfNull(exports);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _participants = participants;
            _exports = exports;
            _session = session;
            Prompts = new MessageBoxPrompts(this);
            SavePrompt = new SaveFileDialogPrompt(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

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
            if (cmbEtkinlikSec.SelectedItem is not EventSummary selected)
            {
                return;
            }

            await this.RunAsync(async () =>
            {
                var participants = await _participants.GetByEventAsync(_session, selected.Id);
                if (_participantRequests.IsLatest(request) && !IsDisposed)
                {
                    lstKatilimcilar.ReplaceItems(participants.Select(p => new ParticipantItem(p)));
                }
            });
        }

        private async void KatilimciGoruntule_Load(object? sender, EventArgs e) => await LoadAsync();

        private async void cmbEtkinlikSec_SelectedIndexChanged(object? sender, EventArgs e) => await LoadParticipantsAsync();

        private async void btnCsv_Click(object? sender, EventArgs e) => await ExportCsvAsync();

        private async void btnHtml_Click(object? sender, EventArgs e) => await ExportHtmlAsync();
    }
}
