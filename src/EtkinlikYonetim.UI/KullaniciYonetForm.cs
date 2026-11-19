using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI
{
    /// <summary>
    /// Account management for admins: disables and enables accounts and changes their role. The
    /// services refuse the admin's own account and the last enabled admin, and check the admin's own
    /// role in the database for every change.
    /// </summary>
    public partial class KullaniciYonetForm : Form
    {
        private readonly IAccountService _accounts;
        private readonly UserSession _session;
        private readonly ExclusiveOperation _change = new();
        private readonly RequestSequence _listRequests = new();

        public KullaniciYonetForm(IAccountService accounts, UserSession session)
        {
            ArgumentNullException.ThrowIfNull(accounts);
            ArgumentNullException.ThrowIfNull(session);

            _accounts = accounts;
            _session = session;
            Prompts = new MessageBoxPrompts(this);

            InitializeComponent();
            Icon = AppAssets.Icon;
            Text = AppText.Title + " - " + Text;
        }

        /// <summary>Confirmation and information dialogs; replaced in tests so no window opens.</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal IUserPrompts Prompts { get; set; }

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
            var accounts = await _accounts.GetAccountsAsync(_session);
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

        private async void KullaniciYonetForm_Load(object? sender, EventArgs e) => await LoadAsync();

        private async void btnEtkinlestir_Click(object? sender, EventArgs e) => await SetEnabledAsync(true);

        private async void btnDevreDisi_Click(object? sender, EventArgs e) => await SetEnabledAsync(false);

        private async void btnYoneticiYap_Click(object? sender, EventArgs e) => await ChangeRoleAsync(UserRole.Admin);

        private async void btnKullaniciYap_Click(object? sender, EventArgs e) => await ChangeRoleAsync(UserRole.User);
    }
}
