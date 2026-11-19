using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Sign-in with a brand panel; also the entry point to the sign-up and, while the database has accounts
    /// but no admin, to the first admin's setup with the setup key. It stays the application's main window:
    /// it hides while a panel is open and returns when the panel's session ended.
    /// </summary>
    public sealed class GirisYapForm : AppDialog
    {
        /// <summary>Shown when a panel closed because its session ended (signed out or no longer accepted).</summary>
        internal const string SessionEndedNotice = "Oturum kapandı. Devam etmek için yeniden giriş yapın.";

        /// <summary>Shown after the first admin was created with the setup key.</summary>
        internal const string AdminCreatedNotice = "Yönetici hesabı oluşturuldu. Yeni şifrenizle giriş yapın.";

        private readonly IAuthService _auth;
        private readonly IFormFactory _forms;
        private readonly ExclusiveOperation _signIn = new();
        private bool _offerAdminSetup;

        private readonly TextBox txtKullaniciAdi = new() { Name = nameof(txtKullaniciAdi), MaxLength = InputRules.UserNameMaxLength };
        private readonly TextBox txtSifre = new() { Name = nameof(txtSifre), MaxLength = PasswordPolicy.SignInMaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnGiris = FormLayout.Button(nameof(btnGiris), "Giriş yap", ButtonKind.Primary);
        private readonly ThemedButton btnKayitOl = FormLayout.Button(nameof(btnKayitOl), "Kayıt ol", ButtonKind.Secondary, Glyphs.AddPerson);
        private readonly ThemedButton btnKurulum = FormLayout.Button(nameof(btnKurulum), "İlk yönetici hesabını oluştur", ButtonKind.Subtle, Glyphs.Admin);

        public GirisYapForm(IAuthService auth, IFormFactory forms)
            : base("Giriş", string.Empty, string.Empty, new Size(860, 540), new Size(760, 500), header: false)
        {
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(forms);

            _auth = auth;
            _forms = forms;
            txtKullaniciAdi.AcceptLettersOnly();
            BuildLayout();
            OfferAdminSetup = false;

            btnGiris.Click += async (_, _) => await SignInAndOpenPanelAsync();
            btnKayitOl.Click += (_, _) => OpenSignUp();
            btnKurulum.Click += (_, _) => OpenSetup();
        }

        /// <summary>
        /// Whether the database has accounts but no admin, so the sign-in offers the first-run setup.
        /// That setup asks for the local setup key; having no admin alone grants nothing.
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool OfferAdminSetup
        {
            get => _offerAdminSetup;
            set
            {
                _offerAdminSetup = value;
                FormLayout.SetShown(btnKurulum, value);
            }
        }

        /// <summary>A message for the next sign-in, for example after the first admin was created.</summary>
        internal void ShowNotice(string? notice)
        {
            if (!string.IsNullOrWhiteSpace(notice))
            {
                ShowStatus(StatusKind.Info, notice);
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
                btnGiris,
                btnKayitOl,
                btnKurulum);
            return session;
        }

        private async Task SignInAndOpenPanelAsync()
        {
            var session = await SignInAsync();
            if (session is not null && !IsDisposed)
            {
                OpenPanel(session);
            }
        }

        /// <summary>
        /// Opens the panel of the signed-in role: admins get the management panel, users the participant
        /// screen. Closing the panel ends the application, as in the original application; a panel closed
        /// because its session ended shows the sign-in again.
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
                ShowNotice(SessionEndedNotice);
                Show();
                txtKullaniciAdi.Focus();
                return;
            }

            Close();
        }

        private void OpenSignUp()
        {
            using var register = _forms.Create<KayitOlForm>();
            register.ShowDialog(this);
        }

        private void OpenSetup()
        {
            if (_signIn.IsRunning)
            {
                return;
            }

            using var setup = _forms.Create<IlkKurulumForm>();
            setup.RequireSetupKey = true;
            if (setup.ShowDialog(this) == DialogResult.OK)
            {
                // The new admin signs in here with the password just chosen.
                OfferAdminSetup = false;
                txtKullaniciAdi.Text = setup.Session?.UserName ?? string.Empty;
                ShowNotice(AdminCreatedNotice);
                txtSifre.Focus();
            }
        }

        private void BuildLayout()
        {
            Name = nameof(GirisYapForm);
            SetSidePanel(
                new BrandPanel(
                    "Etkinlikleri planlayın, katılımcıları kaydedin, kapıda biletleri saniyeler içinde doğrulayın.",
                    (Glyphs.Calendar, "Etkinliğin tarihini, kapasitesini ve açıklamasını tek yerden yönetin."),
                    (Glyphs.QrCode, "Her kayda QR kodlu bilet; etkinlik dolunca bekleme listesi."),
                    (Glyphs.CheckMark, "Girişte bileti okutun, içerideki katılımcı sayısını anında görün.")),
                Sizes.BrandPanelWidth);

            var title = FormLayout.SectionTitle("Giriş yap");
            title.Name = "formTitle";
            title.Font = Theme.Fonts.Title;
            var intro = FormLayout.Hint("Kullanıcı adınız ve şifrenizle oturum açın.");
            intro.Name = "formIntro";
            intro.Margin = new Padding(0, 0, 0, Spacing.XL);

            var name = FormLayout.Field("&Kullanıcı adı", FormLayout.Input(txtKullaniciAdi));
            var password = FormLayout.Field("Ş&ifre", FormLayout.Input(txtSifre));
            name.Margin = password.Margin = new Padding(0, 0, 0, Spacing.M);
            btnGiris.AutoSize = false;
            btnGiris.Dock = DockStyle.Top;
            btnGiris.Margin = new Padding(0, Spacing.S, 0, Spacing.XL);

            var newHere = FormLayout.Hint("Hesabınız yok mu? Bir kullanıcı adı ve şifreyle hemen kayıt olun.");
            newHere.Name = "signUpHint";
            newHere.Margin = new Padding(0, 0, 0, Spacing.S);
            btnKayitOl.Margin = new Padding(0, 0, 0, Spacing.L);
            btnKurulum.Margin = Padding.Empty;

            var stack = FormLayout.Stack("signInForm", title, intro, name, password, btnGiris, newHere, btnKayitOl, btnKurulum);
            stack.Padding = new Padding(Spacing.S, Spacing.S, Spacing.S, 0);
            btnKayitOl.Anchor = btnKurulum.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            Content.Controls.Add(stack);

            AcceptButton = btnGiris;
            SetFooterNote("Yönetici hesapları yönetim panelindeki Hesaplar ekranından verilir.");
            SetFooterButtons(CreateCloseButton("btnCikis", "Çıkış"));
            FinishLayout();
        }
    }
}
