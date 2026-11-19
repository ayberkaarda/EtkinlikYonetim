using System.Globalization;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using EtkinlikYonetim.Domain.Repositories;

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

internal sealed class EventService(IEventRepository events, TimeProvider timeProvider) : IEventService
{
    internal const string NameRequired = "Etkinlik adı boş bırakılamaz.";
    internal const string PastDate = "Geçmiş tarihli etkinlik eklenemez.";

    internal static readonly string NameTooLong = string.Create(
        CultureInfo.InvariantCulture,
        $"Etkinlik adı en fazla {InputRules.EventNameMaxLength} karakter olabilir.");

    internal static readonly string CapacityInvalid = string.Create(
        CultureInfo.InvariantCulture,
        $"Kapasite {InputRules.MinCapacity} ile {InputRules.MaxCapacity} arasında olmalıdır.");

    public Task<IReadOnlyList<EventSummary>> GetEventsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        return events.GetAllAsync(cancellationToken);
    }

    public Task<IReadOnlyList<EventSummary>> GetOpenEventsAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        Access.RequireSignedIn(session);
        return events.GetFromAsync(timeProvider.Today(), cancellationToken);
    }

    public Task<int> CreateAsync(
        UserSession session,
        string? name,
        DateTime eventDate,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        Access.RequireAdmin(session);

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new BusinessRuleException(NameRequired);
        }

        if (trimmed.Length > InputRules.EventNameMaxLength)
        {
            throw new BusinessRuleException(NameTooLong);
        }

        var day = DateOnly.FromDateTime(eventDate);
        if (day < timeProvider.Today())
        {
            throw new BusinessRuleException(PastDate);
        }

        if (capacity is < InputRules.MinCapacity or > InputRules.MaxCapacity)
        {
            throw new BusinessRuleException(CapacityInvalid);
        }

        return events.AddAsync(
            new ScheduledEvent { Name = trimmed, Date = day, Capacity = capacity, RegisteredCount = 0 },
            cancellationToken);
    }
}
