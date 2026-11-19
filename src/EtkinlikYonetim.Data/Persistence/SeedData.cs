using EtkinlikYonetim.Domain.Entities;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// Sample data applied by the InitialCreate migration through HasData, only to a new database. The
/// people and events are fictional, the national ids are synthetic numbers that merely pass the
/// checksum and the e-mail addresses use the reserved example.com domain. Values are fixed so the
/// migration is deterministic, which is why the password hashes are precomputed
/// (admin: "Demo.Admin1", ornek: "Demo.Kullanici1").
/// </summary>
internal static class SeedData
{
    public const int AdminId = 1;
    public const int SampleUserId = 2;

    public static readonly User[] Users =
    [
        new()
        {
            Id = AdminId,
            UserName = "admin",
            Role = UserRole.Admin,
            PasswordHash = "PBKDF2-SHA256$600000$YpX54aoSvQPCiUKqlMMoGA==$MH93hF8SQ80mWaKR6Y+dpuj10DcfJLUBryuy8ZdEnhY=",
        },
        new()
        {
            Id = SampleUserId,
            UserName = "ornek",
            Role = UserRole.User,
            PasswordHash = "PBKDF2-SHA256$600000$Xm3uBwS1RHIIhXfHqUMdzw==$gfpLWUifJbAP+nhdV43Lr5lHCog4znWXgpJF+kyjiRw=",
        },
    ];

    public static readonly ScheduledEvent[] Events =
    [
        new() { Id = 1, Name = "Bahar Konseri", Date = new DateOnly(2027, 4, 17), Capacity = 150 },
        new() { Id = 2, Name = "Yazılım Atölyesi", Date = new DateOnly(2027, 5, 8), Capacity = 30 },
        new() { Id = 3, Name = "Kısa Film Gösterimi", Date = new DateOnly(2027, 6, 12), Capacity = 2 },
    ];

    public static readonly Participant[] Participants =
    [
        new() { Id = 1, NationalId = "10000000146", FullName = "Deniz Örnek", Email = "deniz@example.com", OwnerUserId = SampleUserId },
        new() { Id = 2, NationalId = "10000000214", FullName = "Ece Taslak", Email = "ece@example.com", OwnerUserId = SampleUserId },
        new() { Id = 3, NationalId = "10000000382", FullName = "Mert Deneme", Email = "mert@example.com", OwnerUserId = AdminId },
    ];
}
