using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDokumRezervasyonTeklif : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FaturalananGun",
                table: "Reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HaftaSonuFark",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HediyeGun",
                table: "Reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IskontoTutar",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FaturalananGun",
                table: "Quotations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HaftaSonuFark",
                table: "Quotations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HediyeGun",
                table: "Quotations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IskontoTutar",
                table: "Quotations",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaturalananGun",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "HaftaSonuFark",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "HediyeGun",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "IskontoTutar",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "FaturalananGun",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "HaftaSonuFark",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "HediyeGun",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "IskontoTutar",
                table: "Quotations");
        }
    }
}
