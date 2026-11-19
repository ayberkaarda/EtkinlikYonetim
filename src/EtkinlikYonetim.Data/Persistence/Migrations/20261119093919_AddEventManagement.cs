using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar");

            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "Etkinlikler",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Existing events are not cancelled. The default only fills the existing rows: like the other
            // columns of the model the new one has no default constraint afterwards.
            migrationBuilder.Sql(
                "ALTER TABLE dbo.Etkinlikler ADD IptalEdildi bit NOT NULL CONSTRAINT DF_Upgrade_Etkinlikler_IptalEdildi DEFAULT 0 WITH VALUES; "
                + "ALTER TABLE dbo.Etkinlikler DROP CONSTRAINT DF_Upgrade_Etkinlikler_IptalEdildi;");

            migrationBuilder.AddColumn<DateTime>(
                name: "IptalZamani",
                table: "Etkinlikler",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar",
                sql: "[Durum] IN (0, 1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older schema cannot tell an event's cancellation from a participant's: the closed
            // registrations become cancelled ones. The events themselves lose their cancelled mark.
            migrationBuilder.Sql("UPDATE dbo.Kayitlar SET Durum = 1 WHERE Durum = 3;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar");

            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "Etkinlikler");

            migrationBuilder.DropColumn(
                name: "IptalEdildi",
                table: "Etkinlikler");

            migrationBuilder.DropColumn(
                name: "IptalZamani",
                table: "Etkinlikler");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar",
                sql: "[Durum] IN (0, 1, 2)");
        }
    }
}
