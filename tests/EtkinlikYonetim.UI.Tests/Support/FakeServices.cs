using EtkinlikYonetim.Business;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.UI.Infrastructure;

namespace EtkinlikYonetim.UI.Tests.Support;

/// <summary>Sign-in and sign-up calls that stay pending until the test completes them.</summary>
internal sealed class PendingAuthService : IAuthService
{
    private TaskCompletionSource<UserSession> _signIn = new();
    private TaskCompletionSource _register = new();

    public int SignInCalls { get; private set; }

    public int RegisterCalls { get; private set; }

    public string? LastSignInPassword { get; private set; }

    public Task<UserSession> SignInAsync(string? userName, string? password, CancellationToken cancellationToken = default)
    {
        SignInCalls++;
        LastSignInPassword = password;
        _signIn = new TaskCompletionSource<UserSession>();
        return _signIn.Task;
    }

    public Task RegisterAsync(string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default)
    {
        RegisterCalls++;
        _register = new TaskCompletionSource();
        return _register.Task;
    }

    /// <summary>Refuses the pending sign-in as wrong credentials would.</summary>
    public void RefuseSignIn() => _signIn.SetException(new BusinessRuleException("Kullanıcı adı veya şifre hatalı."));

    /// <summary>Completes the pending sign-up.</summary>
    public void CompleteRegister() => _register.SetResult();
}

/// <summary>First-run setup calls that stay pending until the test completes them.</summary>
internal sealed class PendingSetupService : ISetupService
{
    private TaskCompletionSource<UserSession> _create = new();

    public int CreateCalls { get; private set; }

    public string? LastSetupKey { get; private set; }

    public Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> IsAdminMissingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    /// <summary>What the fake ETKINLIK_SETUP_KEY fills in.</summary>
    public SetupKeyPrefill Prefill { get; set; } = new(null, false);

    public SetupKeyPrefill GetEnvironmentKey() => Prefill;

    public Task<UserSession> CreateFirstAdminAsync(string? setupKey, string? userName, string? password, string? passwordRepeat, CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        LastSetupKey = setupKey;
        _create = new TaskCompletionSource<UserSession>();
        return _create.Task;
    }

    /// <summary>Completes the pending setup with a new admin session.</summary>
    public void CompleteCreate() => _create.SetResult(new UserSession(1, "kurucu", UserRole.Admin));
}

/// <summary>Event listings answered at once; event creation stays pending.</summary>
internal sealed class ControlledEventService : IEventService
{
    private TaskCompletionSource<int> _create = new();

    public List<EventSummary> Events { get; } =
    [
        new(1, "Bahar Konseri", new DateOnly(2027, 4, 17), 150, 0),
        new(2, "Yazılım Atölyesi", new DateOnly(2027, 5, 8), 30, 0),
    ];

    public int CreateCalls { get; private set; }

    public Task<IReadOnlyList<EventSummary>> GetEventsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventSummary>>(Events);

    public Task<IReadOnlyList<EventSummary>> GetOpenEventsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventSummary>>(Events);

    public Task<int> CreateAsync(UserSession session, string? name, DateTime eventDate, int capacity, CancellationToken cancellationToken = default)
    {
        CreateCalls++;
        _create = new TaskCompletionSource<int>();
        return _create.Task;
    }

    public void CompleteCreate() => _create.SetResult(3);

    public int UpdateCalls { get; private set; }

    public int CancelCalls { get; private set; }

    public List<(int EventId, DateTime Date, int Capacity, string? Description)> Updates { get; } = [];

    public Task UpdateAsync(UserSession session, int eventId, DateTime eventDate, int capacity, string? description, CancellationToken cancellationToken = default)
    {
        UpdateCalls++;
        Updates.Add((eventId, eventDate, capacity, description));
        return new TaskCompletionSource().Task;
    }

    public Task CancelAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        CancelCalls++;
        return new TaskCompletionSource().Task;
    }
}

/// <summary>
/// Participant writes stay pending; the participant lists of events are answered by the test, in any
/// order, through <see cref="ListRequests"/>.
/// </summary>
internal sealed class ControlledParticipantService : IParticipantService
{
    private TaskCompletionSource<int> _add = new();

    public int AddCalls { get; private set; }

    public List<(int EventId, TaskCompletionSource<IReadOnlyList<ParticipantView>> Answer)> ListRequests { get; } = [];

    public Task<int> AddAsync(UserSession session, string? nationalId, string? fullName, string? email, CancellationToken cancellationToken = default)
    {
        AddCalls++;
        _add = new TaskCompletionSource<int>();
        return _add.Task;
    }

    public Task<IReadOnlyList<ParticipantView>> GetByEventAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<IReadOnlyList<ParticipantView>>();
        ListRequests.Add((eventId, answer));
        return answer.Task;
    }

    public void CompleteAdd() => _add.SetResult(4);
}

/// <summary>Registration writes stay pending; the registration list is answered at once.</summary>
internal sealed class ControlledRegistrationService : IRegistrationService
{
    private TaskCompletionSource<RegistrationReceipt> _register = new();

    public List<RegistrationDetails> Registrations { get; } = [];

    public int RegisterCalls { get; private set; }

    public int CancelCalls { get; private set; }

    public Task<RegistrationReceipt> RegisterAsync(UserSession session, int eventId, string? nationalId, CancellationToken cancellationToken = default)
    {
        RegisterCalls++;
        _register = new TaskCompletionSource<RegistrationReceipt>();
        return _register.Task;
    }

    public Task<IReadOnlyList<RegistrationDetails>> GetMyRegistrationsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RegistrationDetails>>(Registrations);

    public Task CancelAsync(UserSession session, int registrationId, CancellationToken cancellationToken = default)
    {
        CancelCalls++;
        return new TaskCompletionSource().Task;
    }

    public void CompleteRegister() =>
        _register.SetResult(new RegistrationReceipt(9, "ETK-ABCDEFGHJK", "Bahar Konseri", new DateOnly(2027, 4, 17)));

    /// <summary>Completes the pending registration with a place on the waiting list.</summary>
    public void CompleteRegisterOnTheWaitingList(int position) =>
        _register.SetResult(new RegistrationReceipt(10, null, "Bahar Konseri", new DateOnly(2027, 4, 17), position));
}

/// <summary>Account changes stay pending; the account list is answered at once, or refused when <see cref="Expired"/> is set.</summary>
internal sealed class ControlledAccountService : IAccountService
{
    public List<AccountSummary> Accounts { get; } =
    [
        new(1, "admin", UserRole.Admin, false),
        new(2, "ornek", UserRole.User, false),
    ];

    public bool Expired { get; set; }

    public int ChangeCalls { get; private set; }

    public List<(int UserId, string Change)> Changes { get; } = [];

    public Task<IReadOnlyList<AccountSummary>> GetAccountsAsync(UserSession session, CancellationToken cancellationToken = default) =>
        Expired
            ? Task.FromException<IReadOnlyList<AccountSummary>>(new SessionExpiredException())
            : Task.FromResult<IReadOnlyList<AccountSummary>>(Accounts);

    public Task SetEnabledAsync(UserSession session, int userId, bool enabled, CancellationToken cancellationToken = default)
    {
        ChangeCalls++;
        Changes.Add((userId, enabled ? "enable" : "disable"));
        return new TaskCompletionSource().Task;
    }

    public Task ChangeRoleAsync(UserSession session, int userId, UserRole role, CancellationToken cancellationToken = default)
    {
        ChangeCalls++;
        Changes.Add((userId, role.ToString()));
        return new TaskCompletionSource().Task;
    }
}

/// <summary>Check-ins stay pending until the test answers them; counters are answered by the test, in any order.</summary>
internal sealed class ControlledCheckInService : ICheckInService
{
    public List<(int EventId, string? Code, TaskCompletionSource<CheckInResult> Answer)> CheckIns { get; } = [];

    public List<(int EventId, TaskCompletionSource<CheckInCount> Answer)> CountRequests { get; } = [];

    public Task<CheckInResult> CheckInAsync(UserSession session, int eventId, string? code, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<CheckInResult>();
        CheckIns.Add((eventId, code, answer));
        return answer.Task;
    }

    public Task<CheckInCount> GetCountAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        var answer = new TaskCompletionSource<CheckInCount>();
        CountRequests.Add((eventId, answer));
        return answer.Task;
    }
}

/// <summary>Answers confirmations with yes and records every message instead of showing a dialog.</summary>
internal sealed class RecordingPrompts : IUserPrompts
{
    public List<string> Messages { get; } = [];

    public bool Confirm(string question)
    {
        Messages.Add(question);
        return true;
    }

    public void Inform(string message) => Messages.Add(message);

    public void Warn(string message) => Messages.Add(message);
}

internal sealed class NoFormsFactory : IFormFactory
{
    public T Create<T>(params object[] arguments)
        where T : Form => throw new InvalidOperationException("Tests do not open other forms.");
}

internal static class Sessions
{
    public static UserSession User { get; } = new(2, "ornek", UserRole.User);

    public static UserSession Admin { get; } = new(1, "admin", UserRole.Admin);

    public static RegistrationDetails Registration(int id) =>
        new(id, 1, "Bahar Konseri", new DateOnly(2027, 4, 17), 1, "Deniz Örnek", "ETK-ABCDEFGHJK");
}
