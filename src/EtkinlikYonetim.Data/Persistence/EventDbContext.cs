using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EtkinlikYonetim.Data.Persistence;

/// <summary>
/// EF Core context of the event database. Table and column names follow the original schema
/// (Kullanicilar, Etkinlikler, Katilimcilar) and its Turkish naming for the new tables (Kayitlar, Biletler).
/// </summary>
public sealed class EventDbContext(DbContextOptions<EventDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<ScheduledEvent> Events => Set<ScheduledEvent>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<Registration> Registrations => Set<Registration>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<SetupKeyRecord> SetupKeys => Set<SetupKeyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EventDbContext).Assembly);
    }
}
