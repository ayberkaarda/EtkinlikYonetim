using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// The entrance for admins: choose the event, then type or scan ticket codes one after the other. A
    /// scanner that types the code and Enter works as is. The screen stays open after every answer, the
    /// code field is emptied at once and the code is kept nowhere.
    /// </summary>
    public partial class BiletKontrolForm : Form
    {
        internal const string CheckedInText = "GİRİŞ YAPILDI";
        internal const string AlreadyUsedText = "ZATEN KULLANILDI - Bu bilet daha önce okutuldu.";
        internal const string RejectedText = "GEÇERSİZ BİLET - Bu etkinlik için geçerli bir bilet değil.";
        internal const string NotEventDayText = "ETKİNLİK GÜNÜ DEĞİL - Bilet yalnızca etkinlik günü okutulabilir; bilet kullanılmadı.";

        private readonly IEventService _events;
        private readonly ICheckInService _checkIns;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _check = new();
        private readonly RequestSequence _countRequests = new();
        private bool _checkQueued;

        public BiletKontrolForm(IEventService events, ICheckInService checkIns, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(checkIns);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _checkIns = checkIns;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            Text = AppText.Title + " - " + Text;
            txtKod.MaxLength = 64;
            AcceptButton = btnKontrol;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

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
            lblSonuc.Text = result.Outcome switch
            {
                // The name lets the staff compare the ticket with an identity document.
                CheckInOutcome.CheckedIn when result.ParticipantName is { Length: > 0 } name => CheckedInText + " - " + name,
                CheckInOutcome.CheckedIn => CheckedInText,
                CheckInOutcome.AlreadyUsed => AlreadyUsedText,
                CheckInOutcome.NotEventDay => NotEventDayText,
                _ => RejectedText,
            };

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
            lblSonuc.Text = string.Empty;
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

        private void ShowCount(CheckInCount count) =>
            lblSayac.Text = string.Create(CultureInfo.InvariantCulture, $"Giriş yapan: {count.CheckedIn} / {count.Seats}");

        private async void BiletKontrolForm_Load(object? sender, EventArgs e) => await LoadAsync();

        private async void cmbEtkinlik_SelectedIndexChanged(object? sender, EventArgs e) => await LoadCountAsync();

        private async void btnKontrol_Click(object? sender, EventArgs e) => await CheckInAsync();
    }
}