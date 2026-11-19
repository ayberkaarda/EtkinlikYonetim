using System.Globalization;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Event management for admins: changes the date, capacity and description of an event and cancels
    /// it. Past and cancelled events are shown but cannot be changed; "past" is decided by the database's day,
    /// loaded with the list, never by this computer's clock. The services decide every rule again.
    /// </summary>
    public sealed class EtkinlikYonetForm : AppDialog
    {
        internal const string NothingSelected = "Düzenlemek için soldan bir etkinlik seçin.";

        private readonly IEventService _events;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();
        private readonly RequestSequence _listRequests = new();
        private DateOnly? _today;
        private bool _binding;

        private readonly ThemedListBox lstEtkinlikler = new() { Name = nameof(lstEtkinlikler) };
        private readonly Label lblSecili = FormLayout.Text(nameof(lblSecili));
        private readonly Label lblDoluluk = FormLayout.Text(nameof(lblDoluluk));
        private readonly DatePicker dtpTarih = new() { Name = nameof(dtpTarih), Format = DateTimePickerFormat.Short, Width = 130 };
        private readonly NumericUpDown numKapasite = new() { Name = nameof(numKapasite), Minimum = InputRules.MinCapacity, Maximum = InputRules.MaxCapacity };
        private readonly TextBox txtAciklama = new() { Name = nameof(txtAciklama), Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = InputRules.EventDescriptionMaxLength };
        private readonly ThemedButton btnKaydet = FormLayout.Button(nameof(btnKaydet), "Değişiklikleri kaydet", ButtonKind.Primary, Glyphs.Save);
        private readonly ThemedButton btnIptalEt = FormLayout.Button(nameof(btnIptalEt), "Etkinliği iptal et", ButtonKind.Danger, Glyphs.Cancel);

        public EtkinlikYonetForm(IEventService events, UserSession session)
            : base("Etkinlik yönetimi", "Tarihi, kapasiteyi ve açıklamayı değiştirin ya da etkinliği iptal edin.", Glyphs.Calendar, new Size(980, 600), new Size(820, 520), modal: true)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _session = session;
            BuildLayout();
            ShowSelection(null);

            lstEtkinlikler.SelectedIndexChanged += (_, _) =>
            {
                if (!_binding)
                {
                    ShowSelection(Selected);
                }
            };
            btnKaydet.Click += async (_, _) => await SaveAsync();
            btnIptalEt.Click += async (_, _) => await CancelEventAsync();
            Load += async (_, _) => await LoadAsync();
        }

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

                    try
                    {
                        // The event as it was loaded goes along: the service refuses to write over a change
                        // another admin made in the meantime.
                        await _events.UpdateAsync(_session, selected, dtpTarih.Value.Date, (int)numKapasite.Value, txtAciklama.Text);
                    }
                    catch (StaleDataException)
                    {
                        // Show the event as it is now before the message, so the change can be made again on it.
                        await RefreshAsync();
                        throw;
                    }

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
            lstEtkinlikler.IsLoading = true;
            IReadOnlyList<EventSummary> events;
            DateOnly today;
            try
            {
                // The database's day decides which events have passed, as it does in the services.
                today = await _events.GetTodayAsync(_session);
                events = await _events.GetEventsAsync(_session);
            }
            finally
            {
                if (_listRequests.IsLatest(request) && !IsDisposed)
                {
                    lstEtkinlikler.IsLoading = false;
                }
            }

            if (!_listRequests.IsLatest(request) || IsDisposed)
            {
                return;
            }

            _today = today;

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

        /// <summary>
        /// Shows the event's fields; only an event that is neither past (by the database's day) nor cancelled
        /// can be changed. Until the day is known nothing can be changed.
        /// </summary>
        private void ShowSelection(EventSummary? selected)
        {
            var today = _today;
            var editable = selected is { Cancelled: false } && today is { } day && selected.Date >= day;

            _binding = true;
            try
            {
                if (selected is null)
                {
                    lblSecili.Text = NothingSelected;
                    lblDoluluk.Text = string.Empty;
                    txtAciklama.Clear();
                }
                else
                {
                    lblSecili.Text = selected.ToString()
                        + (selected.Cancelled ? string.Empty : today is { } past && selected.Date < past ? " (Geçmiş etkinlik, düzenlenemez)" : string.Empty);
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

        private void BuildLayout()
        {
            Name = nameof(EtkinlikYonetForm);

            // Every event, with its seats and its state.
            lstEtkinlikler.Describe = item => item is EventSummary summary ? EventRows.Describe(summary, _today) : new ListRow(item.ToString() ?? string.Empty);
            lstEtkinlikler.EmptyText = "Henüz etkinlik yok. Yönetim panelindeki Yeni etkinlik kartıyla ekleyin.";
            var listCard = FormLayout.Card("eventsCard", "Etkinlikler", lstEtkinlikler);

            // The selected event: what it is, how full it is, and the fields that can change.
            lblSecili.Font = Theme.Fonts.Subtitle;
            lblSecili.ForeColor = Theme.Current.Text;
            lblSecili.Margin = Padding.Empty;
            lblDoluluk.ForeColor = Theme.Current.TextSecondary;
            lblDoluluk.Margin = new Padding(0, Spacing.XS, 0, Spacing.L);
            var when = FormLayout.FieldRow(
                "eventRow",
                (FormLayout.Field("&Tarih", FormLayout.Date(dtpTarih)), 50),
                (FormLayout.Field("&Kapasite", FormLayout.Number(numKapasite), "Kayıtlı kişi sayısının altına inemez."), 50));
            var description = FormLayout.Field("&Açıklama", FormLayout.Input(txtAciklama, Sizes.NoteHeight + Spacing.XL));
            description.Margin = new Padding(0, 0, 0, Spacing.M);
            btnKaydet.Margin = btnIptalEt.Margin = Padding.Empty;
            var actions = FormLayout.Bar("eventActions", (btnKaydet, false), (FormLayout.Spacer(), true), (btnIptalEt, false));
            var editor = FormLayout.Stack("eventEditor", lblSecili, lblDoluluk, when, description, actions);
            var editCard = FormLayout.Card("editCard", "Seçili etkinlik", FormLayout.Page("editPage", (editor, false)));
            Content.Controls.Add(FormLayout.Columns("layout", listCard, 46, editCard));

            SetFooterNote("Geçmiş ve iptal edilmiş etkinlikler değiştirilemez; iptal geri alınamaz.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
