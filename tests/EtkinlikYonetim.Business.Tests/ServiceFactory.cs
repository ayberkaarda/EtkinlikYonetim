using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>
/// The services with their real validators, as the application's container builds them. Audit events
/// go to <c>audit</c>, or nowhere when it is not given.
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

    public static EventService Event(IEventRepository events, TimeProvider time, IAuditLog? audit = null) =>
        new(events, time, new EventInputValidator(), audit ?? NullAuditLog.Instance);

    public static ParticipantService Participant(IParticipantRepository participants, IAuditLog? audit = null) =>
        new(participants, new ParticipantInputValidator(), audit ?? NullAuditLog.Instance);

    public static RegistrationService Registration(
        IRegistrationRepository registrations,
        IParticipantRepository participants,
        IEventRepository events,
        TimeProvider time,
        IAuditLog? audit = null) =>
        new(
            registrations,
            participants,
            events,
            time,
            new RegistrationRequestInputValidator(),
            new RegistrationTargetInputValidator(),
            new CancellationInputValidator(),
            audit ?? NullAuditLog.Instance);
}
