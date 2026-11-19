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
    /// Changes the date, capacity and description of the event <paramref name="original"/> (as the caller
    /// loaded it); admins only. Past and cancelled events cannot be changed, and the capacity cannot go below
    /// the seats held. Seats added by a larger capacity go to the waiting list in order. When somebody else
    /// changed the event since it was loaded, nothing is written and <see cref="StaleDataException"/> is thrown.
    /// </summary>
    Task UpdateAsync(UserSession session, EventSummary original, DateTime eventDate, int capacity, string? description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an event; admins only. The event stays in the lists as cancelled, its registrations and
    /// waiting places are closed and their tickets stop being valid. Past events cannot be cancelled.
    /// </summary>
    Task CancelAsync(UserSession session, int eventId, CancellationToken cancellationToken = default);
}

internal sealed class EventService(
    IEventRepository events,
    IDatabaseClock clock,
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
    internal const string ChangedElsewhere = "Etkinlik siz düzenlerken başka biri tarafından değiştirildi; değişikliğiniz kaydedilmedi. Liste yenilendi, lütfen değişikliği güncel bilgiler üzerinde yeniden yapın.";

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
        var now = await clock.NowAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        return await events.GetFromAsync(now.Today, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(
        UserSession session,
        string? name,
        DateTime eventDate,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        var now = await clock.NowAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var input = EventInput.Create(name, eventDate, capacity, now.Today);
        validator.ValidateOrThrow(input);

        var id = await audit.WriteAsAsync(session, actor => events.AddAsync(
            actor,
            new ScheduledEvent { Name = input.Name, Date = input.Day, Capacity = input.Capacity, RegisteredCount = 0 },
            cancellationToken)).ConfigureAwait(false);
        audit.Record(AuditEvent.EventCreated, session.Role);
        return id;
    }

    public async Task UpdateAsync(
        UserSession session,
        EventSummary original,
        DateTime eventDate,
        int capacity,
        string? description,
        CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(original);

        var now = await clock.NowAsync(timeProvider, cancellationToken).ConfigureAwait(false);
        var input = EventUpdateInput.Create(eventDate, capacity, description, now.Today);
        updateValidator.ValidateOrThrow(input);

        // The repository decides whether the event is still open, still as the caller loaded it and whether
        // the capacity still holds its seats, in the transaction that writes.
        var status = await audit.WriteAsAsync(session, actor => events.UpdateAsync(
            actor,
            original.Id,
            new EventValues(original.Date, original.Capacity, original.Description),
            new EventValues(input.Day, input.Capacity, input.Description),
            TicketCode.New,
            cancellationToken)).ConfigureAwait(false);
        Complete(status);
        audit.Record(AuditEvent.EventUpdated, session.Role);
    }

    public async Task CancelAsync(UserSession session, int eventId, CancellationToken cancellationToken = default)
    {
        await sessions.RequireAdminAsync(session, audit, cancellationToken).ConfigureAwait(false);

        // The repository reads the database's day itself, after the event's lock.
        var status = await audit.WriteAsAsync(session, actor => events.CancelAsync(actor, eventId, cancellationToken)).ConfigureAwait(false);
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
            case EventChangeStatus.DateInPast:
                throw new BusinessRuleException(NewDateInPast);
            case EventChangeStatus.CapacityBelowRegistrations:
                throw new BusinessRuleException(CapacityBelowRegistrations);
            case EventChangeStatus.ChangedElsewhere:
                throw new StaleDataException(ChangedElsewhere);
            default:
                throw new BusinessRuleException(EventNotFound);
        }
    }
}
