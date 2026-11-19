using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>The services with their real validators, as the application's container builds them.</summary>
internal static class ServiceFactory
{
    public static AuthService Auth(IUserRepository users, IPasswordHasher hasher, TimeProvider time) =>
        new(users, hasher, time, new SignInInputValidator(), new AccountInputValidator());

    public static SetupService Setup(IUserRepository users, IPasswordHasher hasher, SetupKeyGate gate, SetupKeyEnvironment environment) =>
        new(users, hasher, gate, environment, new AccountInputValidator());

    public static EventService Event(IEventRepository events, TimeProvider time) =>
        new(events, time, new EventInputValidator());

    public static ParticipantService Participant(IParticipantRepository participants) =>
        new(participants, new ParticipantInputValidator());

    public static RegistrationService Registration(
        IRegistrationRepository registrations,
        IParticipantRepository participants,
        IEventRepository events,
        TimeProvider time) =>
        new(
            registrations,
            participants,
            events,
            time,
            new RegistrationRequestInputValidator(),
            new RegistrationTargetInputValidator(),
            new CancellationInputValidator());
}
