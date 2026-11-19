using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Etkinlikler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EtkinlikAdi = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Tarih = table.Column<DateOnly>(type: "date", nullable: false),
                    Kapasite = table.Column<int>(type: "int", nullable: false),
                    KayitSayisi = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Etkinlikler", x => x.Id);
                    table.CheckConstraint("CK_Etkinlikler_Kapasite", "[Kapasite] > 0");
                    table.CheckConstraint("CK_Etkinlikler_KayitSayisi", "[KayitSayisi] >= 0 AND [KayitSayisi] <= [Kapasite]");
                });

            migrationBuilder.CreateTable(
                name: "Kullanicilar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KullaniciAdi = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Sifre = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SifreBicimi = table.Column<int>(type: "int", nullable: false),
                    Rol = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BasarisizGiris = table.Column<int>(type: "int", nullable: false),
                    KilitBitis = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kullanicilar", x => x.Id);
                    table.CheckConstraint("CK_Kullanicilar_BasarisizGiris", "[BasarisizGiris] >= 0");
                    table.CheckConstraint("CK_Kullanicilar_Rol", "[Rol] IN ('Kullanici', 'Admin')");
                    table.CheckConstraint("CK_Kullanicilar_SifreBicimi", "[SifreBicimi] IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "Katilimcilar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TcKimlikNo = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    AdSoyad = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    KullaniciId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Katilimcilar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Katilimcilar_Kullanicilar",
                        column: x => x.KullaniciId,
                        principalTable: "Kullanicilar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Kayitlar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KatilimciId = table.Column<int>(type: "int", nullable: false),
                    EtkinlikId = table.Column<int>(type: "int", nullable: false),
                    KayitZamani = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Durum = table.Column<int>(type: "int", nullable: false),
                    IptalZamani = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kayitlar", x => x.Id);
                    table.CheckConstraint("CK_Kayitlar_Durum", "[Durum] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_Kayitlar_Etkinlikler",
                        column: x => x.EtkinlikId,
                        principalTable: "Etkinlikler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Kayitlar_Katilimcilar",
                        column: x => x.KatilimciId,
                        principalTable: "Katilimcilar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Biletler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    KayitId = table.Column<int>(type: "int", nullable: false),
                    BiletKodu = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OlusturmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Biletler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Biletler_Kayitlar",
                        column: x => x.KayitId,
                        principalTable: "Kayitlar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_BiletKodu",
                table: "Biletler",
                column: "BiletKodu",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_KayitId",
                table: "Biletler",
                column: "KayitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Etkinlikler_Tarih",
                table: "Etkinlikler",
                column: "Tarih");

            migrationBuilder.CreateIndex(
                name: "IX_Katilimcilar_KullaniciId",
                table: "Katilimcilar",
                column: "KullaniciId");

            migrationBuilder.CreateIndex(
                name: "IX_Katilimcilar_TcKimlikNo",
                table: "Katilimcilar",
                column: "TcKimlikNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kayitlar_EtkinlikId",
                table: "Kayitlar",
                column: "EtkinlikId");

            migrationBuilder.CreateIndex(
                name: "IX_Kayitlar_KatilimciId_EtkinlikId",
                table: "Kayitlar",
                columns: new[] { "KatilimciId", "EtkinlikId" },
                unique: true,
                filter: "[Durum] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_KullaniciAdi",
                table: "Kullanicilar",
                column: "KullaniciAdi",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Biletler");

            migrationBuilder.DropTable(
                name: "Kayitlar");

            migrationBuilder.DropTable(
                name: "Etkinlikler");

            migrationBuilder.DropTable(
                name: "Katilimcilar");

            migrationBuilder.DropTable(
                name: "Kullanicilar");
        }
    }
}
