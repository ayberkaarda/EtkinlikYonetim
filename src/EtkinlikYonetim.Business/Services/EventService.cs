using System.Globalization;
using EtkinlikYonetim.Business.Auditing;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;
using FluentValidation;

namespace EtkinlikYonetim.Business.Services;

/// <summary>Listing events for every signed-in account and creating them for admins.</summary>
public interface IEventService
{
    /// <summary>All events, past ones included, earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetEventsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Events that still accept registrations (today or later), earliest first.</summary>
    Task<IReadOnlyList<EventSummary>> GetOpenEventsAsync(UserSession session, CancellationToken cancellationToken = default);

    /// <summary>Creates an event; admins only. Returns the new event id.</summary>
    Task<int> CreateAsync(UserSession session, string? name, DateTime eventDate, int capacity, CancellationToken cancellationToken = default);
}

internal sealed class EventService(
    IEventRepository events,
    TimeProvider timeProvider,
    IValidator<EventInput> validator,
    ISessionGuard sessions,
    IAuditLog audit) : IEventService
{
    internal const string NameRequired = "Etkinlik adı boş bırakılamaz.";
    internal const string PastDate = "Geçmiş tarihli etkinlik eklenemez.";

    internal static readonly string NameTooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Etkinlik adı en fazla {InputRules.EventNameMaxLength} karakter olabilir.");

    internal static readonly string CapacityInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kapasite {InputRules.MinCapacity} ile {InputRules.MaxCapacity} arasında olmalıdır.");

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
}
