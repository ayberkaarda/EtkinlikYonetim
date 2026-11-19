using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountDisabledFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing accounts stay enabled. The default only fills the existing rows: like the other
            // columns of the model the new one has no default constraint afterwards.
            migrationBuilder.Sql(
                "ALTER TABLE dbo.Kullanicilar ADD DevreDisi bit NOT NULL CONSTRAINT DF_Upgrade_Kullanicilar_DevreDisi DEFAULT 0 WITH VALUES; "
                + "ALTER TABLE dbo.Kullanicilar DROP CONSTRAINT DF_Upgrade_Kullanicilar_DevreDisi;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DevreDisi",
                table: "Kullanicilar");
        }
    }
}
