namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>Asks where to save a file. Tests replace it so no dialog opens.</summary>
internal interface ISaveFilePrompt
{
    /// <summary>The chosen path, or <see langword="null"/> when the user cancelled.</summary>
    string? AskSavePath(string suggestedName, string filter);
}

internal sealed class SaveFileDialogPrompt(IWin32Window owner) : ISaveFilePrompt
{
    public string? AskSavePath(string suggestedName, string filter)
    {
        using var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            Filter = filter,
            OverwritePrompt = true,
            AddExtension = true,
            Title = AppText.Title,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}

/// <summary>Writes exported files so that a half written file never replaces an existing one.</summary>
internal static class ExportFileWriter
{
    /// <summary>
    /// Writes <paramref name="content"/> to a new temporary file in the target's folder, flushes it to the
    /// disk and only then moves it over <paramref name="path"/>. A failure leaves the target as it was and
    /// removes the temporary file.
    /// </summary>
    public static async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var target = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(target) ?? throw new ArgumentException("The path has no folder.", nameof(path));
        var temporary = Path.Combine(folder, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}