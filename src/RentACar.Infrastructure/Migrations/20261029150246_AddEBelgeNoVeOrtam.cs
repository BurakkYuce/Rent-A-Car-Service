using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEBelgeNoVeOrtam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BelgeNo",
                table: "EBelgeler",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SonDenemeCanli",
                table: "EBelgeler",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EArsivSeriKodu",
                table: "Ayarlar",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EFaturaSeriKodu",
                table: "Ayarlar",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EBelgeler_TenantId_SonDenemeCanli_BelgeNo",
                table: "EBelgeler",
                columns: new[] { "TenantId", "SonDenemeCanli", "BelgeNo" },
                unique: true,
                filter: "\"BelgeNo\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EBelgeler_TenantId_SonDenemeCanli_BelgeNo",
                table: "EBelgeler");

            migrationBuilder.DropColumn(
                name: "BelgeNo",
                table: "EBelgeler");

            migrationBuilder.DropColumn(
                name: "SonDenemeCanli",
                table: "EBelgeler");

            migrationBuilder.DropColumn(
                name: "EArsivSeriKodu",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "EFaturaSeriKodu",
                table: "Ayarlar");
        }
    }
}
