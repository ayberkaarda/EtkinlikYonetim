using EtkinlikYonetim.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EtkinlikYonetim.Data.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Roles are stored as the original application wrote them ("Kullanici", "Admin"). Reading is case
    /// insensitive because the original CHECK constraint ran under a case-insensitive collation and
    /// accepted "admin" as well.
    /// </summary>
    internal static readonly ValueConverter<UserRole, string> RoleConverter = new(
        role => role == UserRole.Admin ? SchemaLimits.AdminRoleValue : SchemaLimits.UserRoleValue,
        value => string.Equals(value, SchemaLimits.AdminRoleValue, StringComparison.OrdinalIgnoreCase) ? UserRole.Admin : UserRole.User);

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Kullanicilar", table =>
        {
            table.HasCheckConstraint("CK_Kullanicilar_Rol", SchemaLimits.RoleCheck);
            table.HasCheckConstraint("CK_Kullanicilar_BasarisizGiris", SchemaLimits.FailedLoginCheck);
            table.HasCheckConstraint("CK_Kullanicilar_SifreBicimi", SchemaLimits.PasswordFormatCheck);
        });

        builder.HasKey(u => u.Id).HasName("PK_Kullanicilar");
        builder.Property(u => u.Id).HasColumnName("Id").ValueGeneratedOnAdd();
        builder.Property(u => u.UserName).HasColumnName("KullaniciAdi").HasMaxLength(SchemaLimits.UserNameLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("Sifre").HasMaxLength(SchemaLimits.PasswordLength).IsRequired();
        builder.Property(u => u.PasswordFormat).HasColumnName("SifreBicimi");
        builder.Property(u => u.Role).HasColumnName("Rol").HasMaxLength(SchemaLimits.RoleLength).HasConversion(RoleConverter).IsRequired();
        builder.Property(u => u.FailedLoginCount).HasColumnName("BasarisizGiris");
        builder.Property(u => u.LockoutEnd).HasColumnName("KilitBitis");
        builder.Property(u => u.Disabled).HasColumnName("DevreDisi");

        builder.HasIndex(u => u.UserName, "IX_Kullanicilar_KullaniciAdi").IsUnique();
    }
}
