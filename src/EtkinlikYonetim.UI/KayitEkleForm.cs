using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Registers a participant for an event and issues the ticket, or a place on the waiting list of a full
    /// event; lists the registrations the signed-in account may see, shows their tickets and cancels them.
    /// </summary>
    public sealed class KayitEkleForm : AppDialog
    {
        private readonly IEventService _events;
        private readonly IRegistrationService _registrations;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _register = new();
        private readonly ExclusiveOperation _cancel = new();
        private readonly ExclusiveOperation _ticket = new();
        private readonly RequestSequence _listRequests = new();

        private readonly ThemedComboBox cmbEtkinlikler = FormLayout.Picker(nameof(cmbEtkinlikler), 240, "Etkinlik seçin");
        private readonly TextBox txtKatilimciTC = new() { Name = nameof(txtKatilimciTC), MaxLength = NationalIdNumber.Length };
        private readonly ThemedButton btnKayit = FormLayout.Button(nameof(btnKayit), "Kaydet", ButtonKind.Primary, Glyphs.Add);
        private readonly ThemedListBox lstKayitlar = new() { Name = nameof(lstKayitlar) };
        private readonly ThemedButton btnBiletGoster = FormLayout.Button(nameof(btnBiletGoster), "Bileti göster", ButtonKind.Secondary, Glyphs.QrCode);
        private readonly ThemedButton btnKayitIptal = FormLayout.Button(nameof(btnKayitIptal), "Kaydı iptal et", ButtonKind.Danger, Glyphs.Cancel);

        public KayitEkleForm(IEventService events, IRegistrationService registrations, UserSession session)
            : base("Kayıtlar ve biletler", "Katılımcıyı etkinliğe kaydedin, biletini gösterin ya da kaydı iptal edin.", Glyphs.QrCode, new Size(980, 600), new Size(820, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(registrations);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _registrations = registrations;
            _session = session;
            txtKatilimciTC.AcceptDigitsOnly();
            BuildLayout();

            btnKayit.Click += async (_, _) => await RegisterAsync();
            btnKayitIptal.Click += async (_, _) => await CancelAsync();
            btnBiletGoster.Click += async (_, _) => await ShowTicketAsync();
            lstKayitlar.DoubleClick += async (_, _) => await ShowTicketAsync();
            Load += async (_, _) => await LoadAsync();
        }

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
                    _cancel.FocusAfterUnlock(lstKayitlar);
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
                ShowRegistrations(registrations, selected.Registration.Id);
            }

            if (current is null)
            {
                Prompts.Warn("Bu kayıt artık listenizde değil; liste yenilendi.");
                return null;
            }

            if (current.Status != RegistrationStatus.Active)
            {
                Prompts.Warn("Bu kaydın bileti yok: katılımcı bekleme listesinde ya da kayıt iptal edilmiş.");
                return null;
            }

            if (current.TicketCode is not { Length: > 0 })
            {
                Prompts.Warn("Bilet yalnızca katılımcıyı ekleyen hesapta gösterilir.");
                return null;
            }

            return new BiletForm(current);
        }

        private Task<bool> ShowTicketAsync() =>
            this.RunExclusiveAsync(
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

        private async Task RefreshRegistrationsAsync()
        {
            var request = _listRequests.Next();
            lstKayitlar.IsLoading = true;
            try
            {
                var registrations = await _registrations.GetMyRegistrationsAsync(_session);
                if (_listRequests.IsLatest(request) && !IsDisposed)
                {
                    ShowRegistrations(registrations, (lstKayitlar.SelectedItem as RegistrationItem)?.Registration.Id);
                }
            }
            finally
            {
                if (_listRequests.IsLatest(request) && !IsDisposed)
                {
                    lstKayitlar.IsLoading = false;
                }
            }
        }

        /// <summary>Replaces the list and keeps the registration with <paramref name="selectedId"/> selected.</summary>
        private void ShowRegistrations(IReadOnlyList<RegistrationDetails> registrations, int? selectedId)
        {
            lstKayitlar.ReplaceItems(registrations.Select(r => new RegistrationItem(r)));
            lstKayitlar.SelectedIndex = registrations.ToList().FindIndex(r => r.Id == selectedId);
        }

        private void BuildLayout()
        {
            Name = nameof(KayitEkleForm);

            // A new registration: the event and the participant's national id.
            var eventField = FormLayout.Field("&Etkinlik", cmbEtkinlikler, "Yalnızca kayda açık etkinlikler listelenir.");
            var nationalId = FormLayout.Field("Katılımcının &T.C. kimlik no", FormLayout.Input(txtKatilimciTC), "Katılımcı önce Katılımcı ekle ekranında eklenmiş olmalı.");
            eventField.Margin = nationalId.Margin = new Padding(0, 0, 0, Spacing.M);
            btnKayit.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            btnKayit.Margin = Padding.Empty;
            var newCard = FormLayout.Card("newRegistrationCard", "Yeni kayıt", FormLayout.Stack("newRegistrationForm", eventField, nationalId, btnKayit), fill: false);

            var waiting = FormLayout.Text(
                "lblBekleme",
                "Etkinlik doluysa katılımcı bekleme listesine alınır. Bir kayıt iptal edildiğinde sıradaki kişi kendiliğinden kayda geçer ve bileti oluşur.");
            var note = FormLayout.Note("waitingNote", Glyphs.Clock, waiting);
            note.Margin = Padding.Empty;
            var left = FormLayout.Page("leftColumn", (newCard, false), (note, false));

            // The account's registrations with the actions on the selected one.
            lstKayitlar.Describe = item => item is RegistrationItem registration ? registration.Row() : new ListRow(item.ToString() ?? string.Empty);
            lstKayitlar.EmptyText = "Henüz kayıt yok. Soldaki formla bir katılımcıyı etkinliğe kaydedin.";
            btnBiletGoster.Margin = btnKayitIptal.Margin = Padding.Empty;
            var actions = FormLayout.Bar("registrationActions", (btnBiletGoster, false), (FormLayout.Spacer(), true), (btnKayitIptal, false));
            actions.Margin = new Padding(0, Spacing.M, 0, 0);
            var listCard = FormLayout.Card("registrationsCard", "Kayıtlarım", FormLayout.Page("registrationsPage", (lstKayitlar, true), (actions, false)));
            listCard.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Columns("layout", left, 42, listCard));

            AcceptButton = btnKayit;
            SetFooterNote("Bilet kodu yalnızca katılımcıyı ekleyen hesapta görünür; kimseyle paylaşmayın.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
