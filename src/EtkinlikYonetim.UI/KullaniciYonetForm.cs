using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Account management for admins: disables and enables accounts and changes their role. The
    /// services refuse the admin's own account and the last enabled admin, and check the admin's own
    /// role in the database for every change.
    /// </summary>
    public sealed class KullaniciYonetForm : AppDialog
    {
        private readonly IAccountService _accounts;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();
        private readonly RequestSequence _listRequests = new();

        private readonly ThemedListBox lstHesaplar = new() { Name = nameof(lstHesaplar) };
        private readonly ThemedButton btnEtkinlestir = FormLayout.Button(nameof(btnEtkinlestir), "Etkinleştir", ButtonKind.Secondary, Glyphs.Enable);
        private readonly ThemedButton btnDevreDisi = FormLayout.Button(nameof(btnDevreDisi), "Devre dışı bırak", ButtonKind.Danger, Glyphs.Cancel);
        private readonly ThemedButton btnYoneticiYap = FormLayout.Button(nameof(btnYoneticiYap), "Yönetici yap", ButtonKind.Secondary, Glyphs.Admin);
        private readonly ThemedButton btnKullaniciYap = FormLayout.Button(nameof(btnKullaniciYap), "Kullanıcı yap", ButtonKind.Secondary, Glyphs.Person);

        public KullaniciYonetForm(IAccountService accounts, UserSession session)
            : base("Hesaplar", "Hesapları etkinleştirin, devre dışı bırakın ve rollerini değiştirin.", Glyphs.People, new Size(760, 560), new Size(640, 480), modal: true)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            ArgumentNullException.ThrowIfNull(session);

            _accounts = accounts;
            _session = session;
            BuildLayout();

            btnEtkinlestir.Click += async (_, _) => await SetEnabledAsync(true);
            btnDevreDisi.Click += async (_, _) => await SetEnabledAsync(false);
            btnYoneticiYap.Click += async (_, _) => await ChangeRoleAsync(UserRole.Admin);
            btnKullaniciYap.Click += async (_, _) => await ChangeRoleAsync(UserRole.User);
            Load += async (_, _) => await LoadAsync();
        }

        /// <summary>Loads the accounts; the account selected before stays selected.</summary>
        internal Task LoadAsync() => this.RunAsync(RefreshAsync);

        /// <summary>Enables or disables the selected account; ignored while a change is running.</summary>
        internal Task<bool> SetEnabledAsync(bool enabled) =>
            ChangeAsync(
                enabled ? "Seçilen hesap etkinleştirilsin mi?" : "Seçilen hesap devre dışı bırakılsın mı? Hesabın açık oturumları da çalışmaz hale gelir.",
                account => _accounts.SetEnabledAsync(_session, account.Id, enabled),
                enabled ? "Hesap etkinleştirildi." : "Hesap devre dışı bırakıldı.");

        /// <summary>Gives the selected account the role; ignored while a change is running.</summary>
        internal Task<bool> ChangeRoleAsync(UserRole role) =>
            ChangeAsync(
                role == UserRole.Admin ? "Seçilen hesaba yönetici rolü verilsin mi?" : "Seçilen hesabın rolü kullanıcı yapılsın mı?",
                account => _accounts.ChangeRoleAsync(_session, account.Id, role),
                "Hesabın rolü değiştirildi.");

        private Task<bool> ChangeAsync(string question, Func<AccountSummary, Task> change, string done) =>
            this.RunExclusiveAsync(
                _change,
                async () =>
                {
                    if (lstHesaplar.SelectedItem is not AccountItem item)
                    {
                        Prompts.Warn("Lütfen bir hesap seçin.");
                        return;
                    }

                    if (!Prompts.Confirm(question))
                    {
                        return;
                    }

                    await change(item.Account);
                    Prompts.Inform(done);
                    _change.FocusAfterUnlock(lstHesaplar);
                    await RefreshAsync();
                },
                btnEtkinlestir,
                btnDevreDisi,
                btnYoneticiYap,
                btnKullaniciYap);

        private async Task RefreshAsync()
        {
            var request = _listRequests.Next();
            var selectedId = (lstHesaplar.SelectedItem as AccountItem)?.Account.Id;
            lstHesaplar.IsLoading = true;
            IReadOnlyList<AccountSummary> accounts;
            try
            {
                accounts = await _accounts.GetAccountsAsync(_session);
            }
            finally
            {
                if (_listRequests.IsLatest(request) && !IsDisposed)
                {
                    lstHesaplar.IsLoading = false;
                }
            }

            if (!_listRequests.IsLatest(request) || IsDisposed)
            {
                return;
            }

            lstHesaplar.ReplaceItems(accounts.Select(a => new AccountItem(a)));
            var index = accounts.ToList().FindIndex(a => a.Id == selectedId);
            if (index >= 0)
            {
                lstHesaplar.SelectedIndex = index;
            }
        }

        private void BuildLayout()
        {
            Name = nameof(KullaniciYonetForm);

            lstHesaplar.Describe = item => item is AccountItem account ? account.Row() : new ListRow(item.ToString() ?? string.Empty);
            lstHesaplar.EmptyText = "Hesap bulunamadı.";
            btnEtkinlestir.Margin = btnDevreDisi.Margin = btnYoneticiYap.Margin = new Padding(0, 0, Spacing.S, 0);
            btnKullaniciYap.Margin = Padding.Empty;
            var actions = FormLayout.Bar(
                "accountActions",
                (btnEtkinlestir, false),
                (btnDevreDisi, false),
                (FormLayout.Spacer(), true),
                (btnYoneticiYap, false),
                (btnKullaniciYap, false));
            actions.Margin = new Padding(0, Spacing.M, 0, 0);
            var card = FormLayout.Card("accountsCard", "Hesaplar", FormLayout.Page("accountsPage", (lstHesaplar, true), (actions, false)));
            card.Margin = Padding.Empty;
            Content.Controls.Add(card);

            SetFooterNote("Kendi hesabınızı değiştiremezsiniz; son etkin yönetici devre dışı bırakılamaz.");
            SetFooterButtons(CreateCloseButton("btnKapat"));
            FinishLayout();
        }
    }
}
