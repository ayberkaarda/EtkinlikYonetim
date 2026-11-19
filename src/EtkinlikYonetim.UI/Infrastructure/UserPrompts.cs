using EtkinlikYonetim.UI.Theming;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Prompts a form shows during an operation. Tests replace them so no window opens.</summary>
internal interface IUserPrompts
{
    bool Confirm(string question);

    void Inform(string message);

    void Warn(string message);
}

/// <summary>Something that can show an inline result or validation message.</summary>
internal interface IStatusSurface
{
    void ShowStatus(StatusKind kind, string message);

    void ClearStatus();
}

/// <summary>Every prompt in a message window (the themed <see cref="MessageDialog"/>, through <see cref="Dialogs"/>).</summary>
internal sealed class MessageBoxPrompts(IWin32Window? owner) : IUserPrompts
{
    public bool Confirm(string question) =>
        Dialogs.Show(owner, question, "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void Inform(string message) =>
        Dialogs.Show(owner, message, AppText.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public void Warn(string message) =>
        Dialogs.Show(owner, message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

/// <summary>
/// Prompts of a screen: results and validation messages appear inline in its message strip, so nothing
/// takes the focus; questions are asked in a message window owned by the screen.
/// </summary>
internal sealed class ScreenPrompts(AppDialog screen) : IUserPrompts
{
    public bool Confirm(string question) =>
        Dialogs.Show(screen, question, "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void Inform(string message) => screen.ShowStatus(StatusKind.Success, message);

    public void Warn(string message) => screen.ShowStatus(StatusKind.Warning, message);
}
