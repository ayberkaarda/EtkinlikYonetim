namespace EtkinlikYonetim.Domain.Entities;

/// <summary>A participant's registration for an event (table: Kayitlar).</summary>
public sealed class Registration
{
    public int Id { get; set; }

    public int ParticipantId { get; set; }

    public int EventId { get; set; }

    /// <summary>UTC time of the registration.</summary>
    public DateTime CreatedAt { get; set; }

    public RegistrationStatus Status { get; set; } = RegistrationStatus.Active;

    /// <summary>UTC time of the cancellation, or <see langword="null"/> for an active registration.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>The ticket issued with the registration.</summary>
    public Ticket? Ticket { get; set; }
}

/// <summary>Cancelled registrations are kept for the record and no longer take a seat.</summary>
public enum RegistrationStatus
{
    Active = 0,
    Cancelled = 1,
}

/// <summary>The ticket of exactly one registration (table: Biletler).</summary>
public sealed class Ticket
{
    public int Id { get; set; }

    public int RegistrationId { get; set; }

    /// <summary>Unique, random code printed on the ticket, for example "ETK-7KQ2M9XA4C".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>UTC time the ticket was issued.</summary>
    public DateTime IssuedAt { get; set; }
}
