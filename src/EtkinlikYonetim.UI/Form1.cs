using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Admin panel: creates events and opens the participant, registration, listing and account forms.
    /// When the database no longer accepts the admin's session the panel closes and the sign-in returns.
    /// </summary>
    public partial class Form1 : Form, ISessionPanel
    {
        private readonly IEventService _events;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _create = new();

        public Form1(IEventService events, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(events);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _events = events;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            txtEtkinlikAd.MaxLength = InputRules.EventNameMaxLength;
            numKapasite.Minimum = InputRules.MinCapacity;
            numKapasite.Maximum = InputRules.MaxCapacity;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

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
                },
                btnEtkinlikEkle);

        private void Form1_Load(object? sender, EventArgs e) => dtpTarih.MinDate = DateTime.Today;

        private async void btnEtkinlikEkle_Click(object? sender, EventArgs e) => await CreateEventAsync();

        private void btnKatilimciEkle_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<KatilimciEkleForm>(_session);
            form.ShowDialog(this);
        }

        private void btnKayit_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<KayitEkleForm>(_session);
            form.ShowDialog(this);
        }

        private void btnKatilimciGoruntule_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<KatilimciGoruntule>(_session);
            form.ShowDialog(this);
        }

        /// <inheritdoc/>
        public bool SignedOut { get; private set; }

        /// <inheritdoc/>
        public void EndSession()
        {
            SignedOut = true;
            SessionEnd.Close(this);
        }

        private void btnEtkinlikYonet_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<EtkinlikYonetForm>(_session);
            form.ShowDialog(this);
        }

        private void btnBiletKontrol_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<BiletKontrolForm>(_session);
            form.ShowDialog(this);
        }

        private void btnKullanicilar_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<KullaniciYonetForm>(_session);
            form.ShowDialog(this);
        }

        private void btnAsagial_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;

        private void btnkapat_Click(object? sender, EventArgs e) => Close();
    }
}
