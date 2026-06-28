using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleGroupKuralAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AylikMaxKm",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BuyukBagaj",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GencEhliyetMinYil",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KrediKartiSart",
                table: "AracGruplari",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KucukBagaj",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Marka",
                table: "AracGruplari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Muafiyet2",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Provizyon2",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SonraOdeOran",
                table: "AracGruplari",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tipi",
                table: "AracGruplari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpgradeSira",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WebSira",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "YakitFiyati",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AylikMaxKm",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "BuyukBagaj",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "GencEhliyetMinYil",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "KrediKartiSart",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "KucukBagaj",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Marka",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Muafiyet2",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Provizyon2",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "SonraOdeOran",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Tipi",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "UpgradeSira",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "WebSira",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "YakitFiyati",
                table: "AracGruplari");
        }
    }
}
