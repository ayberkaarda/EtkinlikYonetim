using System.Globalization;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// First-run setup: opens by itself on an empty database, where the person installing the
    /// application creates the first admin account with a password of their own; there is no default
    /// password. A database with accounts but no admin offers it from the sign-in with
    /// <see cref="RequireSetupKey"/> set: then a setup key issued for that database proves the founder's authority.
    /// Closes with <see cref="DialogResult.OK"/> and <see cref="Session"/> set on success.
    /// </summary>
    public sealed class IlkKurulumForm : AppDialog
    {
        private readonly ISetupService _setup;
        private readonly ExclusiveOperation _create = new();
        private bool _requireSetupKey = true;

        private readonly Label lblAciklama = FormLayout.Text(nameof(lblAciklama));
        private readonly TextBox txtAnahtar = new() { Name = nameof(txtAnahtar), MaxLength = 64, UseSystemPasswordChar = true };
        private readonly TextBox txtKullaniciAdi = new() { Name = nameof(txtKullaniciAdi), MaxLength = InputRules.UserNameMaxLength };
        private readonly TextBox txtSifre = new() { Name = nameof(txtSifre), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtSifreTekrar = new() { Name = nameof(txtSifreTekrar), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnOlustur = FormLayout.Button(nameof(btnOlustur), "Hesabı oluştur", ButtonKind.Primary);
        private readonly ThemedButton btnIptal = FormLayout.Button(nameof(btnIptal), "İptal", ButtonKind.Secondary);
        private TableLayoutPanel _keyField = null!;

        public IlkKurulumForm(ISetupService setup)
            : base("İlk kurulum", "İlk yönetici hesabını kendi şifrenizle oluşturun.", Glyphs.Admin, new Size(640, 540), new Size(520, 460))
        {
            ArgumentNullException.ThrowIfNull(setup);

            _setup = setup;
            txtKullaniciAdi.AcceptLettersOnly();
            BuildLayout();
            _create.HoldClosingOf(this);
            RequireSetupKey = false;
            btnOlustur.Click += async (_, _) => await CreateAndCloseAsync();
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public UserSession? Session { get; private set; }

        /// <summary>
        /// Whether the setup key is asked for, because the database has accounts but no admin. The key
        /// field starts with the key of the ETKINLIK_SETUP_KEY environment variable, if it has the form of
        /// one; the database decides whether it is a valid key. A malformed value fills in nothing and is
        /// reported as a configuration warning when the window opens.
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
                FormLayout.SetShown(_keyField, value);
                lblAciklama.Text = value ? SetupKeyHint : EmptyDatabaseHint;
                var prefill = value ? _setup.GetEnvironmentKey() : new SetupKeyPrefill(null, false);
                txtAnahtar.Text = prefill.Key ?? string.Empty;
                EnvironmentKeyWarning = prefill.Invalid ? EnvironmentKeyInvalid : null;
                ActiveControl = value ? txtAnahtar : txtKullaniciAdi;
            }
        }

        /// <summary>The configuration warning shown when the window opens, or <see langword="null"/>.</summary>
        internal string? EnvironmentKeyWarning { get; private set; }

        /// <summary>Shown when ETKINLIK_SETUP_KEY is set to something that is not a setup key; the value itself is never shown.</summary>
        internal static readonly string EnvironmentKeyInvalid =
            SetupKeyPolicy.EnvironmentVariable + " ortam değişkeni geçerli bir kurulum anahtarı biçiminde değil (32 onaltılık karakter); "
            + "alan boş bırakıldı. Anahtarı elle girin veya ortam değişkenini düzeltin.";

        /// <summary>How the founder gets a setup key for this database; no key is issued or shown here.</summary>
        internal static readonly string SetupKeyHint =
            "Bu veritabanında hesaplar var ama yönetici yok. İlk yönetici hesabı yalnızca bu veritabanı için üretilmiş bir kurulum anahtarıyla oluşturulabilir. "
            + "Anahtarı uygulama değil, veritabanı sahibi docs\\issue-setup-key.sql betiğiyle üretir; "
            + "anahtar 24 saat geçerlidir ve bir kez kullanılır.";

        /// <summary>The explanation on an empty database, where no key is needed.</summary>
        internal const string EmptyDatabaseHint =
            "Henüz bir yönetici hesabı yok. İlk yönetici hesabını kendi belirleyeceğiniz bir şifreyle oluşturun; varsayılan bir şifre yoktur.";

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

        private async Task CreateAndCloseAsync()
        {
            var session = await CreateAsync();
            if (session is not null && !IsDisposed)
            {
                // The window closes at once; the sign-in that follows says the account was created.
                Session = session;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(IlkKurulumForm);
            var note = FormLayout.Note("setupNote", Glyphs.Info, lblAciklama);

            _keyField = FormLayout.Field("&Kurulum anahtarı", FormLayout.Input(txtAnahtar), "Anahtar ekranda gösterilmez; yapıştırabilirsiniz.");
            var name = FormLayout.Field(
                "&Kullanıcı adı",
                FormLayout.Input(txtKullaniciAdi),
                string.Create(CultureInfo.CurrentCulture, $"Yalnızca harf; en fazla {InputRules.UserNameMaxLength} karakter."));
            var passwords = FormLayout.FieldRow(
                "passwordRow",
                (FormLayout.Field("Ş&ifre", FormLayout.Input(txtSifre), string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength} karakter.")), 50),
                (FormLayout.Field("Şifre &tekrar", FormLayout.Input(txtSifreTekrar)), 50));
            _keyField.Margin = name.Margin = new Padding(0, 0, 0, Spacing.M);

            var form = FormLayout.Stack("setupForm", _keyField, name, passwords);
            var card = FormLayout.Card("setupCard", null, form, fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(FormLayout.Page("setupPage", (note, false), (card, false)));

            AcceptButton = btnOlustur;
            CancelButton = btnIptal;
            btnIptal.DialogResult = DialogResult.Cancel;
            SetFooterNote("Hesap oluşunca giriş ekranında yeni şifrenizle oturum açın.");
            SetFooterButtons(btnOlustur, btnIptal);
            FinishLayout();
        }
    }
}
