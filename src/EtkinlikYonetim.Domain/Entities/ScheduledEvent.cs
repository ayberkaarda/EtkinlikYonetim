namespace EtkinlikYonetim.Domain.Entities;

/// <summary>An event people can register for (table: Etkinlikler).</summary>
public sealed class ScheduledEvent
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Day of the event; registrations close when the day has passed.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Maximum number of active registrations.</summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Number of active registrations. It is changed in the same transaction as the registration rows,
    /// by a conditional UPDATE that also enforces <see cref="Capacity"/>, and a check constraint keeps
    /// it between zero and the capacity.
    /// </summary>
    public int RegisteredCount { get; set; }

    /// <summary>Optional text an admin writes about the event; shown in the event management.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Set when an admin cancels the event. The row stays (soft delete): its registrations are closed
    /// with <see cref="RegistrationStatus.EventCancelled"/>, it takes no new registration and cannot be
    /// changed any more.
    /// </summary>
    public bool Cancelled { get; set; }

    /// <summary>UTC time of the cancellation, or <see langword="null"/>.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Raised by one on every change and cancellation by an admin. A change is stored only while the event
    /// still has the version its form was loaded with, so an older form never writes over another admin's
    /// change, also not over one that only changed the case of the description (the database compares
    /// text without regard to case).
    /// </summary>
    public int EditVersion { get; set; }
}
