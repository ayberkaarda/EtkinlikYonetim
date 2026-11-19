using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSetupKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KurulumAnahtarlari",
                columns: table => new
                {
                    AnahtarID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnahtarOzeti = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    Olusturma = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SonKullanma = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Tuketildi = table.Column<bool>(type: "bit", nullable: false),
                    TuketimTarihi = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KurulumAnahtarlari", x => x.AnahtarID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KurulumAnahtarlari");
        }
    }
}
