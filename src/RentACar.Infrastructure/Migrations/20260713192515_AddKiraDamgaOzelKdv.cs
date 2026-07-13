using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiraDamgaOzelKdv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DamgaVergisi",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OzelKdvOran",
                table: "Rentals",
                type: "numeric(9,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DamgaVergisi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OzelKdvOran",
                table: "Rentals");
        }
    }
}
