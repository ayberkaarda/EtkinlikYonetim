using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Event management for admins: changes the date, capacity and description of an event and cancels
    /// it. Past and cancelled events are shown but cannot be changed; the services decide every rule again.
    /// </summary>
    public partial class EtkinlikYonetForm : Form
    {
        private readonly IEventService _events;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();
        private readonly RequestSequence _listRequests = new();
        private readonly TimeProvider _time;
        private bool _binding;

        public EtkinlikYonetForm(IEventService events, UserSession session, TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(time);

            _events = events;
            _session = session;
            _time = time;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            Text = AppText.Title + " - " + Text;
            numKapasite.Minimum = InputRules.MinCapacity;
            numKapasite.Maximum = InputRules.MaxCapacity;
            txtAciklama.MaxLength = InputRules.EventDescriptionMaxLength;
            ShowSelection(null);
        }

        /// <summary>Confirmation and information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>The event whose fields are shown, or <see langword="null"/>.</summary>
        internal EventSummary? Selected => lstEtkinlikler.SelectedItem as EventSummary;

        /// <summary>Loads every event; the event selected before stays selected.</summary>
        internal Task LoadAsync() => this.RunAsync(RefreshAsync);

        /// <summary>Stores the shown date, capacity and description; ignored while a change is running.</summary>
        internal Task<bool> SaveAsync() =>
            RunChangeAsync(
                async () =>
                {
                    if (Selected is not { } selected)
                    {
                        Prompts.Warn("Lütfen bir etkinlik seçin.");
                        return;
                    }

                    await _events.UpdateAsync(_session, selected.Id, dtpTarih.Value.Date, (int)numKapasite.Value, txtAciklama.Text);
                    Prompts.Inform("Etkinlik güncellendi.");
                    _change.FocusAfterUnlock(lstEtkinlikler);
                    await RefreshAsync();
                });

        /// <summary>Cancels the selected event after a confirmation; ignored while a change is running.</summary>
        internal Task<bool> CancelEventAsync() =>
            RunChangeAsync(
                async () =>
                {
                    if (Selected is not { } selected)
                    {
                        Prompts.Warn("Lütfen bir etkinlik seçin.");
                        return;
                    }

                    if (!Prompts.Confirm("Seçilen etkinlik iptal edilsin mi? Tüm kayıtlar ve bekleme listesi kapanır, biletler geçersiz olur. Bu işlem geri alınamaz."))
                    {
                        return;
                    }

                    await _events.CancelAsync(_session, selected.Id);
                    Prompts.Inform("Etkinlik iptal edildi.");
                    _change.FocusAfterUnlock(lstEtkinlikler);
                    await RefreshAsync();
                });

        /// <summary>
        /// Runs a change with both change buttons locked. The lock gives the buttons back their state of
        /// before the change, so the state of the event as it is now is applied once the change has ended.
        /// </summary>
        private async Task<bool> RunChangeAsync(Func<Task> change)
        {
            var ran = await this.RunExclusiveAsync(_change, change, btnKaydet, btnIptalEt);
            if (ran && !IsDisposed)
            {
                ShowSelection(Selected);
            }

            return ran;
        }

        private async Task RefreshAsync()
        {
            var request = _listRequests.Next();
            var selectedId = Selected?.Id;
            var events = await _events.GetEventsAsync(_session);
            if (!_listRequests.IsLatest(request) || IsDisposed)
            {
                return;
            }

            // Restoring the selection is not a choice of the user: the fields are bound once, below.
            _binding = true;
            try
            {
                lstEtkinlikler.ReplaceItems(events);
                lstEtkinlikler.SelectedIndex = events.ToList().FindIndex(e => e.Id == selectedId);
            }
            finally
            {
                _binding = false;
            }

            ShowSelection(Selected);
        }

        /// <summary>Shows the event's fields; only an event that is neither past nor cancelled can be changed.</summary>
        private void ShowSelection(EventSummary? selected)
        {
            var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
            var editable = selected is { Cancelled: false } && selected.Date >= today;

            _binding = true;
            try
            {
                if (selected is null)
                {
                    lblSecili.Text = "Düzenlemek için soldan bir etkinlik seçin.";
                    lblDoluluk.Text = string.Empty;
                    txtAciklama.Clear();
                }
                else
                {
                    lblSecili.Text = selected.ToString()
                        + (selected.Cancelled ? string.Empty : selected.Date < today ? " (Geçmiş etkinlik, düzenlenemez)" : string.Empty);
                    lblDoluluk.Text = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Kayıtlı: {selected.RegisteredCount} / {selected.Capacity}, bekleme listesi: {selected.WaitingCount}");
                    dtpTarih.Value = selected.Date.ToDateTime(TimeOnly.MinValue);
                    numKapasite.Maximum = Math.Max(InputRules.MaxCapacity, selected.Capacity);
                    numKapasite.Value = Math.Max(InputRules.MinCapacity, selected.Capacity);
                    txtAciklama.Text = selected.Description ?? string.Empty;
                }
            }
            finally
            {
                _binding = false;
            }

            dtpTarih.Enabled = editable;
            numKapasite.Enabled = editable;
            txtAciklama.ReadOnly = !editable;
            btnKaydet.Enabled = editable;
            btnIptalEt.Enabled = editable;
        }

        private async void EtkinlikYonetForm_Load(object? sender, EventArgs e) => await LoadAsync();

        private void lstEtkinlikler_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (!_binding)
            {
                ShowSelection(Selected);
            }
        }

        private async void btnKaydet_Click(object? sender, EventArgs e) => await SaveAsync();

        private async void btnIptalEt_Click(object? sender, EventArgs e) => await CancelEventAsync();
    }
}
