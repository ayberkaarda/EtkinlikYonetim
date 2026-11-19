using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>Sign-in; also the entry point to the sign-up form.</summary>
    public partial class GirisYapForm : Form
    {
        private readonly IAuthService _auth;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _signIn = new();

        public GirisYapForm(IAuthService auth, IFormFactory forms)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(forms);

            _auth = auth;
            _forms = forms;

            InitializeComponent();
            Icon = AppAssets.Icon;
            BackgroundImage = AppAssets.Background;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MaximumSize = Size;
            MinimumSize = Size;
            txtKullaniciAdi.AcceptLettersOnly();
            txtSifre.MaxLength = PasswordPolicy.SignInMaxLength;
        }

        /// <summary>
        /// Whether the database has accounts but no admin, so the sign-in offers the first-run setup.
        /// That setup asks for the local setup key; having no admin alone grants nothing.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool OfferAdminSetup
        {
            get => btnKurulum.Tag is true;
            set
            {
                btnKurulum.Tag = value;
                btnKurulum.Visible = value;
            }
        }

        /// <summary>
        /// Signs the account in; ignored while a previous attempt is still running. Returns the session,
        /// or <see langword="null"/> when the attempt failed (the reason has been shown) or was ignored.
        /// </summary>
        internal async Task<UserSession?> SignInAsync()
        {
            UserSession? session = null;
            await this.RunExclusiveAsync(
                _signIn,
                async () =>
                {
                    try
                    {
                        session = await _auth.SignInAsync(txtKullaniciAdi.Text, txtSifre.Text);
                    }
                    finally
                    {
                        txtSifre.Clear();
                        if (session is null)
                        {
                            // Retyping the password is the next step after a refused attempt.
                            _signIn.FocusAfterUnlock(txtSifre);
                        }
                    }
                },
                btnGiris2,
                btnkayitol,
                btnKurulum);
            return session;
        }

        /// <summary>
        /// Opens the panel of the signed-in role: admins get the management panel, users the participant
        /// form. Closing the panel ends the application, as in the original application; a panel closed
        /// because the database no longer accepts the session shows the sign-in again.
        /// </summary>
        private void OpenPanel(UserSession session)
        {
            Hide();
            bool signedOut;
            using (Form panel = session.IsAdmin ? _forms.Create<Form1>(session) : _forms.Create<KatilimciEkleForm>(session))
            {
                panel.ShowDialog();
                signedOut = panel is ISessionPanel { SignedOut: true };
            }

            if (signedOut)
            {
                txtKullaniciAdi.Clear();
                Show();
                txtKullaniciAdi.Focus();
                return;
            }

            Close();
        }

        private void GirisYapForm_Load(object? sender, EventArgs e)
        {
            txtSifre.PasswordChar = '*';
            FormBorderStyle = FormBorderStyle.None;

            // The window has its own close and minimize buttons.
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;

            var accent = ColorTranslator.FromHtml("#ea2842");
            btnkayitol.ForeColor = accent;
            btnGiris2.PrimaryColor = accent;
            btnGiris2.Font = new Font(btnGiris2.Font.FontFamily, 9, FontStyle.Bold);
        }

        private async void btnGiris2_Click(object? sender, EventArgs e)
        {
            var session = await SignInAsync();
            if (session is not null)
            {
                Dialogs.Show(this, "Giriş başarılı. Hoş geldiniz, " + session.UserName + "!", AppText.Title);
                OpenPanel(session);
            }
        }

        private void btnkayitol_Click(object? sender, EventArgs e)
        {
            using var register = _forms.Create<KayitOlForm>();
            register.StartPosition = FormStartPosition.CenterParent;
            register.ShowDialog(this);
        }

        private void btnKurulum_Click(object? sender, EventArgs e)
        {
            using var setup = _forms.Create<IlkKurulumForm>();
            setup.RequireSetupKey = true;
            setup.StartPosition = FormStartPosition.CenterParent;
            if (setup.ShowDialog(this) == DialogResult.OK)
            {
                // The new admin signs in here with the password just chosen.
                OfferAdminSetup = false;
                txtKullaniciAdi.Text = setup.Session?.UserName ?? string.Empty;
                txtSifre.Focus();
            }
        }

        private void btnkapat_Click(object? sender, EventArgs e) => Close();

        private void btnAsagial_Click(object? sender, EventArgs e) => WindowState = FormWindowState.Minimized;
    }
}
