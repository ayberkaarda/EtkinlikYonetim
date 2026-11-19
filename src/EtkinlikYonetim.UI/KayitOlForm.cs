using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>Sign-up of a new account with the user role.</summary>
    public partial class KayitOlForm : Form
    {
        private readonly IAuthService _auth;
        private readonly ExclusiveOperation _save = new();

        public KayitOlForm(IAuthService auth)
        {
            ArgumentNullException.ThrowIfNull(auth);
            _auth = auth;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            txtKullaniciAdi.AcceptLettersOnly();
            txtSifre.MaxLength = PasswordPolicy.MaxLength;
            txtSifreTekrar.MaxLength = PasswordPolicy.MaxLength;
        }

        /// <summary>Information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

        /// <summary>Registers the account; ignored while a previous save is still running.</summary>
        internal Task<bool> SaveAsync() =>
            this.RunExclusiveAsync(
                _save,
                async () =>
                {
                    await _auth.RegisterAsync(txtKullaniciAdi.Text, txtSifre.Text, txtSifreTekrar.Text);

                    txtKullaniciAdi.Clear();
                    txtSifre.Clear();
                    txtSifreTekrar.Clear();
                    Prompts.Inform("Kullanıcı başarıyla eklendi.");
                    _save.FocusAfterUnlock(txtKullaniciAdi);
                },
                btnKayitOl);

        private async void btnKayitOl_Click(object? sender, EventArgs e) => await SaveAsync();

        private void btnkapat_Click(object? sender, EventArgs e) => Close();

        private void btnAsagial_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;
    }
}
