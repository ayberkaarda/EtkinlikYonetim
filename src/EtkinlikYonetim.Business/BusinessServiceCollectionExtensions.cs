using EtkinlikYonetim.Business.Security;
using EtkinlikYonetim.Business.Services;
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
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IEventService, EventService>();
        services.AddSingleton<IParticipantService, ParticipantService>();
        services.AddSingleton<IRegistrationService, RegistrationService>();
        return services;
    }
}
