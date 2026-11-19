using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using EtkinlikYonetim.Business;
using EtkinlikYonetim.Data.Persistence;
using Serilog;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// Logs exceptions and turns them into short, user friendly Turkish messages, shown inline on a screen
/// with a message strip and in a message window otherwise.
/// </summary>
internal static class ErrorReporter
{
    private const int MaxLoggedExceptions = 8;

    internal const string UnexpectedErrorMessage = "Beklenmeyen bir hata oluştu. İşleminiz tamamlanamamış olabilir.";

    /// <summary>Another change of the same event or of the accounts held the database lock too long; nothing was written.</summary>
    internal const string BusyMessage = "Veritabanı şu anda aynı kayıtlar üzerindeki başka bir işlemle meşgul; hiçbir değişiklik yapılmadı. Lütfen birkaç saniye sonra tekrar deneyin.";

    public static void Show(Exception? exception, IWin32Window? owner = null)
    {
        if (exception is null)
        {
            return;
        }

        Record(Log.Logger, exception);

        // An ended session closes the windows of the panel, so its message needs a window of its own.
        if (exception is SessionExpiredException)
        {
            Dialogs.Show(owner, exception.Message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Rule and permission messages are fixed texts written for the user; they are shown as warnings,
        // inline on a screen with a message strip.
        if (exception is BusinessRuleException or AccessDeniedException)
        {
            if (owner is IStatusSurface surface)
            {
                surface.ShowStatus(Theming.StatusKind.Warning, exception.Message);
                return;
            }

            Dialogs.Show(owner, exception.Message, "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (owner is IStatusSurface screen)
        {
            screen.ShowStatus(Theming.StatusKind.Error, Describe(exception));
            return;
        }

        Dialogs.Show(owner, Describe(exception), AppText.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>
    /// Writes <paramref name="exception"/> to <paramref name="logger"/> without its message. Rule,
    /// permission and session refusals are expected answers, not failures, and are not written: a line for
    /// a refusal and none for a success would tell the reader of the file which attempt found an existing
    /// account, registration or ticket. The audit log records the security relevant ones with neutral
    /// codes. Every other exception is logged through <see cref="FormatForLog"/>.
    /// </summary>
    internal static void Record(ILogger logger, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is BusinessRuleException or AccessDeniedException or SessionExpiredException)
        {
            return;
        }

        logger.Error("Operation failed: {Details}", FormatForLog(exception));
    }

    /// <summary>
    /// Builds a log entry that carries no exception messages and no file paths: exception messages
    /// can contain server names, connection details or personal data such as national ids. Only the
    /// exception types, their error codes and the methods of each stack frame are written, for the
    /// whole inner exception chain.
    /// </summary>
    public static string FormatForLog(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var builder = new StringBuilder();
        var depth = 0;
        for (Exception? current = exception; current is not null && depth < MaxLoggedExceptions; depth++)
        {
            if (depth > 0)
            {
                builder.AppendLine("--- inner exception ---");
            }

            builder.Append(current.GetType().FullName)
                .Append(" (HResult 0x")
                .Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture))
                .AppendLine(")");

            foreach (var frame in new StackTrace(current, fNeedFileInfo: false).GetFrames())
            {
                var method = frame.GetMethod();
                if (method is not null)
                {
                    builder.Append("   at ")
                        .Append(method.DeclaringType?.FullName ?? "<unknown>")
                        .Append('.')
                        .AppendLine(method.Name);
                }
            }

            current = current is AggregateException { InnerExceptions.Count: > 0 } aggregate
                ? aggregate.InnerExceptions[0]
                : current.InnerException;
        }

        return builder.ToString();
    }

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Find<DatabaseInitializationException>(exception) is not null)
        {
            return "Veritabanı hazırlanamadı. SQL Server'ın çalıştığından, bağlantı ayarlarının doğru olduğundan "
                + "ve veritabanı oluşturma yetkiniz bulunduğundan emin olun.";
        }

        // A deadlock victim was rolled back like a write that found its lock busy: nothing was written.
        if (Find<DatabaseBusyException>(exception) is not null || DatabaseErrors.IsDeadlock(exception))
        {
            return BusyMessage;
        }

        if (Find<DbException>(exception) is not null)
        {
            return "Veritabanı işlemi tamamlanamadı. SQL Server'ın çalıştığından ve bağlantı ayarlarının doğru olduğundan emin olun.";
        }

        if (Find<TimeoutException>(exception) is not null)
        {
            return "İşlem zaman aşımına uğradı. Lütfen tekrar deneyin.";
        }

        return UnexpectedErrorMessage;
    }

    private static T? Find<T>(Exception exception)
        where T : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is T match)
            {
                return match;
            }

            if (current is AggregateException { InnerExceptions.Count: > 0 } aggregate)
            {
                current = aggregate.InnerExceptions[0];
                if (current is T inner)
                {
                    return inner;
                }
            }
        }

        return null;
    }
}
