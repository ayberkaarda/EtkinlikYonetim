using EtkinlikYonetim.Business.Exports;
using EtkinlikYonetim.UI.Infrastructure;
using EtkinlikYonetim.UI.Tests.Support;
using static EtkinlikYonetim.UI.Tests.Support.StaThread;

namespace EtkinlikYonetim.UI.Tests;

/// <summary>Saving the participant list: exclusive, through the replaceable save dialog, written by temporary file and move.</summary>
public sealed class ExportUiTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "etkinlik-export-" + Guid.NewGuid().ToString("N"));

    public ExportUiTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Export_WhilePending_IsNotSentTwiceAndLocksBothButtons() => Run(() =>
    {
        var exports = new ControlledExportService();
        using var form = Listing(exports, new FixedSavePrompt(Path.Combine(_directory, "liste.csv")));

        var first = form.ExportCsvAsync();
        var second = form.ExportHtmlAsync();

        Assert.Equal([("csv", 1)], exports.Requests.Select(r => (r.Format, r.EventId)));
        Assert.True(second.IsCompletedSuccessfully && !second.Result);
        Assert.False(first.IsCompleted);
        Assert.False(Find<Button>(form, "btnCsv").Enabled);
        Assert.False(Find<Button>(form, "btnHtml").Enabled);
    });

    [Fact]
    public async Task Export_WritesTheFileToTheChosenPath()
    {
        var path = Path.Combine(_directory, "liste.csv");
        File.WriteAllText(path, "eski");
        var exports = new ControlledExportService();
        var save = new FixedSavePrompt(path);
        var prompts = new RecordingPrompts();
        Task<bool>? run = null;
        Run(() =>
        {
            using var form = Listing(exports, save, prompts);
            run = form.ExportCsvAsync();
            exports.Requests[0].Answer.SetResult(new ExportFile("katilimcilar-1-20270417.csv", [1, 2, 3]));
        });

        Assert.True(await run!);
        Assert.Equal([("katilimcilar-1-20270417.csv", "CSV dosyası (*.csv)|*.csv")], save.Questions);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["Liste kaydedildi."], prompts.Messages);
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public void Export_CancelledDialog_WritesNothing() => Run(() =>
    {
        var exports = new ControlledExportService();
        var prompts = new RecordingPrompts();
        using var form = Listing(exports, new FixedSavePrompt(null), prompts);

        var run = form.ExportHtmlAsync();
        exports.Requests[0].Answer.SetResult(new ExportFile("katilimcilar-1-20270417.html", [1]));

        Assert.True(run.IsCompletedSuccessfully);
        Assert.Empty(Directory.GetFiles(_directory));
        Assert.Empty(prompts.Messages);
    });

    [Fact]
    public void Export_WithoutAnEvent_AsksForOne() => Run(() =>
    {
        var exports = new ControlledExportService();
        var prompts = new RecordingPrompts();
        using var form = new KatilimciGoruntule(new ControlledEventService(), new ControlledParticipantService(), exports, Sessions.Admin)
        {
            Prompts = prompts,
            SavePrompt = new FixedSavePrompt(null),
        };

        Assert.True(form.ExportCsvAsync().IsCompletedSuccessfully);

        Assert.Empty(exports.Requests);
        Assert.Equal(["Lütfen önce bir etkinlik seçin."], prompts.Messages);
    });

    /// <summary>The file appears only complete: written next to the target first, then moved over it.</summary>
    [Fact]
    public async Task Writer_ReplacesTheTargetAndLeavesNoTemporaryFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_directory, "liste.html");
        await File.WriteAllTextAsync(path, "eski içerik", ct);

        await ExportFileWriter.WriteAsync(path, [60, 33, 62], ct);

        Assert.Equal([60, 33, 62], await File.ReadAllBytesAsync(path, ct));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Writer_FailedMove_KeepsTheTargetAndRemovesTheTemporaryFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Path.Combine(_directory, "acik.csv");
        await File.WriteAllTextAsync(path, "eski", ct);

        // The target is held open without sharing, so it cannot be replaced.
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => ExportFileWriter.WriteAsync(path, [1, 2], ct));
            Assert.True(error is IOException or UnauthorizedAccessException, error.GetType().Name);
        }

        Assert.Equal("eski", await File.ReadAllTextAsync(path, ct));
        Assert.Equal([path], Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static KatilimciGoruntule Listing(ControlledExportService exports, ISaveFilePrompt save, RecordingPrompts? prompts = null)
    {
        var events = new ControlledEventService();
        var form = new KatilimciGoruntule(events, new ControlledParticipantService(), exports, Sessions.Admin)
        {
            Prompts = prompts ?? new RecordingPrompts(),
            SavePrompt = save,
        };
        var picker = Find<ComboBox>(form, "cmbEtkinlikSec");
        picker.Items.Add(events.Events[0]);
        picker.SelectedIndex = 0;
        return form;
    }
}
