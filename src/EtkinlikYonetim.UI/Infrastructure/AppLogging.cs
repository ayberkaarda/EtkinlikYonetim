using System.Globalization;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Domain.Entities;
using Serilog;
using Serilog.Core;

namespace EtkinlikYonetim.UI.Infrastructure;

/// <summary>
/// File logging for diagnostics. Logs stay on the user's machine, outside the repository. User names,
/// participant names, e-mail addresses, national ids, passwords, setup keys, ticket codes and
/// exception messages are never written: error entries come from <see cref="ErrorReporter.FormatForLog"/>
/// and audit entries are fixed codes from <see cref="AuditCodes"/>.
/// </summary>
internal static class AppLogging
{
    /// <summary>Files dated more than this many days before today are deleted.</summary>
    internal const int RetainedDays = 30;

    /// <summary>At most this many files are kept, the current one included (one per day normally).</summary>
    internal const int RetainedFileCount = 30;

    /// <summary>%LOCALAPPDATA%\EtkinlikYonetim\logs</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EtkinlikYonetim",
        "logs");

    /// <summary>
    /// Creates a logger that writes one file per day. When a file is opened, older files of the folder
    /// are deleted: those dated before the last <see cref="RetainedDays"/> days, and any beyond the
    /// newest <see cref="RetainedFileCount"/>.
    /// </summary>
    public static Logger CreateLogger(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(directory, "etkinlikyonetim-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFileCount,
                retainedFileTimeLimit: TimeSpan.FromDays(RetainedDays),
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();
    }
}

/// <summary>
/// Writes audit events as a fixed code and a role, for example <c>Audit auth.sign_in.failed none</c>.
/// Nothing that identifies an account, a participant, a registration or a ticket is available here, so
/// nothing can leak. The file is readable by the local Windows user, so the codes come from
/// <see cref="AuditCodes.ForPublicLog"/>: outcomes that would tell whether an account, a registration or
/// a ticket exists share one code there.
/// </summary>
internal sealed class SerilogAuditLog(ILogger logger) : IAuditLog
{
    public void Record(AuditEvent auditEvent, UserRole? role)
    {
        if (AuditCodes.ForPublicLog(auditEvent) is { } code)
        {
            logger.Information("Audit {Code} {Role}", code, AuditCodes.Of(role));
        }
    }
}
