using System.Globalization;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using FluentValidation;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Listing events for every signed-in account; creating, changing and cancelling them for admins.</summary>
public interface IEventService
{
    /// <summary>All events, past and cancelled ones included, earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetEventsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Events that still accept registrations (today or later, not cancelled), earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetOpenEventsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Creates an event without a description; admins only. Returns the new event id.</summary>
    Task<int> CreateAsync(UserSession session, string? name, DateTime eventDate, int capacity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the date, capacity and description of an event; admins only. Past and cancelled events
    /// cannot be changed, and the capacity cannot go below the seats held. Seats added by a larger
    /// capacity go to the waiting list in order.
    /// </summary>
    Task UpdateAsync(UserSession session, int eventId, DateTime eventDate, int capacity, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an event; admins only. The event stays in the lists as cancelled, its registrations and
    /// waiting places are closed and their tickets stop being valid. Past events cannot be cancelled.
    /// </summary>
    Task CancelAsync(UserSession session, int eventId, CancellationToken cancellationToken = default);
}

internal sealed class EventService(
    IEventRepository events,
    TimeProvider timeProvider,
    IValidator<EventInput> validator,
    IValidator<EventUpdateInput> updateValidator,
    ISessionGuard sessions,
    IAuditLog audit) : IEventService
{
    internal const string NameRequired = "Etkinlik adı boş bırakılamaz.";
    internal const string PastDate = "Geçmiş tarihli etkinlik eklenemez.";
    internal const string NewDateInPast = "Etkinliğin yeni tarihi geçmiş bir gün olamaz.";
    internal const string EventNotFound = "Etkinlik bulunamadı.";
    internal const string EventClosed = "Geçmiş veya iptal edilmiş bir etkinlik değiştirilemez ya da iptal edilemez.";
    internal const string CapacityBelowRegistrations = "Kapasite, etkinliğin mevcut kayıt sayısının altına indirilemez.";

    internal static readonly string NameTooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Etkinlik adı en fazla {InputRules.EventNameMaxLength} karakter olabilir.");

    internal static readonly string CapacityInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kapasite {InputRules.MinCapacity} ile {InputRules.MaxCapacity} arasında olmalıdır.");

    internal static readonly string DescriptionTooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Açıklama en fazla {InputRules.EventDescriptionMaxLength} karakter olabilir.");

    public async Task<IReadOnlyList<EventSummary>> GetEventsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await sessions.RequireCurrentAsync(session, cancellationToken).ConfigureAwait(false);
        return await events.GetAllAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EventSummary>> GetOpenEventsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        await sessions.RequireCurrentAsync(session, cancellationToken).ConfigureAwait(false);
        return await events.GetFromAsync(timeProvider.Today(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(
        UserSession session,
        string? name,
        DateTime eventDate,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var input = EventInput.Create(name, eventDate, capacity, timeProvider.Today());
        validator.ValidateOrThrow(input);

        var id = await events.AddAsync(
            new ScheduledEvent { Name = input.Name, Date = input.Day, Capacity = input.Capacity, RegisteredCount = 0 },
            cancellationToken).ConfigureAwait(false);
        audit.Record(AuditEvent.EventCreated, session.Role);
        return id;
    }

    public async Task UpdateAsync(
        UserSession session,
        int eventId,
        DateTime eventDate,
        int capacity,
        string? description,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var today = timeProvider.Today();
        var input = EventUpdateInput.Create(eventDate, capacity, description, today);
        updateValidator.ValidateOrThrow(input);

        // The repository decides whether the event is still open and the capacity still holds its seats,
        // in the transaction that writes.
        var status = await events.UpdateAsync(
            eventId,
            input.Day,
            input.Capacity,
            input.Description,
            today,
            timeProvider.GetUtcNow().UtcDateTime,
            TicketCode.New,
            cancellationToken).ConfigureAwait(false);
        Complete(status);
        audit.Record(AuditEvent.EventUpdated, session.Role);
    }

    public async Task CancelAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var status = await events.CancelAsync(eventId, timeProvider.Today(), timeProvider.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        Complete(status);
        audit.Record(AuditEvent.EventCancelled, session.Role);
    }

    private static void Complete(EventChangeStatus status)
    {
        switch (status)
        {
            case EventChangeStatus.Changed:
                return;
            case EventChangeStatus.Closed:
                throw new BusinessRuleException(EventClosed);
            case EventChangeStatus.CapacityBelowRegistrations:
                throw new BusinessRuleException(CapacityBelowRegistrations);
            default:
                throw new BusinessRuleException(EventNotFound);
        }
    }
}
