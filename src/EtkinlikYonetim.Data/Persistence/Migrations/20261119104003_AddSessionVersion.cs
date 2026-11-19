using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing accounts start at version 0, the value their open sessions carry. The default only fills
            // the existing rows: like the other columns of the model the new one has no default constraint afterwards.
            migrationBuilder.Sql(
                "ALTER TABLE dbo.Kullanicilar ADD OturumSurumu int NOT NULL CONSTRAINT DF_Upgrade_Kullanicilar_OturumSurumu DEFAULT 0 WITH VALUES; "
                + "ALTER TABLE dbo.Kullanicilar DROP CONSTRAINT DF_Upgrade_Kullanicilar_OturumSurumu;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OturumSurumu",
                table: "Kullanicilar");
        }
    }
}
