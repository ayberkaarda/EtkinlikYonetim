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
    /// <see cref="RequireSetupKey"/> set: then the local setup key proves the founder's authority.
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
        /// Whether the setup key is asked for, because the database has accounts but no admin. Setting it
        /// issues the key (when it comes from a file) and shows where to find it; the key itself is never
        /// shown.
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
                lblAciklama.Text = value ? SetupKeyHint(_setup.IssueSetupKey()) : _emptyDatabaseText;
                lblAnahtar.Visible = value;
                txtAnahtar.Visible = value;
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

        /// <summary>Where the installer finds the setup key; the key itself is never shown.</summary>
        internal static string SetupKeyHint(string? keyFilePath) =>
            "Bu veritabanında hesaplar var ama yönetici yok. İlk yönetici hesabını yalnızca bu bilgisayarın kurulum anahtarıyla oluşturabilirsiniz. "
            + (keyFilePath is null
                ? "Anahtar, " + FileSetupKeyStore.EnvironmentVariable + " ortam değişkeninde tanımlıdır."
                : "Anahtar şu dosyadadır:\n" + keyFilePath)
            + "\nAnahtar bir kez kullanılır.";

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
