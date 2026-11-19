using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Registers a participant for an event and issues the ticket; lists the registrations the
    /// signed-in account may see and cancels them.
    /// </summary>
    public partial class KayitEkleForm : Form
    {
        private readonly IEventService _events;
        private readonly IRegistrationService _registrations;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _register = new();
        private readonly ExclusiveOperation _cancel = new();
        private readonly ExclusiveOperation _ticket = new();
        private readonly RequestSequence _listRequests = new();

        public KayitEkleForm(IEventService events, IRegistrationService registrations, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(registrations);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _registrations = registrations;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            txtKatilimciTC.AcceptDigitsOnly();
            txtKatilimciTC.MaxLength = NationalIdNumber.Length;
        }

        /// <summary>Confirmation and information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Loads the events that accept registrations and the account's registrations.</summary>
        internal Task LoadAsync() =>
            this.RunAsync(async () =>
            {
                var events = await _events.GetOpenEventsAsync(_session);
                cmbEtkinlikler.ReplaceItems(events);
                if (cmbEtkinlikler.Items.Count > 0)
                {
                    cmbEtkinlikler.SelectedIndex = 0;
                }

                await RefreshRegistrationsAsync();
            });

        /// <summary>Registers the participant; ignored while a previous registration is still running.</summary>
        internal Task<bool> RegisterAsync() =>
            this.RunExclusiveAsync(
                _register,
                async () =>
                {
                    var eventId = cmbEtkinlikler.SelectedItem is EventSummary selected ? selected.Id : 0;
                    var receipt = await _registrations.RegisterAsync(_session, eventId, txtKatilimciTC.Text);
                    Prompts.Inform(receipt.QueuePosition is int position
                        ? $"Etkinlik dolu. Katılımcı bekleme listesine alındı (sıra: {position}). Bir kayıt iptal edildiğinde sıradaki katılımcı kendiliğinden kayda geçer ve bileti oluşturulur."
                        : $"Kayıt tamamlandı. Bilet kodu: {receipt.TicketCode}");

                    txtKatilimciTC.Clear();
                    if (cmbEtkinlikler.Items.Count > 0)
                    {
                        cmbEtkinlikler.SelectedIndex = 0;
                    }

                    _register.FocusAfterUnlock(txtKatilimciTC);
                    await RefreshRegistrationsAsync();
                },
                btnKayit);

        /// <summary>Cancels the selected registration; ignored while a previous cancellation is still running.</summary>
        internal Task<bool> CancelAsync() =>
            this.RunExclusiveAsync(
                _cancel,
                async () =>
                {
                    if (lstKayitlar.SelectedItem is not RegistrationItem item)
                    {
                        Prompts.Warn("Lütfen iptal etmek istediğiniz kaydı seçin.");
                        return;
                    }

                    if (!Prompts.Confirm("Seçilen kaydı iptal etmek istediğinize emin misiniz?"))
                    {
                        return;
                    }

                    await _registrations.CancelAsync(_session, item.Registration.Id);
                    Prompts.Inform("Kayıt iptal edildi.");
                    await RefreshRegistrationsAsync();
                },
                btnKayitIptal);

        /// <summary>
        /// The ticket window of the selected registration, or <see langword="null"/> after telling why there is
        /// none (nothing selected, a waiting place or a cancelled registration). The registration is read again
        /// first: the list may be older than a cancellation by an admin or of the event, or a move up from the
        /// waiting list, and a cancelled ticket must not be shown as valid. The caller shows and disposes it.
        /// </summary>
        internal async Task<BiletForm?> CreateTicketViewAsync()
        {
            if (lstKayitlar.SelectedItem is not RegistrationItem selected)
            {
                Prompts.Warn("Lütfen biletini görmek istediğiniz kaydı seçin.");
                return null;
            }

            var request = _listRequests.Next();
            var registrations = await _registrations.GetMyRegistrationsAsync(_session);
            if (IsDisposed)
            {
                return null;
            }

            var current = registrations.FirstOrDefault(r => r.Id == selected.Registration.Id);
            if (_listRequests.IsLatest(request) && current != selected.Registration)
            {
                lstKayitlar.ReplaceItems(registrations.Select(r => new RegistrationItem(r)));
                lstKayitlar.SelectedIndex = registrations.ToList().FindIndex(r => r.Id == selected.Registration.Id);
            }

            if (current is null)
            {
                Prompts.Warn("Bu kayıt artık listenizde değil; liste yenilendi.");
                return null;
            }

            var item = new RegistrationItem(current);
            if (item.Registration.Status != RegistrationStatus.Active)
            {
                Prompts.Warn("Bu kaydın bileti yok: katılımcı bekleme listesinde ya da kayıt iptal edilmiş.");
                return null;
            }

            if (item.Registration.TicketCode is not { Length: > 0 })
            {
                Prompts.Warn("Bilet yalnızca katılımcıyı ekleyen hesapta gösterilir.");
                return null;
            }

            return new BiletForm(item.Registration);
        }

        private async Task RefreshRegistrationsAsync()
        {
            var request = _listRequests.Next();
            var registrations = await _registrations.GetMyRegistrationsAsync(_session);
            if (_listRequests.IsLatest(request) && !IsDisposed)
            {
                lstKayitlar.ReplaceItems(registrations.Select(r => new RegistrationItem(r)));
            }
        }

        private async void KayitEkleForm_Load(object? sender, EventArgs e) => await LoadAsync();

        private async void btnKayit_Click(object? sender, EventArgs e) => await RegisterAsync();

        private async void btnKayitIptal_Click(object? sender, EventArgs e) => await CancelAsync();

        private async void btnBiletGoster_Click(object? sender, EventArgs e) =>
            await this.RunExclusiveAsync(
                _ticket,
                async () =>
                {
                    using var ticket = await CreateTicketViewAsync();
                    if (ticket is not null && !IsDisposed)
                    {
                        ticket.ShowDialog(this);
                    }
                },
                btnBiletGoster);

        private void btnkapat_Click(object? sender, EventArgs e) => Close();

        private void btnAsagial_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;
    }
}
