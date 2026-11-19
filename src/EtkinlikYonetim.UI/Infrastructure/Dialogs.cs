namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// The single place that opens message windows. Questions, and messages that have to outlive the window
/// they belong to (an ended session, a startup failure), use the themed <see cref="MessageDialog"/>;
/// results and validation messages of a screen are shown inline instead (see <see cref="IStatusSurface"/>).
/// </summary>
internal static class Dialogs
{
    /// <summary>
    /// Replaces the message window; the test assembly and the offscreen screenshot tool set it so that no
    /// window can open. Always <see langword="null"/> in the application.
    /// </summary>
    internal static Func<string, string, MessageBoxButtons, MessageBoxIcon, DialogResult>? Override { get; set; }

    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None)
    {
        if (Override is { } replacement)
        {
            return replacement(text, caption, buttons, icon);
        }

        // A window needs an STA thread; errors reported from a worker thread fall back to the system box.
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            return MessageBox.Show(owner, text, caption, buttons, icon);
        }

        using var dialog = new MessageDialog(text, caption, buttons, icon);
        return dialog.ShowDialog(owner);
    }
}

/// <summary>
/// A window handle that is not an <see cref="IStatusSurface"/>: messages reported to it open a message window
/// owned by that window instead of appearing inline, for a window that is about to close.
/// </summary>
internal sealed class DialogOwner(IWin32Window window) : IWin32Window
{
    public IntPtr Handle => window.Handle;
}
