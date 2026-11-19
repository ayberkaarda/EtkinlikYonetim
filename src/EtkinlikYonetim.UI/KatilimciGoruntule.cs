using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>Admin view of the participants of an event; national ids are shown masked.</summary>
    public partial class KatilimciGoruntule : Form
    {
        private readonly IEventService _events;
        private readonly IParticipantService _participants;
        private readonly UserSession _session;
        private readonly RequestSequence _participantRequests = new();

        public KatilimciGoruntule(IEventService events, IParticipantService participants, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(participants);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _participants = participants;
            _session = session;

            InitializeComponent();
            Icon = AppAssets.Icon;
        }

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
    }
}
