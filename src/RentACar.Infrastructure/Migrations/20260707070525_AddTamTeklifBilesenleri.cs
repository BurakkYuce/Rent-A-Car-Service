using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTamTeklifBilesenleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FaturalananGun",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HaftaSonuFark",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HediyeGun",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IskontoTutar",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaturalananGun",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "HaftaSonuFark",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "HediyeGun",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "IskontoTutar",
                table: "Rentals");
        }
    }
}
