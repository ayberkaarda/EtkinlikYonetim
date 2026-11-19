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

        /// <summary>
        /// Not a lossless way back: the earlier index allowed a national id only once in the whole
        /// table, while this migration allows it once per account. When two accounts have a participant
        /// with the same national id, the rollback stops before it changes anything and those rows
        /// have to be resolved by hand first; nothing is deleted or merged automatically.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT TcKimlikNo FROM dbo.Katilimcilar GROUP BY TcKimlikNo HAVING COUNT(*) > 1)
                    THROW 50002, N'Participants of different accounts share a national id (TcKimlikNo); resolve them by hand before rolling back ScopeNationalIdToOwner.', 1;
                """);

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
