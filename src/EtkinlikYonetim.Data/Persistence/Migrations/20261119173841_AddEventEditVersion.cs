using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventEditVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing events start at version 0, the value the forms open during the upgrade carry. The default
            // only fills the existing rows: like the other columns of the model the new one has no default
            // constraint afterwards.
            migrationBuilder.Sql(
                "ALTER TABLE dbo.Etkinlikler ADD DuzenlemeSurumu int NOT NULL CONSTRAINT DF_Upgrade_Etkinlikler_DuzenlemeSurumu DEFAULT 0 WITH VALUES; "
                + "ALTER TABLE dbo.Etkinlikler DROP CONSTRAINT DF_Upgrade_Etkinlikler_DuzenlemeSurumu;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DuzenlemeSurumu",
                table: "Etkinlikler");
        }
    }
}
