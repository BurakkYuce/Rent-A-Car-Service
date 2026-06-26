using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationKmTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FazlaKmUcret",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "KmLimit",
                table: "Reservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "YakitBirimUcret",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FazlaKmUcret",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "KmLimit",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "YakitBirimUcret",
                table: "Reservations");
        }
    }
}
