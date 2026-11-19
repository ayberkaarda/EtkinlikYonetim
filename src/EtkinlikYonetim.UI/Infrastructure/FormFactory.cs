using Microsoft.Extensions.DependencyInjection;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Creates forms with their dependencies resolved from the container.</summary>
public interface IFormFactory
{
    /// <summary>
    /// Creates <typeparamref name="T"/>; <paramref name="arguments"/> supply constructor parameters that
    /// are not services, such as the signed-in session.
    /// </summary>
    T Create<T>(params object[] arguments)
        where T : Form;
}

internal sealed class FormFactory(IServiceProvider serviceProvider) : IFormFactory
{
    // ActivatorUtilities is used instead of registering forms as transients so the container
    // does not keep every opened form alive until the application exits.
    public T Create<T>(params object[] arguments)
        where T : Form => ActivatorUtilities.CreateInstance<T>(serviceProvider, arguments);
}
