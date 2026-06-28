using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOdemeDerinlikAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Depozito",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DropUcreti",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KomisyonOran",
                table: "Reservations",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KomisyonTutar",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Provizyon",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SonraOdeOran",
                table: "Reservations",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Depozito",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DropUcreti",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KomisyonOran",
                table: "Rentals",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KomisyonTutar",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Provizyon",
                table: "Rentals",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SonraOdeOran",
                table: "Rentals",
                type: "numeric(9,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Depozito",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "DropUcreti",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "KomisyonOran",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "KomisyonTutar",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Provizyon",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "SonraOdeOran",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "Depozito",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "DropUcreti",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KomisyonOran",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KomisyonTutar",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "Provizyon",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "SonraOdeOran",
                table: "Rentals");
        }
    }
}
