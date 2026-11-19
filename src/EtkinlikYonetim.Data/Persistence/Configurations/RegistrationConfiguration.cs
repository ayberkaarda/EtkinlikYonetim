using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EtkinlikYonetim.Data.Persistence.Configurations;

internal sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> builder)
    {
        builder.ToTable("Kayitlar", table => table.HasCheckConstraint("CK_Kayitlar_Durum", SchemaLimits.StatusCheck));

        builder.HasKey(r => r.Id).HasName("PK_Kayitlar");
        builder.Property(r => r.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        builder.Property(r => r.ParticipantId).HasColumnName("KatilimciId");
        builder.Property(r => r.EventId).HasColumnName("EtkinlikId");
        builder.Property(r => r.CreatedAt).HasColumnName("KayitZamani");
        builder.Property(r => r.Status).HasColumnName("Durum").HasConversion<int>();
        builder.Property(r => r.CancelledAt).HasColumnName("IptalZamani");

        // Registrations are history: deleting a participant or event that has registrations is refused.
        builder.HasOne<Participant>()
            .WithMany()
            .HasForeignKey(r => r.ParticipantId)
            .HasConstraintName("FK_Kayitlar_Katilimcilar")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScheduledEvent>()
            .WithMany()
            .HasForeignKey(r => r.EventId)
            .HasConstraintName("FK_Kayitlar_Etkinlikler")
            .OnDelete(DeleteBehavior.Restrict);

        // One active registration per participant and event. This index is what prevents double
        // registrations under concurrency; the service's own check only produces a friendlier message.
        builder.HasIndex(r => new { r.ParticipantId, r.EventId }, "IX_Kayitlar_KatilimciId_EtkinlikId")
            .IsUnique()
            .HasFilter(SchemaLimits.ActiveRegistrationFilter);
        builder.HasIndex(r => r.EventId, "IX_Kayitlar_EtkinlikId");
    }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Biletler");

        builder.HasKey(t => t.Id).HasName("PK_Biletler");
        builder.Property(t => t.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        builder.Property(t => t.RegistrationId).HasColumnName("KayitId");
        builder.Property(t => t.Code).HasColumnName("BiletKodu").HasMaxLength(SchemaLimits.TicketCodeLength).IsRequired();
        builder.Property(t => t.IssuedAt).HasColumnName("OlusturmaZamani");

        // A ticket always belongs to exactly one registration, and a registration has at most one ticket.
        builder.HasOne<Registration>()
            .WithOne(r => r.Ticket)
            .HasForeignKey<Ticket>(t => t.RegistrationId)
            .HasConstraintName("FK_Biletler_Kayitlar")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.RegistrationId, "IX_Biletler_KayitId").IsUnique();
        builder.HasIndex(t => t.Code, "IX_Biletler_BiletKodu").IsUnique();
    }
}
