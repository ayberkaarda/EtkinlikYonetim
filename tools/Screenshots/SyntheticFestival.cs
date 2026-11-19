using System.Reflection;
using EtkinlikYonetim.Business.Exports;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Screenshots;

/// <summary>
/// An invented season of events in memory: made-up events, people and accounts, e-mail addresses on the
/// reserved example.com domain and masked national ids, so the screenshots never show real or personal data
/// and no database is needed. Every call completes synchronously. Ticket codes are visibly sample values.
/// </summary>
internal sealed class SyntheticFestival :
    IAuthService, ISetupService, IEventService, IParticipantService, IRegistrationService, IAccountService, ICheckInService, IParticipantExportService
{
    /// <summary>A visibly made-up ticket code for the screens that show one.</summary>
    public const string SampleTicket = "ETK-ORNEK-BILET-KODU-2027";

    private readonly List<EventSummary> _events =
    [
        new(1, "Bahar Konseri: Şehir Orkestrası", new DateOnly(2027, 4, 17), 150, 112, 0, "Açık hava sahnesi, kapılar 19.00'da açılır."),
        new(2, "Yazılım Atölyesi", new DateOnly(2027, 5, 8), 30, 30, 3, "Dizüstü bilgisayarınızı getirin."),
        new(3, "Kısa Film Gecesi", new DateOnly(2027, 5, 21), 80, 46, 0, "Gösterimlerin ardından yönetmenlerle söyleşi."),
        new(4, "Bilim ve Teknoloji Festivali Kapanış Paneli: Yapay Zekâ, Uzay ve Gelecek", new DateOnly(2027, 6, 12), 400, 251, 0, null),
        new(5, "Kış Şenliği", new DateOnly(2027, 1, 10), 50, 50, 0, "Geçmiş etkinlik."),
        new(6, "Fotoğraf Yürüyüşü", new DateOnly(2027, 6, 26), 25, 9, 0, null, Cancelled: true),
    ];

    private readonly List<RegistrationDetails> _registrations =
    [
        new(11, 1, "Bahar Konseri: Şehir Orkestrası", new DateOnly(2027, 4, 17), 1, "Deniz Aksoy", SampleTicket),
        new(12, 1, "Bahar Konseri: Şehir Orkestrası", new DateOnly(2027, 4, 17), 2, "Elif Karaca", SampleTicket),
        new(13, 2, "Yazılım Atölyesi", new DateOnly(2027, 5, 8), 3, "Mert Yılmazer", null, RegistrationStatus.Waiting, 2),
        new(14, 3, "Kısa Film Gecesi", new DateOnly(2027, 5, 21), 1, "Deniz Aksoy", SampleTicket),
        new(15, 4, "Bilim ve Teknoloji Festivali Kapanış Paneli: Yapay Zekâ, Uzay ve Gelecek", new DateOnly(2027, 6, 12), 4, "Zehra Nur Karamanlıoğlu Büyükçekmeceli", SampleTicket),
        new(16, 6, "Fotoğraf Yürüyüşü", new DateOnly(2027, 6, 26), 2, "Elif Karaca", null, RegistrationStatus.EventCancelled),
    ];

    private readonly List<ParticipantView> _participants =
    [
        new("Deniz Aksoy", "100******46", "deniz.aksoy@example.com", "Giriş yapıldı"),
        new("Elif Karaca", "205******18", "elif.karaca@example.com", "Giriş yapılmadı"),
        new("Mert Yılmazer", "318******72", "mert.yilmazer@example.com", "Giriş yapıldı"),
        new("Zehra Nur Karamanlıoğlu Büyükçekmeceli", "427******30", "zehra.karamanlioglu@example.com", "Giriş yapılmadı"),
        new("Can Özdemirci", "539******04", "E-posta Yok", "Bekleme listesi, sıra: 1"),
        new("Selin Ateş", "641******88", "selin.ates@example.com", "Bekleme listesi, sıra: 2"),
    ];

    private readonly List<AccountSummary> _accounts =
    [
        new(1, "derya", UserRole.Admin, false),
        new(2, "kapigorevlisi", UserRole.Admin, false),
        new(3, "deniz", UserRole.User, false),
        new(4, "elif", UserRole.User, false),
        new(5, "mert", UserRole.User, true),
        new(6, "zehranurkaramanlioglu", UserRole.User, false),
    ];

    public UserSession Admin { get; } = Session(1, "derya", UserRole.Admin);

    public UserSession User { get; } = Session(3, "deniz", UserRole.User);

    /// <summary>A session as the services would issue it; its constructor is internal to the business layer.</summary>
    private static UserSession Session(int id, string name, UserRole role) =>
        (UserSession)Activator.CreateInstance(
            typeof(UserSession),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [id, name, role, 0],
            culture: null)!;

    public Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default) => Task.FromResult(Admin);

    public Task RegisterAsync(string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public SetupKeyPrefill GetEnvironmentKey() => new(null, false);

    public Task<UserSession> CreateFirstAdminAsync(string? setupKey, string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default) =>
        Task.FromResult(Admin);

    public Task<IReadOnlyList<EventSummary>> GetEventsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventSummary>>(_events);

    /// <summary>The database's day of the screenshots: before the season, so every event can still be changed.</summary>
    public Task<DateOnly> GetTodayAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DateOnly(2027, 3, 1));

    public Task<IReadOnlyList<EventSummary>> GetOpenEventsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventSummary>>([.. _events.Where(e => !e.Cancelled && e.Date >= new DateOnly(2027, 4, 1))]);

    public Task<int> CreateAsync(UserSession session, string? name, DateTime eventDate, int capacity, CancellationToken cancellationToken = default) =>
        Task.FromResult(7);

    public Task UpdateAsync(UserSession session, EventSummary original, DateTime eventDate, int capacity, string? description, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CancelAsync(UserSession session, int eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> AddAsync(UserSession session, string? nationalId, string? fullName, string? email, CancellationToken cancellationToken = default) =>
        Task.FromResult(7);

    public Task<IReadOnlyList<ParticipantView>> GetByEventAsync(UserSession session, int eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ParticipantView>>(_participants);

    public Task<RegistrationReceipt> RegisterAsync(UserSession session, int eventId, string? nationalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new RegistrationReceipt(17, null, "Yazılım Atölyesi", new DateOnly(2027, 5, 8), 3));

    public Task<IReadOnlyList<RegistrationDetails>> GetMyRegistrationsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegistrationDetails>>(_registrations);

    Task IRegistrationService.CancelAsync(UserSession session, int registrationId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AccountSummary>>(_accounts);

    public Task SetEnabledAsync(UserSession session, int userId, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ChangeRoleAsync(UserSession session, int userId, UserRole role, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<CheckInResult> CheckInAsync(UserSession session, int eventId, string? code, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckInResult(CheckInOutcome.CheckedIn, new CheckInCount(87, 112), "Elif Karaca"));

    public Task<CheckInCount> GetCountAsync(UserSession session, int eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckInCount(86, 112));

    public Task<ExportFile> ExportCsvAsync(UserSession session, int eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ExportFile("katilimcilar.csv", []));

    public Task<ExportFile> ExportHtmlAsync(UserSession session, int eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ExportFile("katilimcilar.html", []));
}
