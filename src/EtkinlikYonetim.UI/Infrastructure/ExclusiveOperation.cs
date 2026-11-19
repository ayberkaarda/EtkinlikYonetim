namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// Guards a UI operation (such as a save) so it cannot run again while a previous run is still
/// pending. The "running" flag is set synchronously, before the first await, so a second click that
/// arrives while the first one awaits is ignored instead of starting a duplicate write.
/// </summary>
/// <remarks>
/// Intended for the UI thread only; it has no dependencies besides Windows Forms and can be copied
/// into other projects as is.
/// </remarks>
public sealed class ExclusiveOperation
{
    /// <summary>Whether an operation started through <see cref="RunAsync"/> has not finished yet.</summary>
    public bool IsRunning { get; private set; }

    private Control? _focusAfterUnlock;
    private Form? _closeAfterRun;

    /// <summary>
    /// Keeps <paramref name="form"/> open while a run is pending: a close request (X, Alt+F4, Esc) is
    /// postponed until the run finished, so whoever opened the form reads its final outcome. A pending
    /// shutdown of Windows is not held up.
    /// </summary>
    public void HoldClosingOf(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.FormClosing += (_, e) =>
        {
            if (IsRunning && e.CloseReason is not (CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing))
            {
                e.Cancel = true;
                _closeAfterRun = form;
            }
        };
    }

    /// <summary>
    /// Drops a close request that arrived while the pending run was working, so the form stays open after
    /// the run: for a result the user still has to read, such as a single use code shown only once.
    /// Returns whether a close request was dropped.
    /// </summary>
    public bool DropPendingClose()
    {
        var pending = _closeAfterRun is not null;
        _closeAfterRun = null;
        return pending;
    }

    /// <summary>
    /// Chooses the control that receives the focus once the guarded controls are enabled again. A
    /// disabled control cannot take the focus, so the action only records the target and the focus is
    /// moved after the unlock; a closed form or a disposed control is skipped. Only the run that is
    /// pending can record a target, because an ignored call never reaches its action.
    /// </summary>
    public void FocusAfterUnlock(Control? target) => _focusAfterUnlock = target;

    /// <summary>
    /// Runs <paramref name="action"/> unless another run is pending. The given controls are disabled
    /// for the duration and their previous <see cref="Control.Enabled"/> state is restored afterwards,
    /// also when the action fails. Exceptions are propagated to the caller.
    /// </summary>
    /// <returns><see langword="false"/> when the call was ignored because a run is pending.</returns>
    public async Task<bool> RunAsync(Func<Task> action, params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(controls);

        if (IsRunning)
        {
            return false;
        }

        IsRunning = true;
        _focusAfterUnlock = null;
        var previousStates = new bool[controls.Length];
        try
        {
            for (var i = 0; i < controls.Length; i++)
            {
                previousStates[i] = controls[i].Enabled;
                controls[i].Enabled = false;
            }

            await action().ConfigureAwait(true);
            return true;
        }
        finally
        {
            for (var i = 0; i < controls.Length; i++)
            {
                if (!controls[i].IsDisposed)
                {
                    controls[i].Enabled = previousStates[i];
                }
            }

            IsRunning = false;
            var target = _focusAfterUnlock;
            _focusAfterUnlock = null;
            if (target is { IsDisposed: false, Enabled: true } && target.FindForm() is { IsDisposed: false })
            {
                target.Select();
            }

            // Posted, so the caller's own continuation (for example a dialog result) runs first.
            var postponed = _closeAfterRun;
            _closeAfterRun = null;
            if (postponed is { IsDisposed: false, IsHandleCreated: true })
            {
                postponed.BeginInvoke(() =>
                {
                    if (!postponed.IsDisposed)
                    {
                        postponed.Close();
                    }
                });
            }
        }
    }
}
