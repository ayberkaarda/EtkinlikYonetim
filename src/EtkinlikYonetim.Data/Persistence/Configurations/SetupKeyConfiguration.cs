using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EtkinlikYonetim.Data.Persistence.Configurations;

internal sealed class SetupKeyConfiguration : IEntityTypeConfiguration<SetupKeyRecord>
{
    public void Configure(EntityTypeBuilder<SetupKeyRecord> builder)
    {
        builder.ToTable("KurulumAnahtarlari", table =>
            table.HasCheckConstraint("CK_KurulumAnahtarlari_BasarisizDeneme", SchemaLimits.SetupKeyFailedAttemptsCheck));

        builder.HasKey(k => k.Id).HasName("PK_KurulumAnahtarlari");
        builder.Property(k => k.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        builder.Property(k => k.KeyHash).HasColumnName("AnahtarOzeti").HasMaxLength(SchemaLimits.SetupKeyHashLength).IsFixedLength().IsRequired();
        builder.Property(k => k.CreatedAt).HasColumnName("OlusturmaZamani");
        builder.Property(k => k.ExpiresAt).HasColumnName("GecerlilikBitis");
        builder.Property(k => k.ConsumedAt).HasColumnName("KullanimZamani");
        builder.Property(k => k.FailedAttempts).HasColumnName("BasarisizDeneme");
    }
}