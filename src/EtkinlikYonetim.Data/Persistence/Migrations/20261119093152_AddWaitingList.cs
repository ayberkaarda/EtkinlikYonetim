using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitingList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Kayitlar_KatilimciId_EtkinlikId",
                table: "Kayitlar");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar");

            migrationBuilder.AddColumn<int>(
                name: "SiraNo",
                table: "Kayitlar",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kayitlar_EtkinlikId_SiraNo",
                table: "Kayitlar",
                columns: new[] { "EtkinlikId", "SiraNo" },
                unique: true,
                filter: "[SiraNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Kayitlar_KatilimciId_EtkinlikId",
                table: "Kayitlar",
                columns: new[] { "KatilimciId", "EtkinlikId" },
                unique: true,
                filter: "[Durum] IN (0, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar",
                sql: "[Durum] IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older schema has no waiting list: places on it are cancelled, so they cannot clash with the
            // narrower status check and index below.
            migrationBuilder.Sql("UPDATE dbo.Kayitlar SET Durum = 1, IptalZamani = SYSUTCDATETIME() WHERE Durum = 2;");

            migrationBuilder.DropIndex(
                name: "IX_Kayitlar_EtkinlikId_SiraNo",
                table: "Kayitlar");

            migrationBuilder.DropIndex(
                name: "IX_Kayitlar_KatilimciId_EtkinlikId",
                table: "Kayitlar");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar");

            migrationBuilder.DropColumn(
                name: "SiraNo",
                table: "Kayitlar");

            migrationBuilder.CreateIndex(
                name: "IX_Kayitlar_KatilimciId_EtkinlikId",
                table: "Kayitlar",
                columns: new[] { "KatilimciId", "EtkinlikId" },
                unique: true,
                filter: "[Durum] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Kayitlar_Durum",
                table: "Kayitlar",
                sql: "[Durum] IN (0, 1)");
        }
    }
}
