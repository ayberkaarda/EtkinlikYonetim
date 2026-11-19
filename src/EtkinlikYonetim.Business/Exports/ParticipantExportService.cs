using System.Globalization;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

namespace EtkinlikYonetim.Business.Exports;

/// <summary>Exports the participant and check-in list of an event for admins.</summary>
public interface IParticipantExportService
{
    /// <summary>The list as CSV; admins only.</summary>
    Task<ExportFile> ExportCsvAsync(UserSession session, int eventId, CancellationToken cancellationToken = default);

    /// <summary>The list as a printable HTML page; admins only.</summary>
    Task<ExportFile> ExportHtmlAsync(UserSession session, int eventId, CancellationToken cancellationToken = default);
}

internal sealed class ParticipantExportService(
    IEventRepository events,
    IParticipantRepository participants,
    TimeProvider timeProvider,
    ISessionGuard sessions,
    IAuditLog audit) : IParticipantExportService
{
    public async Task<ExportFile> ExportCsvAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        var list = await LoadAsync(session, eventId, cancellationToken).ConfigureAwait(false);
        return new ExportFile(FileName(eventId, list, "csv"), ParticipantExport.ToCsv(list));
    }

    public async Task<ExportFile> ExportHtmlAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        var list = await LoadAsync(session, eventId, cancellationToken).ConfigureAwait(false);
        return new ExportFile(FileName(eventId, list, "html"), ParticipantExport.ToHtml(list));
    }

    private async Task<ParticipantList> LoadAsync(UserSession session, int eventId, CancellationToken cancellationToken)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var target = await events.GetByIdAsync(eventId, cancellationToken).ConfigureAwait(false)
            ?? throw new BusinessRuleException(EventService.EventNotFound);
        var rows = await participants.GetByEventAsync(eventId, cancellationToken).ConfigureAwait(false);
        var zone = timeProvider.LocalTimeZone;
        var list = new ParticipantList(
            target.Name,
            target.Date,
            target.Cancelled,
            rows.Select(r =>
                {
                    var view = ParticipantService.View(r);
                    var checkedIn = r.CheckedInAt is DateTime at
                        ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(at, DateTimeKind.Utc), zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
                        : string.Empty;
                    return new ExportRow(view.FullName, view.MaskedNationalId, view.Email, view.State, checkedIn, r.Status);
                })
                .ToList(),
            timeProvider.GetLocalNow());
        audit.Record(AuditEvent.ParticipantListExported, session.Role);
        return list;
    }

    /// <summary>A name without the event's own text, so nothing typed into an event name reaches the file system.</summary>
    private static string FileName(int eventId, ParticipantList list, string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"katilimcilar-{eventId}-{list.EventDate:yyyyMMdd}.{extension}");
}
