using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMegaFormKucukler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OpsiyonGun",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpsiyonNet",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RiskOnay",
                table: "Rentals",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpsiyonGun",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OpsiyonNet",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "RiskOnay",
                table: "Rentals");
        }
    }
}
