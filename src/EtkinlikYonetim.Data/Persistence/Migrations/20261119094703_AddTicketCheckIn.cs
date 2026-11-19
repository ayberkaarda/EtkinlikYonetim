using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtkinlikYonetim.Data.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "BiletKodu",
                table: "Biletler",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<byte[]>(
                name: "BiletKoduOzeti",
                table: "Biletler",
                type: "binary(32)",
                fixedLength: true,
                maxLength: 32,
                nullable: true);

            // Every existing ticket gets a new code of 128 random bits from the database's cryptographic
            // generator (32 hexadecimal characters after "ETK-"): the old codes had 50 bits, and those of the
            // legacy upgrade 40, too few for a code that alone opens the entrance. The lookup hash is the
            // SHA-256 of the code's ASCII text, as TicketCodeHash computes it.
            migrationBuilder.Sql("UPDATE dbo.Biletler SET BiletKodu = N'ETK-' + CONVERT(nvarchar(64), CRYPT_GEN_RANDOM(16), 2);");
            migrationBuilder.Sql("UPDATE dbo.Biletler SET BiletKoduOzeti = HASHBYTES('SHA2_256', CONVERT(varchar(40), BiletKodu));");

            migrationBuilder.AlterColumn<byte[]>(
                name: "BiletKoduOzeti",
                table: "Biletler",
                type: "binary(32)",
                fixedLength: true,
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(byte[]),
                oldType: "binary(32)",
                oldFixedLength: true,
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GirisZamani",
                table: "Biletler",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Biletler_BiletKoduOzeti",
                table: "Biletler",
                column: "BiletKoduOzeti",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Biletler_BiletKoduOzeti",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "BiletKoduOzeti",
                table: "Biletler");

            migrationBuilder.DropColumn(
                name: "GirisZamani",
                table: "Biletler");

            // The older column holds 20 characters: the tickets get new codes of that length (64 random bits).
            migrationBuilder.Sql("UPDATE dbo.Biletler SET BiletKodu = N'ETK-' + CONVERT(nvarchar(32), CRYPT_GEN_RANDOM(8), 2);");

            migrationBuilder.AlterColumn<string>(
                name: "BiletKodu",
                table: "Biletler",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);
        }
    }
}
