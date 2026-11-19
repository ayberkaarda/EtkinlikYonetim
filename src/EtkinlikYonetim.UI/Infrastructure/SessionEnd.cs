namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>The main window of a signed-in account; when it closes with <see cref="SignedOut"/> the sign-in is shown again.</summary>
internal interface ISessionPanel
{
    /// <summary>Whether the session ended because the database no longer accepts it.</summary>
    bool SignedOut { get; }

    /// <summary>Marks the session as ended and closes the window once the running operation has finished.</summary>
    void EndSession();
}

/// <summary>Ends the session of the window chain a form belongs to.</summary>
internal static class SessionEnd
{
    /// <summary>
    /// Closes the dialogs between <paramref name="form"/> and the panel that owns them and ends the
    /// panel's session, so the application returns to the sign-in. A form outside any panel (as in the
    /// tests) is left as it is.
    /// </summary>
    public static void Request(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        // The participant form is a user's panel and also a dialog of the admin panel, so the outermost
        // panel of the chain is the one whose session ends; everything inside it closes as a dialog.
        var chain = new List<Form>();
        for (Form? current = form; current is not null; current = current.Owner)
        {
            chain.Add(current);
        }

        var panel = chain.LastOrDefault(f => f is ISessionPanel);
        foreach (var dialog in chain.Where(f => f != panel && f.Modal))
        {
            dialog.DialogResult = DialogResult.Cancel;
        }

        (panel as ISessionPanel)?.EndSession();
    }

    /// <summary>
    /// The common <see cref="ISessionPanel.EndSession"/>: posted to the message loop, so the running
    /// operation and the dialog it opened finish first.
    /// </summary>
    public static void Close(Form panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        if (panel.IsHandleCreated && !panel.IsDisposed)
        {
            panel.BeginInvoke(() =>
            {
                if (!panel.IsDisposed)
                {
                    panel.Close();
                }
            });
        }
    }
}
