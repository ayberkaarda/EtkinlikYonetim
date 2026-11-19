using System.Globalization;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>Sign-up of a new account with the user role.</summary>
    public sealed class KayitOlForm : AppDialog
    {
        private readonly IAuthService _auth;
        private readonly ExclusiveOperation _save = new();

        private readonly TextBox txtKullaniciAdi = new() { Name = nameof(txtKullaniciAdi), MaxLength = InputRules.UserNameMaxLength };
        private readonly TextBox txtSifre = new() { Name = nameof(txtSifre), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly TextBox txtSifreTekrar = new() { Name = nameof(txtSifreTekrar), MaxLength = PasswordPolicy.MaxLength, UseSystemPasswordChar = true };
        private readonly ThemedButton btnKayitOl = FormLayout.Button(nameof(btnKayitOl), "Kayıt ol", ButtonKind.Primary);

        public KayitOlForm(IAuthService auth)
            : base("Kayıt ol", "Bir kullanıcı adı ve şifreyle hesap açın.", Glyphs.AddPerson, new Size(560, 440), new Size(460, 400), modal: true)
        {
            ArgumentNullException.ThrowIfNull(auth);

            _auth = auth;
            txtKullaniciAdi.AcceptLettersOnly();
            BuildLayout();
            _save.HoldClosingOf(this);
            btnKayitOl.Click += async (_, _) => await SaveAsync();
        }

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

        private void BuildLayout()
        {
            Name = nameof(KayitOlForm);
            var name = FormLayout.Field(
                "&Kullanıcı adı",
                FormLayout.Input(txtKullaniciAdi),
                string.Create(CultureInfo.CurrentCulture, $"Yalnızca harf; en fazla {InputRules.UserNameMaxLength} karakter."));
            var passwords = FormLayout.FieldRow(
                "passwordRow",
                (FormLayout.Field("Ş&ifre", FormLayout.Input(txtSifre), string.Create(CultureInfo.CurrentCulture, $"En az {PasswordPolicy.MinLength} karakter.")), 50),
                (FormLayout.Field("Şifre &tekrar", FormLayout.Input(txtSifreTekrar)), 50));
            name.Margin = new Padding(0, 0, 0, Spacing.M);
            var card = FormLayout.Card("signUpCard", null, FormLayout.Stack("signUpForm", name, passwords), fill: false);
            card.Margin = Padding.Empty;
            Content.Controls.Add(card);

            AcceptButton = btnKayitOl;
            SetFooterNote("Kayıttan sonra giriş ekranından oturum açın.");
            SetFooterButtons(btnKayitOl, CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
