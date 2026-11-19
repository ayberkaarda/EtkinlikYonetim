using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Adds a participant owned by the signed-in account; the main screen of users and a dialog of the
    /// admin panel. As a user's main screen it closes when the database no longer accepts the session.
    /// </summary>
    public partial class KatilimciEkleForm : Form, ISessionPanel
    {
        /// <inheritdoc/>
        public bool SignedOut { get; private set; }

        /// <inheritdoc/>
        public void EndSession()
        {
            SignedOut = true;
            SessionEnd.Close(this);
        }

        private readonly IParticipantService _participants;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _save = new();

        public KatilimciEkleForm(IParticipantService participants, IFormFactory forms, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(participants);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _participants = participants;
            _forms = forms;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            txtTC.AcceptDigitsOnly();
            txtTC.MaxLength = NationalIdNumber.Length;
            txtAdSoyad.AcceptLettersOnly(allowSpaces: true);
            txtAdSoyad.MaxLength = InputRules.PersonNameMaxLength;
            txtEmail.MaxLength = InputRules.EmailMaxLength;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Adds the participant; ignored while a previous save is still running.</summary>
        internal Task<bool> SaveAsync() =>
            this.RunExclusiveAsync(
                _save,
                async () =>
                {
                    await _participants.AddAsync(_session, txtTC.Text, txtAdSoyad.Text, txtEmail.Text);
                    Prompts.Inform("Katılımcı başarıyla eklendi.");

                    txtAdSoyad.Clear();
                    txtTC.Clear();
                    txtEmail.Clear();
                    _save.FocusAfterUnlock(txtTC);
                },
                btnKatilimciEkle);

        private async void btnKatilimciEkle_Click(object? sender, EventArgs e) => await SaveAsync();

        private void txtEmail_Leave(object? sender, EventArgs e)
        {
            if (InputRules.IsValidEmail(txtEmail.Text.Trim()))
            {
                lblUyari.Text = "Geçerli E-posta Adresi!";
                lblUyari.ForeColor = Color.DarkGreen;
            }
            else
            {
                lblUyari.Text = "Geçersiz e-posta adresi!";
                lblUyari.ForeColor = Color.Red;
            }
        }

        private void button1_Click(object? sender, EventArgs e)
        {
            using var form = _forms.Create<KayitEkleForm>(_session);
            form.ShowDialog(this);
        }
    }
}
