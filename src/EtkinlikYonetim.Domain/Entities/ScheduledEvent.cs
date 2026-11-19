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
}
