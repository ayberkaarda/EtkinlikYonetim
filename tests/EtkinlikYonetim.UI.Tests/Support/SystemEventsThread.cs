using EtkinlikYonetim.UI.Tests.Support;
using Microsoft.Win32;

[assembly: AssemblyFixture(typeof(SystemEventsThread))]

namespace EtkinlikYonetim.UI.Tests.Support;

/// <summary>
/// Window handles of lists and date boxes subscribe to <see cref="SystemEvents"/>. The first subscriber
/// decides where its hidden window lives: on an STA test thread, which never pumps messages and ends after
/// the test, later broadcasts could block. Subscribing once from an MTA thread makes SystemEvents run its
/// own thread with a message loop instead.
/// </summary>
public sealed class SystemEventsThread
{
    public SystemEventsThread()
    {
        var thread = new Thread(() => SystemEvents.UserPreferenceChanged += Ignore) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }

    private static void Ignore(object? sender, UserPreferenceChangedEventArgs e)
    {
    }
}
