using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRezervasyonTalepAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeldigiBirim",
                table: "Reservations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnayKodu",
                table: "Reservations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjeAdi",
                table: "Reservations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TalepTuru",
                table: "Reservations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_Durum_BasTar",
                table: "Reservations",
                columns: new[] { "TenantId", "Durum", "BasTar" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reservations_TenantId_Durum_BasTar",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "GeldigiBirim",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OnayKodu",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ProjeAdi",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "TalepTuru",
                table: "Reservations");
        }
    }
}
