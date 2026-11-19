using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EtkinlikYonetim.Business;

public static class BusinessServiceCollectionExtensions
{
    /// <summary>Registers the business services. Repositories must be registered separately.</summary>
    public static IServiceCollection AddBusinessServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher>(_ => new PasswordHasher());
        services.TryAddSingleton(SetupKeyThrottle.Default);
        services.TryAddSingleton(_ => SetupKeyEnvironment.FromProcess());
        services.AddSingleton<IValidator<AccountInput>, AccountInputValidator>();
        services.AddSingleton<IValidator<SignInInput>, SignInInputValidator>();
        services.AddSingleton<IValidator<ParticipantInput>, ParticipantInputValidator>();
        services.AddSingleton<IValidator<EventInput>, EventInputValidator>();
        services.AddSingleton<IValidator<RegistrationRequestInput>, RegistrationRequestInputValidator>();
        services.AddSingleton<IValidator<RegistrationTargetInput>, RegistrationTargetInputValidator>();
        services.AddSingleton<IValidator<CancellationInput>, CancellationInputValidator>();
        services.AddSingleton<SetupKeyGate>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISetupService, SetupService>();
        services.AddSingleton<IEventService, EventService>();
        services.AddSingleton<IParticipantService, ParticipantService>();
        services.AddSingleton<IRegistrationService, RegistrationService>();
        return services;
    }
}
