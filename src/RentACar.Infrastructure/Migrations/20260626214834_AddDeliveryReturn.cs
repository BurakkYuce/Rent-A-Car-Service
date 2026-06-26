using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EksikYakit",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FazlaKm",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "FazlaKmBedeli",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FazlaKmUcret",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "GenelToplam",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "KmLimit",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "UzatmaBedeli",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UzatmaGun",
                table: "Rentals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "YakitBedeli",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "YakitBirimUcret",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EksikYakit",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FazlaKm",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FazlaKmBedeli",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FazlaKmUcret",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "GenelToplam",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KmLimit",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "UzatmaBedeli",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "UzatmaGun",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "YakitBedeli",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "YakitBirimUcret",
                table: "Rentals");
        }
    }
}
