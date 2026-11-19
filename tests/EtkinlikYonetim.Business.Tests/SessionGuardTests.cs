using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>
/// An open session is checked against the database at the time of every operation: the role it was
/// issued with is not trusted once the account was demoted, disabled or deleted.
/// </summary>
public sealed class SessionGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _users = new(MockBehavior.Strict);
    private readonly Mock<IEventRepository> _events = new(MockBehavior.Strict);
    private readonly Mock<IParticipantRepository> _participants = new(MockBehavior.Strict);
    private readonly Mock<IRegistrationRepository> _registrations = new(MockBehavior.Strict);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingAudit _audit = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DemotedAdminSession_CannotManageEvents()
    {
        State(Sessions.Admin.UserId, new SessionState(UserRole.User, false));

        await Assert.ThrowsAsync<SessionExpiredException>(
            () => Events().CreateAsync(Sessions.Admin, "Konser", Now.Date.AddDays(3), 10, Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(
            () => Participants().GetByEventAsync(Sessions.Admin, 1, Ct));

        _events.VerifyNoOtherCalls();
        _participants.VerifyNoOtherCalls();
        Assert.Equal([(AuditEvent.SessionRejected, UserRole.Admin), (AuditEvent.SessionRejected, UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task DeletedAdminSession_CannotManageEventsOrAccounts()
    {
        State(Sessions.Admin.UserId, null);

        await Assert.ThrowsAsync<SessionExpiredException>(
            () => Events().CreateAsync(Sessions.Admin, "Konser", Now.Date.AddDays(3), 10, Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(
            () => ServiceFactory.Account(_users.Object, _audit, Guard()).SetEnabledAsync(Sessions.Admin, 2, false, Ct));

        _events.VerifyNoOtherCalls();
        _users.Verify(u => u.SetDisabledAsync(It.IsAny<Actor>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisabledAccountSession_CannotCancelOrRegisterOrList()
    {
        State(Sessions.User.UserId, new SessionState(UserRole.User, true));
        var service = ServiceFactory.Registration(_registrations.Object, _participants.Object, _events.Object, _time, _audit, Guard());

        await Assert.ThrowsAsync<SessionExpiredException>(() => service.CancelAsync(Sessions.User, 5, Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(() => service.RegisterAsync(Sessions.User, 1, "10000000146", Ct));
        await Assert.ThrowsAsync<SessionExpiredException>(() => service.GetMyRegistrationsAsync(Sessions.User, Ct));

        _registrations.VerifyNoOtherCalls();
        _participants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DisabledAdminSession_CannotListParticipants()
    {
        State(Sessions.Admin.UserId, new SessionState(UserRole.Admin, true));

        await Assert.ThrowsAsync<SessionExpiredException>(() => Participants().GetByEventAsync(Sessions.Admin, 1, Ct));

        _participants.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UserSessionOfAnAccountPromotedSince_IsRefusedToo()
    {
        // The session was issued as a user; the account is an admin now. The session has to be renewed.
        State(Sessions.User.UserId, new SessionState(UserRole.Admin, false));

        await Assert.ThrowsAsync<SessionExpiredException>(() => Events().GetEventsAsync(Sessions.User, Ct));
    }

    /// <summary>
    /// The account has the session's role and is enabled again, but its access changed since the session
    /// was issued (disabled and enabled, or demoted and promoted): the session version differs, so the
    /// session stays refused.
    /// </summary>
    [Fact]
    public async Task SessionOfAnAccountChangedAndRestoredSince_IsRefused()
    {
        State(Sessions.Admin.UserId, new SessionState(UserRole.Admin, false, SessionVersion: 2));

        await Assert.ThrowsAsync<SessionExpiredException>(() => Events().GetEventsAsync(Sessions.Admin, Ct));

        _events.VerifyNoOtherCalls();
        Assert.Equal([(AuditEvent.SessionRejected, UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task CurrentAdminSession_IsAccepted()
    {
        State(Sessions.Admin.UserId, new SessionState(UserRole.Admin, false));
        _events.Setup(e => e.AddAsync(It.IsAny<Actor>(), It.IsAny<ScheduledEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(4);

        Assert.Equal(4, await Events().CreateAsync(Sessions.Admin, "Konser", Now.Date.AddDays(3), 10, Ct));
        Assert.Equal([(AuditEvent.EventCreated, UserRole.Admin)], _audit.Events);
    }

    [Fact]
    public async Task UserSession_AskingForAnAdminOperation_IsDeniedBeforeTheDatabaseIsAsked()
    {
        await Assert.ThrowsAsync<AccessDeniedException>(() => Events().CreateAsync(Sessions.User, "Konser", Now.Date.AddDays(3), 10, Ct));

        _users.VerifyNoOtherCalls();
        Assert.Equal([(AuditEvent.AccessDenied, UserRole.User)], _audit.Events);
    }

    /// <summary>A disabled account is refused at sign-in with the answer and the audit entry of a wrong password.</summary>
    [Fact]
    public async Task SignIn_DisabledAccount_IsRefusedLikeAWrongPassword()
    {
        var hasher = new PasswordHasher(iterations: 1_000);
        _users.Setup(u => u.FindByUserNameAsync("ornek", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCredentials(2, "ornek", UserRole.User, hasher.Hash("Gizli.Sifre1"), PasswordFormat.Pbkdf2, 0, null, Disabled: true));

        var refused = await Assert.ThrowsAsync<BusinessRuleException>(
            () => ServiceFactory.Auth(_users.Object, hasher, _time, _audit).SignInAsync("ornek", "Gizli.Sifre1", Ct));

        Assert.Equal(AuthService.CredentialsInvalid, refused.Message);
        Assert.Equal([(AuditEvent.SignInFailed, (UserRole?)null)], _audit.Events);
        _users.Verify(u => u.RecordSuccessfulLoginAsync(It.IsAny<AccountCredentials>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void State(int userId, SessionState? state) =>
        _users.Setup(u => u.GetSessionStateAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

    private SessionGuard Guard() => ServiceFactory.Guard(_users.Object, _audit);

    private EventService Events() => ServiceFactory.Event(_events.Object, _time, _audit, Guard());

    private ParticipantService Participants() => ServiceFactory.Participant(_participants.Object, _audit, Guard());
}

internal sealed class RecordingAudit : IAuditLog
{
    public List<(AuditEvent Event, UserRole? Role)> Events { get; } = [];

    public void Record(AuditEvent auditEvent, UserRole? role) => Events.Add((auditEvent, role));
}
