using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>Which anonymous audit events the services raise; an event carries only its kind and a role.</summary>
public sealed class AuditTests
{
    private const string UserName = "ornek";
    private const string Password = "Gizli.Sifre1";
    private const string NationalId = "10000000146";
    private static readonly DateTime Now = new(2026, 3, 2, 10, 10, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 3, 2);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IEventRepository> _events = new(MockBehavior.Strict);
    private readonly Mock<IParticipantRepository> _participants = new(MockBehavior.Strict);
    private readonly Mock<IRegistrationRepository> _registrations = new(MockBehavior.Strict);
    private readonly PasswordHasher _hasher = new(iterations: 1_000);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(Now));
    private readonly RecordingAudit _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The written codes are an interface of the log file: every event is pinned to its exact code and
    /// numeric value, so renaming, renumbering or adding an event without a code fails here.
    /// </summary>
    public static TheoryData<AuditEvent, int, string> ExpectedCodes => new()
    {
        { AuditEvent.SignInSucceeded, 1, "auth.sign_in.succeeded" },
        { AuditEvent.SignInFailed, 2, "auth.sign_in.failed" },
        { AuditEvent.PasswordUpgraded, 3, "auth.password.upgraded" },
        { AuditEvent.AccountRegistered, 4, "auth.account.registered" },
        { AuditEvent.FirstAdminCreated, 5, "setup.first_admin.created" },
        { AuditEvent.SetupKeyRefused, 6, "setup.key.refused" },
        { AuditEvent.EventCreated, 7, "event.created" },
        { AuditEvent.ParticipantAdded, 8, "participant.added" },
        { AuditEvent.RegistrationCreated, 9, "registration.created" },
        { AuditEvent.RegistrationEventFull, 10, "registration.event_full" },
        { AuditEvent.RegistrationCancelled, 11, "registration.cancelled" },
        { AuditEvent.AccessDenied, 12, "access.denied" },
        { AuditEvent.SessionRejected, 13, "auth.session.rejected" },
        { AuditEvent.AccountDisabled, 14, "admin.account.disabled" },
        { AuditEvent.AccountEnabled, 15, "admin.account.enabled" },
        { AuditEvent.RoleChanged, 16, "admin.account.role_changed" },
        { AuditEvent.RegistrationWaitlisted, 17, "registration.waitlisted" },
        { AuditEvent.RegistrationPromoted, 18, "registration.promoted" },
        { AuditEvent.EventUpdated, 19, "event.updated" },
        { AuditEvent.EventCancelled, 20, "event.cancelled" },
        { AuditEvent.TicketCheckedIn, 21, "ticket.checked_in" },
        { AuditEvent.TicketAlreadyUsed, 22, "ticket.already_used" },
        { AuditEvent.TicketRejected, 23, "ticket.rejected" },
        { AuditEvent.ParticipantListExported, 24, "participant.list_exported" },
        { AuditEvent.AccountRegistrationRefused, 25, "auth.account.registration_refused" },
        { AuditEvent.RegistrationRefused, 26, "registration.refused" },
        { AuditEvent.RegistrationCancelRefused, 27, "registration.cancel_refused" },
    };

    [Theory]
    [MemberData(nameof(ExpectedCodes))]
    public void Code_OfEveryEvent_IsTheFixedValue(AuditEvent auditEvent, int value, string code)
    {
        Assert.Equal(value, (int)auditEvent);
        Assert.Equal(code, AuditCodes.Of(auditEvent));
    }

    [Fact]
    public void Codes_CoverEveryEventExactlyOnceAndCarryNoData()
    {
        var expected = ExpectedCodes.Select(row => (AuditEvent)row.Data.Item1).ToList();

        Assert.Equal(expected.Order(), Enum.GetValues<AuditEvent>().Order());
        Assert.Equal(expected.Count, expected.Select(AuditCodes.Of).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Enum.GetValues<AuditEvent>(), e => Assert.Matches("^[a-z_]+(\\.[a-z_]+)+$", AuditCodes.Of(e)));
        Assert.DoesNotContain(Enum.GetValues<AuditEvent>().Select(AuditCodes.Of), c => c.Contains("lock", StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => AuditCodes.Of((AuditEvent)99));
    }

    [Theory]
    [InlineData(UserRole.Admin, "admin")]
    [InlineData(UserRole.User, "user")]
    [InlineData((UserRole)7, "unknown")]
    public void Code_OfRole_IsTheFixedValue(UserRole role, string code) => Assert.Equal(code, AuditCodes.Of(role));

    [Fact]
    public void Code_OfNoRole_IsNone() => Assert.Equal("none", AuditCodes.Of(null));

    /// <summary>
    /// Missing input, an unknown account, a wrong password, a locked account (of either role) and a lock
    /// placed by a concurrent failure all leave the same single entry, without a role.
    /// </summary>
    [Fact]
    public async Task SignIn_EveryKindOfFailure_RecordsTheSameCodeWithoutARole()
    {
        var hash = _hasher.Hash(Password);
        _users.Setup(u => u.FindByUserNameAsync("bilinmeyen", It.IsAny<CancellationToken>())).ReturnsAsync((AccountCredentials?)null);
        _users.Setup(u => u.FindByUserNameAsync("kilitli", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(8, "kilitli", UserRole.Admin, hash, PasswordFormat.Pbkdf2, 0, Now.AddMinutes(5)));
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(7, UserName, UserRole.User, hash, PasswordFormat.Pbkdf2, 0, null));
        _users.Setup(u => u.RecordFailedLoginAsync(7, LoginPolicy.MaxFailedAttempts, Now + LoginPolicy.LockoutDuration, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), Now, null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var auth = ServiceFactory.Auth(_users.Object, _hasher, _time, _audit);

        var messages = new List<string>
        {
            (await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("bilinmeyen", Password, Ct))).Message,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync("kilitli", Password, Ct))).Message,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync(UserName, "yanlis", Ct))).Message,
            (await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync(UserName, Password, Ct))).Message, // locked concurrently
        };
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.SignInAsync(" ", Password, Ct));

        Assert.All(messages, m => Assert.Equal(AuthService.CredentialsInvalid, m));
        Assert.Equal(Enumerable.Repeat((AuditEvent.SignInFailed, (UserRole?)null), 5), _audit.Events);
    }

    [Fact]
    public async Task SignIn_SuccessWithAWeakerHash_RecordsTheUpgradeAndTheSuccessWithTheRole()
    {
        _users.Setup(u => u.FindByUserNameAsync(UserName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(7, UserName, UserRole.Admin, Password, PasswordFormat.LegacyPlainText, 0, null));
        _users.Setup(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), Now, It.IsNotNull<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await ServiceFactory.Auth(_users.Object, _hasher, _time, _audit).SignInAsync(UserName, Password, Ct);

        Assert.Equal([(AuditEvent.PasswordUpgraded, UserRole.Admin), (AuditEvent.SignInSucceeded, UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task Register_RecordsATakenNameAndTheNewAccount()
    {
        _users.Setup(u => u.UserNameExistsAsync("yeni", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.UserNameExistsAsync("alinmis", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var auth = ServiceFactory.Auth(_users.Object, _hasher, _time, _audit);

        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("alinmis", Password, Password, Ct));
        await Assert.ThrowsAsync<BusinessRuleException>(() => auth.RegisterAsync("yeni", Password, "baska", Ct));
        await auth.RegisterAsync("yeni", Password, Password, Ct);

        Assert.Equal([(AuditEvent.AccountRegistrationRefused, UserRole.User), (AuditEvent.AccountRegistered, (UserRole?)UserRole.User)], _audit.Events);
    }

    [Fact]
    public async Task Setup_RecordsARefusedKeyWithoutARoleAndTheCreatedAdmin()
    {
        _users.SetupSequence(u => u.AddFirstAdminAsync(It.IsAny<User>(), It.IsAny<SetupKeyAttempt?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FirstAdminResult(FirstAdminStatus.NotAuthorized, 0))
            .ReturnsAsync(new FirstAdminResult(FirstAdminStatus.Created, 1));
        var setup = ServiceFactory.Setup(
            _users.Object,
            _hasher,
            new SetupKeyGate(_time, new SetupKeyThrottle(TimeSpan.Zero)),
            new SetupKeyEnvironment(null),
            _audit);

        await Assert.ThrowsAsync<BusinessRuleException>(() => setup.CreateFirstAdminAsync("yanlis", "kurucu", Password, Password, Ct));
        await setup.CreateFirstAdminAsync(null, "kurucu", Password, Password, Ct);

        Assert.Equal([(AuditEvent.SetupKeyRefused, null), (AuditEvent.FirstAdminCreated, UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task AdminOnlyOperations_ByAUser_RecordAccessDeniedWithTheUserRole()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => ServiceFactory.Event(_events.Object, _time, _audit).CreateAsync(Sessions.User, "Konser", Now, 10, Ct));
        await Assert.ThrowsAsync<AccessDeniedException>(() => ServiceFactory.Participant(_participants.Object, _audit).GetByEventAsync(Sessions.User, 1, Ct));

        Assert.Equal([(AuditEvent.AccessDenied, UserRole.User), (AuditEvent.AccessDenied, UserRole.User)], _audit.Events);
        _events.VerifyNoOtherCalls();
        _participants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EventAndParticipant_AreRecordedWhenStored()
    {
        _events.Setup(e => e.AddAsync(It.IsAny<Actor>(), It.IsAny<ScheduledEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);
        _participants.Setup(p => p.FindByNationalIdAsync(NationalId, Sessions.User.UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _participants.Setup(p => p.AddAsync(It.IsAny<Actor>(), It.IsAny<Participant>(), It.IsAny<CancellationToken>())).ReturnsAsync(9);

        await ServiceFactory.Event(_events.Object, _time, _audit).CreateAsync(Sessions.Admin, "Konser", Now, 10, Ct);
        await ServiceFactory.Participant(_participants.Object, _audit).AddAsync(Sessions.User, NationalId, "Deniz Örnek", "d@example.com", Ct);

        Assert.Equal([(AuditEvent.EventCreated, UserRole.Admin), (AuditEvent.ParticipantAdded, UserRole.User)], _audit.Events);
    }

    [Fact]
    public async Task Registration_RecordsTheTicketTheWaitingPlaceTheDenialTheCancellationAndThePromotion()
    {
        _participants.Setup(p => p.FindByNationalIdAsync(NationalId, Sessions.User.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ParticipantRecord(1, NationalId, "Deniz Örnek", null, Sessions.User.UserId)]);
        _events.Setup(e => e.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new EventSummary(1, "Konser", Today, 2, 1));
        _events.Setup(e => e.GetByIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(new EventSummary(2, "Dolu", Today, 2, 2));
        _registrations.Setup(r => r.AddAsync(It.IsAny<Actor>(), 1, 1, Today, Now, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOutcome(5, RegistrationStatus.Active, null));
        _registrations.Setup(r => r.AddAsync(It.IsAny<Actor>(), 1, 2, Today, Now, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOutcome(6, RegistrationStatus.Waiting, 1));
        _registrations.Setup(r => r.GetOwnershipAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationOwnership(5, 1, Today, Sessions.User.UserId, RegistrationStatus.Active));
        _registrations.Setup(r => r.CancelAsync(It.IsAny<Actor>(), 5, Sessions.User.UserId, Today, Now, It.IsAny<Func<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CancellationOutcome.CancelledAndPromoted);
        var service = ServiceFactory.Registration(_registrations.Object, _participants.Object, _events.Object, _time, _audit);

        await service.RegisterAsync(Sessions.User, 1, NationalId, Ct);
        await service.RegisterAsync(Sessions.User, 2, NationalId, Ct); // full: waiting list
        await Assert.ThrowsAsync<AccessDeniedException>(() => service.CancelAsync(Sessions.OtherUser, 5, Ct));
        await service.CancelAsync(Sessions.User, 5, Ct);

        Assert.Equal(
            [
                (AuditEvent.RegistrationCreated, UserRole.User),
                (AuditEvent.RegistrationWaitlisted, UserRole.User),
                (AuditEvent.RegistrationCancelRefused, UserRole.User),
                (AuditEvent.RegistrationCancelled, UserRole.User),
                (AuditEvent.RegistrationPromoted, UserRole.User),
            ],
            _audit.Events);
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<(AuditEvent Event, UserRole? Role)> Events { get; } = [];

        public void Record(AuditEvent auditEvent, UserRole? role) => Events.Add((auditEvent, role));
    }
}
