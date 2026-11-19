using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EtkinlikYonetim.Data.Persistence.Configurations;

internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        builder.ToTable("Katilimcilar");

        builder.HasKey(p => p.Id).HasName("PK_Katilimcilar");
        builder.Property(p => p.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        builder.Property(p => p.NationalId).HasColumnName("TcKimlikNo").HasMaxLength(SchemaLimits.NationalIdLength).IsRequired();
        builder.Property(p => p.FullName).HasColumnName("AdSoyad").HasMaxLength(SchemaLimits.PersonNameLength);
        builder.Property(p => p.Email).HasColumnName("Email").HasMaxLength(SchemaLimits.EmailLength);
        builder.Property(p => p.OwnerUserId).HasColumnName("KullaniciId");

        // An account that still owns participants cannot be deleted.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.OwnerUserId)
            .HasConstraintName("FK_Katilimcilar_Kullanicilar")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.NationalId, "IX_Katilimcilar_TcKimlikNo").IsUnique();
        builder.HasIndex(p => p.OwnerUserId, "IX_Katilimcilar_KullaniciId");
    }
}
