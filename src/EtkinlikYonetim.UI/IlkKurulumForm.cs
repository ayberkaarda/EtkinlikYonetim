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
        /// field starts with the value of the ETKINLIK_SETUP_KEY environment variable, if any; the database
        /// decides whether it is a valid key.
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
                txtAnahtar.Text = value ? Environment.GetEnvironmentVariable(SetupKeyPolicy.EnvironmentVariable) ?? string.Empty : string.Empty;
                foreach (Control control in new Control[] { label1, txtKullaniciAdi, label2, txtSifre, label3, txtSifreTekrar, btnOlustur, btnIptal })
                {
                    control.Top += shift;
                }

                ClientSize = new Size(ClientSize.Width, ClientSize.Height + shift);
                ActiveControl = value ? txtAnahtar : txtKullaniciAdi;
                ResumeLayout();
            }
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>How the founder gets a setup key for this database; no key is issued or shown here.</summary>
        internal static readonly string SetupKeyHint =
            "Bu veritabanında hesaplar var ama yönetici yok. İlk yönetici hesabı yalnızca bu veritabanı için üretilmiş bir kurulum anahtarıyla oluşturulabilir. "
            + "Anahtarı veritabanının kurucusu \"EtkinlikYonetim.exe --create-setup-key\" komutuyla üretir; "
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
