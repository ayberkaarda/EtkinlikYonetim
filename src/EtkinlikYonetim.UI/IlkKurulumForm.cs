using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// First-run setup: opens by itself on an empty database, where the person installing the
    /// application creates the first admin account with a password of their own; there is no default
    /// password. A database with accounts but no admin offers it from the sign-in with
    /// <see cref="RequireSetupKey"/> set: then a setup key issued for that database proves the founder's authority.
    /// Closes with <see cref="DialogResult.OK"/> and <see cref="Session"/> set on success.
    /// </summary>
    public partial class IlkKurulumForm : Form
    {
        /// <summary>Height of the key row and the longer explanation, removed while no key is asked for.</summary>
        private const int KeySectionHeight = 64;

        private readonly ISetupService _setup;
        private readonly ExclusiveOperation _create = new();
        private readonly string _emptyDatabaseText;
        private bool _requireSetupKey = true;

        public IlkKurulumForm(ISetupService setup)
        {
            ArgumentNullException.ThrowIfNull(setup);

            _setup = setup;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            Text = AppText.Title + " - " + Text;
            _emptyDatabaseText = lblAciklama.Text;
            txtAnahtar.MaxLength = 64;
            txtKullaniciAdi.AcceptLettersOnly();
            txtKullaniciAdi.MaxLength = InputRules.UserNameMaxLength;
            txtSifre.MaxLength = PasswordPolicy.MaxLength;
            txtSifreTekrar.MaxLength = PasswordPolicy.MaxLength;
            AcceptButton = btnOlustur;
            CancelButton = btnIptal;
            RequireSetupKey = false;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public UserSession? Session { get; private set; }

        /// <summary>
        /// Whether the setup key is asked for, because the database has accounts but no admin. The key
        /// field starts with the key of the ETKINLIK_SETUP_KEY environment variable, if it has the form of
        /// one; the database decides whether it is a valid key. A malformed value fills in nothing and is
        /// reported as a configuration error when the form opens.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool RequireSetupKey
        {
            get => _requireSetupKey;
            set
            {
                if (value == _requireSetupKey)
                {
                    return;
                }

                _requireSetupKey = value;
                var shift = value ? KeySectionHeight : -KeySectionHeight;
                SuspendLayout();
                lblAciklama.Height += shift;
                lblAciklama.Text = value ? SetupKeyHint : _emptyDatabaseText;
                lblAnahtar.Visible = value;
                txtAnahtar.Visible = value;
                var prefill = value ? _setup.GetEnvironmentKey() : new SetupKeyPrefill(null, false);
                txtAnahtar.Text = prefill.Key ?? string.Empty;
                EnvironmentKeyWarning = prefill.Invalid ? EnvironmentKeyInvalid : null;
                foreach (Control control in new Control[] { label1, txtKullaniciAdi, label2, txtSifre, label3, txtSifreTekrar, btnOlustur, btnIptal })
                {
                    control.Top += shift;
                }

                ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift);
                ActiveControl = value ? txtAnahtar : txtKullaniciAdi;
                ResumeLayout();
            }
        }

        /// <summary>The configuration warning shown when the form opens, or <see langword="null"/>.</summary>
        internal string? EnvironmentKeyWarning { get; private set; }

        /// <summary>Shown when ETKINLIK_SETUP_KEY is set to something that is not a setup key; the value itself is never shown.</summary>
        internal static readonly string EnvironmentKeyInvalid =
            SetupKeyPolicy.EnvironmentVariable + " ortam değişkeni geçerli bir kurulum anahtarı biçiminde değil (32 onaltılık karakter); "
            + "alan boş bırakıldı. Anahtarı elle girin veya ortam değişkenini düzeltin.";

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>How the founder gets a setup key for this database; no key is issued or shown here.</summary>
        internal static readonly string SetupKeyHint =
            "Bu veritabanında hesaplar var ama yönetici yok. İlk yönetici hesabı yalnızca bu veritabanı için üretilmiş bir kurulum anahtarıyla oluşturulabilir. "
            + "Anahtarı uygulama değil, veritabanı sahibi docs\\issue-setup-key.sql betiğiyle üretir; "
            + "anahtar 24 saat geçerlidir ve bir kez kullanılır.";

        /// <summary>Creates the first admin; ignored while a previous attempt is still running.</summary>
        internal async Task<UserSession?> CreateAsync()
        {
            UserSession? session = null;
            await this.RunExclusiveAsync(
                _create,
                async () =>
                {
                    try
                    {
                        var key = RequireSetupKey ? txtAnahtar.Text : null;
                        session = await _setup.CreateFirstAdminAsync(key, txtKullaniciAdi.Text, txtSifre.Text, txtSifreTekrar.Text);
                    }
                    finally
                    {
                        txtSifre.Clear();
                        txtSifreTekrar.Clear();
                        if (session is null)
                        {
                            _create.FocusAfterUnlock(txtSifre);
                        }
                        else
                        {
                            txtAnahtar.Clear();
                        }
                    }
                },
                btnOlustur,
                btnIptal);
            return session;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (EnvironmentKeyWarning is { } warning)
            {
                Prompts.Warn(warning);
            }
        }

        private async void btnOlustur_Click(object? sender, EventArgs e)
        {
            var session = await CreateAsync();
            if (session is not null)
            {
                Prompts.Inform("Yönetici hesabı oluşturuldu. Lütfen yeni şifrenizle giriş yapın.");
                Session = session;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
