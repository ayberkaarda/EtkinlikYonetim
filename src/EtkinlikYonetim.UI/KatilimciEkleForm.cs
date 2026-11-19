using System.Globalization;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Adds a participant owned by the signed-in account; the main screen of users and a dialog of the
    /// admin panel. As a user's main screen it closes when the database no longer accepts the session or the
    /// user signs out.
    /// </summary>
    public sealed class KatilimciEkleForm : AppDialog, ISessionPanel
    {
        internal const string EmailValid = "Geçerli e-posta adresi.";
        internal const string EmailInvalid = "Geçersiz e-posta adresi.";

        private readonly IParticipantService _participants;
        private readonly IFormFactory _forms;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _save = new();

        private readonly TextBox txtTC = new() { Name = nameof(txtTC), MaxLength = NationalIdNumber.Length };
        private readonly TextBox txtAdSoyad = new() { Name = nameof(txtAdSoyad), MaxLength = InputRules.PersonNameMaxLength };
        private readonly TextBox txtEmail = new() { Name = nameof(txtEmail), MaxLength = InputRules.EmailMaxLength };
        private readonly Label lblUyari = FormLayout.Hint(string.Empty);
        private readonly ThemedButton btnKatilimciEkle = FormLayout.Button(nameof(btnKatilimciEkle), "Katılımcıyı ekle", ButtonKind.Primary, Glyphs.AddPerson);
        private readonly ThemedButton btnEtkinlikSecim = FormLayout.Button(nameof(btnEtkinlikSecim), "Kayıtlar ve biletler", ButtonKind.Secondary, Glyphs.QrCode);

        public KatilimciEkleForm(IParticipantService participants, IFormFactory forms, UserSession session)
            : base(
                "Katılımcı ekle",
                "Etkinliğe kaydedeceğiniz kişiyi listenize ekleyin.",
                Glyphs.AddPerson,
                new Size(640, 520),
                new Size(520, 460),
                modal: session?.IsAdmin == true)
        {
            ArgumentNullException.ThrowIfNull(participants);
            ArgumentNullException.ThrowIfNull(forms);
            ArgumentNullException.ThrowIfNull(session);

            _participants = participants;
            _forms = forms;
            _session = session;
            txtTC.AcceptDigitsOnly();
            txtAdSoyad.AcceptLettersOnly(allowSpaces: true);
            BuildLayout();

            btnKatilimciEkle.Click += async (_, _) => await SaveAsync();
            btnEtkinlikSecim.Click += (_, _) => OpenRegistrations();
            txtEmail.Leave += (_, _) => ShowEmailCheck();
        }

        /// <inheritdoc/>
        public bool SignedOut { get; private set; }

        /// <inheritdoc/>
        public void EndSession()
        {
            SignedOut = true;
            SessionEnd.Close(this);
        }

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
                    lblUyari.Text = string.Empty;
                    _save.FocusAfterUnlock(txtTC);
                },
                btnKatilimciEkle);

        /// <summary>Tells right after the field is left whether the e-mail address has a valid form; the service checks again.</summary>
        internal void ShowEmailCheck()
        {
            var email = txtEmail.Text.Trim();
            if (email.Length == 0)
            {
                lblUyari.Text = string.Empty;
                return;
            }

            var valid = InputRules.IsValidEmail(email);
            lblUyari.Text = valid ? EmailValid : EmailInvalid;
            lblUyari.ForeColor = valid ? Theme.Current.Success : Theme.Current.Danger;
        }

        private void OpenRegistrations()
        {
            using var form = _forms.Create<KayitEkleForm>(_session);
            form.ShowDialog(this);
        }

        private void BuildLayout()
        {
            Name = nameof(KatilimciEkleForm);
            var user = FormLayout.Text("lblOturum", _session.UserName + (_session.IsAdmin ? " · Yönetici" : " · Kullanıcı"));
            user.ForeColor = Theme.Current.TextSecondary;
            SetHeaderAside(user);

            var nationalId = FormLayout.Field(
                "&T.C. kimlik no",
                FormLayout.Input(txtTC),
                string.Create(CultureInfo.CurrentCulture, $"{NationalIdNumber.Length} haneli numara; listelerde maskelenmiş görünür."));
            var name = FormLayout.Field("&Ad soyad", FormLayout.Input(txtAdSoyad));
            var email = FormLayout.Field("&E-posta", FormLayout.Input(txtEmail));
            lblUyari.Name = nameof(lblUyari);
            lblUyari.Font = Theme.Fonts.Caption;
            email.Controls.Add(lblUyari);
            nationalId.Margin = new Padding(0, 0, 0, Spacing.M);
            var person = FormLayout.FieldRow("personRow", (name, 50), (email, 50));
            btnKatilimciEkle.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            btnKatilimciEkle.Margin = new Padding(0, Spacing.XS, 0, 0);
            var card = FormLayout.Card("participantCard", "Yeni katılımcı", FormLayout.Stack("participantForm", nationalId, person, btnKatilimciEkle), fill: false);

            var next = FormLayout.Text("lblSonrakiAdim", "Sonraki adım: Kayıtlar ve biletler ekranında katılımcıyı bir etkinliğe kaydedin; bilet orada oluşur.");
            var note = FormLayout.Note("nextStepNote", Glyphs.Info, next);
            note.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("participantPage", (card, false), (note, false)));

            AcceptButton = btnKatilimciEkle;
            Button close;
            if (_session.IsAdmin)
            {
                close = CreateCloseButton("btnKapat");
            }
            else
            {
                close = FormLayout.Button("btnCikis", "Oturumu kapat", ButtonKind.Secondary);
                close.Click += (_, _) => EndSession();
                CancelButton = close;
            }

            SetFooterNote("Eklediğiniz kişileri yalnızca sizin hesabınız görür.");
            SetFooterButtons(btnEtkinlikSecim, close);
            FinishLayout();
        }
    }
}
