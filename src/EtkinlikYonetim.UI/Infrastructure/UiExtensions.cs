using EtkinlikYonetim.Business;

namespace EtkinlikYonetim.UI.Infrastructure;

internal static class UiExtensions
{
    /// <summary>
    /// Runs an asynchronous UI action with a wait cursor. Business rule violations are shown as
    /// warnings; anything else is reported through <see cref="ErrorReporter"/>, inline on a screen with a
    /// message strip. A session the database no longer accepts is reported and then ended, which returns
    /// the application to the sign-in.
    /// </summary>
    /// <param name="reportTo">Where failures are reported instead of <paramref name="form"/>, for a window that closes when its action fails.</param>
    public static async Task RunAsync(this Form form, Func<Task> action, IWin32Window? reportTo = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(action);

        form.UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (SessionExpiredException ex)
        {
            ErrorReporter.Show(ex, form);
            SessionEnd.Request(form);
        }
#pragma warning disable CA1031 // UI boundary: every failure is reported to the user.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorReporter.Show(ex, reportTo ?? form);
        }
        finally
        {
            form.UseWaitCursor = false;
        }
    }

    /// <summary>
    /// Like <see cref="RunAsync"/>, but through <paramref name="operation"/>: while a run is pending a
    /// new call is ignored and <paramref name="controls"/> stay disabled, so a write cannot be started twice.
    /// A run that starts clears the previous inline message of the screen.
    /// </summary>
    public static Task<bool> RunExclusiveAsync(
        this Form form,
        ExclusiveOperation operation,
        Func<Task> action,
        params Control[] controls)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(action);

        return operation.RunAsync(
            () =>
            {
                (form as IStatusSurface)?.ClearStatus();
                return form.RunAsync(action);
            },
            controls);
    }

    /// <summary>Lets only digits (and editing keys) be typed, for national id fields.</summary>
    public static void AcceptDigitsOnly(this TextBox textBox)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Lets only letters (and editing keys) be typed, plus spaces when <paramref name="allowSpaces"/> is
    /// set; for user name and person name fields. The services check the same rules.
    /// </summary>
    public static void AcceptLettersOnly(this TextBox textBox, bool allowSpaces = false)
    {
        ArgumentNullException.ThrowIfNull(textBox);

        textBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsLetter(e.KeyChar) && !(allowSpaces && e.KeyChar == ' '))
            {
                e.Handled = true;
            }
        };
    }

    /// <summary>Replaces the items of a list control, keeping the update to a single repaint.</summary>
    public static void ReplaceItems<T>(this ListBox listBox, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(listBox);
        ArgumentNullException.ThrowIfNull(items);

        listBox.BeginUpdate();
        try
        {
            listBox.Items.Clear();
            foreach (var item in items)
            {
                listBox.Items.Add(item!);
            }
        }
        finally
        {
            listBox.EndUpdate();
        }
    }

    /// <inheritdoc cref="ReplaceItems{T}(ListBox, IEnumerable{T})"/>
    public static void ReplaceItems<T>(this ComboBox comboBox, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(items);

        comboBox.BeginUpdate();
        try
        {
            comboBox.Items.Clear();
            foreach (var item in items)
            {
                comboBox.Items.Add(item!);
            }
        }
        finally
        {
            comboBox.EndUpdate();
        }
    }
}
