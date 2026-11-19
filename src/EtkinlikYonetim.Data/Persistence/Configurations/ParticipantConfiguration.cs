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

        // A national id is unique within the participants of one account, not across accounts, so
        // adding a participant reveals nothing about other accounts' participants. Without a filter
        // the participants of the original application, which have no owner, stay unique as well.
        builder.HasIndex(p => new { p.OwnerUserId, p.NationalId }, "IX_Katilimcilar_KullaniciId_TcKimlikNo").IsUnique().HasFilter(null);
        builder.HasIndex(p => p.OwnerUserId, "IX_Katilimcilar_KullaniciId");
    }
}
