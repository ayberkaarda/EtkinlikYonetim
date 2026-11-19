using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>
/// The services with their real validators, as the application's container builds them. Audit events
/// go to <c>audit</c>, or nowhere when it is not given. Sessions are accepted as they are unless a
/// <c>sessions</c> guard is given; <see cref="SessionGuardTests"/> covers the real one.
/// </summary>
internal static class ServiceFactory
{
    public static AuthService Auth(IUserRepository users, IPasswordHasher hasher, TimeProvider time, IAuditLog? audit = null) =>
        new(users, hasher, time, new SignInInputValidator(), new AccountInputValidator(), audit ?? NullAuditLog.Instance);

    public static SetupService Setup(
        IUserRepository users,
        IPasswordHasher hasher,
        SetupKeyGate gate,
        SetupKeyEnvironment environment,
        IAuditLog? audit = null) =>
        new(users, hasher, gate, environment, new AccountInputValidator(), audit ?? NullAuditLog.Instance);

    public static EventService Event(IEventRepository events, TimeProvider time, IAuditLog? audit = null, ISessionGuard? sessions = null) =>
        new(events, time, new EventInputValidator(), sessions ?? AcceptAllSessions.Instance, audit ?? NullAuditLog.Instance);

    public static ParticipantService Participant(IParticipantRepository participants, IAuditLog? audit = null, ISessionGuard? sessions = null) =>
        new(participants, new ParticipantInputValidator(), sessions ?? AcceptAllSessions.Instance, audit ?? NullAuditLog.Instance);

    public static RegistrationService Registration(
        IRegistrationRepository registrations,
        IParticipantRepository participants,
        IEventRepository events,
        TimeProvider time,
        IAuditLog? audit = null,
        ISessionGuard? sessions = null) =>
        new(
            registrations,
            participants,
            events,
            time,
            new RegistrationRequestInputValidator(),
            new RegistrationTargetInputValidator(),
            new CancellationInputValidator(),
            sessions ?? AcceptAllSessions.Instance,
            audit ?? NullAuditLog.Instance);

    public static AccountService Account(IUserRepository users, IAuditLog? audit = null, ISessionGuard? sessions = null) =>
        new(users, sessions ?? AcceptAllSessions.Instance, audit ?? NullAuditLog.Instance);

    /// <summary>The real guard over <paramref name="users"/>.</summary>
    public static SessionGuard Guard(IUserRepository users, IAuditLog? audit = null) => new(users, audit ?? NullAuditLog.Instance);

    /// <summary>Accepts every session, as if the database confirmed it.</summary>
    private sealed class AcceptAllSessions : ISessionGuard
    {
        public static readonly AcceptAllSessions Instance = new();

        public Task EnsureCurrentAsync(UserSession session, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
