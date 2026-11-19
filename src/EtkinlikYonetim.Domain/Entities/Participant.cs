namespace EtkinlikYonetim.Domain.Entities;

/// <summary>A person who can be registered for events (table: Katilimcilar).</summary>
public sealed class Participant
{
    public int Id { get; set; }

    /// <summary>Turkish national identity number (T.C. Kimlik No). Personal data: never log it.</summary>
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Full name; may be missing on rows of the original application.</summary>
    public string? FullName { get; set; }

    /// <summary>E-mail address; may be missing on rows of the original application.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// The account that added the participant. Only that account (and admins) can register the
    /// participant or see its registrations. <see langword="null"/> for rows of the original
    /// application, which only admins can manage.
    /// </summary>
    public int? OwnerUserId { get; set; }
}
