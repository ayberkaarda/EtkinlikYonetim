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
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnahtarOzeti = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GecerlilikBitis = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KullanimZamani = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BasarisizDeneme = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KurulumAnahtarlari", x => x.Id);
                    table.CheckConstraint("CK_KurulumAnahtarlari_BasarisizDeneme", "[BasarisizDeneme] >= 0");
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
