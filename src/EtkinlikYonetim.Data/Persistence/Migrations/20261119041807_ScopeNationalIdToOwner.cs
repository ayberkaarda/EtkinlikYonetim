using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeNationalIdToOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Katilimcilar_TcKimlikNo",
                table: "Katilimcilar");

            migrationBuilder.CreateIndex(
                name: "IX_Katilimcilar_KullaniciId_TcKimlikNo",
                table: "Katilimcilar",
                columns: new[] { "KullaniciId", "TcKimlikNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Katilimcilar_KullaniciId_TcKimlikNo",
                table: "Katilimcilar");

            migrationBuilder.CreateIndex(
                name: "IX_Katilimcilar_TcKimlikNo",
                table: "Katilimcilar",
                column: "TcKimlikNo",
                unique: true);
        }
    }
}
