namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>The single place that opens message boxes.</summary>
internal static class Dialogs
{
    /// <summary>
    /// Replaces the message box; the test assembly sets it so that no window can open. Always
    /// <see langword="null"/> in the application.
    /// </summary>
    internal static Func<string, string, MessageBoxButtons, MessageBoxIcon, DialogResult>? Override { get; set; }

    public static DialogResult Show(
        IWin32Window? owner,
        string text,
        string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK,
        MessageBoxIcon icon = MessageBoxIcon.None) =>
        Override is { } replacement
            ? replacement(text, caption, buttons, icon)
            : MessageBox.Show(owner, text, caption, buttons, icon);
}
