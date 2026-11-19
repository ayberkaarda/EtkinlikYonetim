namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Dialogs a form shows during an operation. Tests replace them so no window opens.</summary>
internal interface IUserPrompts
{
    bool Confirm(string question);

    void Inform(string message);

    void Warn(string message);
}

internal sealed class MessageBoxPrompts(IWin32Window owner) : IUserPrompts
{
    public bool Confirm(string question) =>
        Dialogs.Show(owner, question, "Onay", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public void Inform(string message) =>
        Dialogs.Show(owner, message, AppText.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public void Warn(string message) =>
        Dialogs.Show(owner, message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
