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

    /// <summary>
    /// Place in the waiting list of the event, given when the event was full: 1 for the first one who
    /// had to wait, then 2, 3 and so on. Numbers are never reused, so the order of the list is the order
    /// of these numbers; the number is kept when the registration moves up to a seat.
    /// <see langword="null"/> for a registration that got a seat at once.
    /// </summary>
    public int? QueueNumber { get; set; }

    /// <summary>The ticket issued with the registration; a waiting registration gets one when it moves up to a seat.</summary>
    public Ticket? Ticket { get; set; }
}

/// <summary>Cancelled registrations are kept for the record and no longer take a seat.</summary>
public enum RegistrationStatus
{
    /// <summary>Holds a seat and a ticket.</summary>
    Active = 0,

    /// <summary>Cancelled by the participant's account or an admin.</summary>
    Cancelled = 1,

    /// <summary>
    /// On the waiting list of a full event, without a seat or a ticket. When a seat becomes free the
    /// first waiting registration moves up to <see cref="Active"/> in the same transaction.
    /// </summary>
    Waiting = 2,

    /// <summary>Closed because an admin cancelled the event; shown to the participant's account as cancelled.</summary>
    EventCancelled = 3,
}

/// <summary>The ticket of exactly one registration (table: Biletler).</summary>
public sealed class Ticket
{
    public int Id { get; set; }

    public int RegistrationId { get; set; }

    /// <summary>
    /// Unique, random code shown on the ticket and in its QR code: "ETK-" and 26 characters of a 32 letter
    /// alphabet, 130 random bits. Kept so that the participant's account can show the QR code again;
    /// never written to a log, an export or the admin lists.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>SHA-256 of <see cref="Code"/> (<see cref="TicketCodeHash"/>); the entrance looks a ticket up by it.</summary>
    public byte[] CodeHash { get; set; } = [];

    /// <summary>UTC time the ticket was issued.</summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>UTC time of the check-in at the entrance, or <see langword="null"/> while the ticket is unused.</summary>
    public DateTime? CheckedInAt { get; set; }
}

/// <summary>
/// The lookup key of a ticket code: SHA-256 of its ASCII text. The entrance finds a ticket by this hash,
/// so the database compares hashes, whose timing tells nothing about the code, and the code itself is
/// then compared in constant time.
/// </summary>
public static class TicketCodeHash
{
    public static byte[] Of(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(code));
    }
}
