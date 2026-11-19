using System.Reflection;
using System.Runtime.ExceptionServices;

namespace EtkinlikYonetim.Data.Tests;

/// <summary>
/// A repository that runs a step of the test right before some of its methods start, as another
/// application instance's change committed in that moment would land: after the service's own checks,
/// before the repository's transaction.
/// </summary>
#pragma warning disable CA1852 // DispatchProxy needs a class it can derive from.
internal class BeforeCall<T> : DispatchProxy
#pragma warning restore CA1852
    where T : class
{
    private T _inner = null!;
    private Func<string, bool> _when = null!;
    private Func<Task> _step = null!;

    /// <summary>Wraps <paramref name="inner"/>; <paramref name="step"/> runs before each method whose name <paramref name="when"/> accepts.</summary>
    public static T Wrap(T inner, Func<string, bool> when, Func<Task> step)
    {
        var proxy = Create<T, BeforeCall<T>>();
        var before = (BeforeCall<T>)(object)proxy;
        before._inner = inner;
        before._when = when;
        before._step = step;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (_when(targetMethod.Name))
        {
            _step().GetAwaiter().GetResult();
        }

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }
}
