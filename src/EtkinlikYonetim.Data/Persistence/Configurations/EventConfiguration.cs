using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EtkinlikYonetim.Data.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<ScheduledEvent>
{
    public void Configure(EntityTypeBuilder<ScheduledEvent> builder)
    {
        builder.ToTable("Etkinlikler", table =>
        {
            table.HasCheckConstraint("CK_Etkinlikler_Kapasite", SchemaLimits.CapacityCheck);
            table.HasCheckConstraint("CK_Etkinlikler_KayitSayisi", SchemaLimits.RegisteredCountCheck);
        });

        builder.HasKey(e => e.Id).HasName("PK_Etkinlikler");
        builder.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        builder.Property(e => e.Name).HasColumnName("EtkinlikAdi").HasMaxLength(SchemaLimits.EventNameLength).IsRequired();
        builder.Property(e => e.Date).HasColumnName("Tarih").HasColumnType("date");
        builder.Property(e => e.Capacity).HasColumnName("Kapasite");
        builder.Property(e => e.RegisteredCount).HasColumnName("KayitSayisi");

        builder.HasIndex(e => e.Date, "IX_Etkinlikler_Tarih");
    }
}
