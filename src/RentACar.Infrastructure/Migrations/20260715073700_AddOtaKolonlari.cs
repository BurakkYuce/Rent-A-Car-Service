using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOtaKolonlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OtaBebekKoltugu",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaCdw",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaDropBedeli",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaEkSurucu",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaKiraBedeli",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaLcf",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaNavigasyon",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtaScdw",
                table: "Reservations",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OtaBebekKoltugu",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaCdw",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaDropBedeli",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaEkSurucu",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaKiraBedeli",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaLcf",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaNavigasyon",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "OtaScdw",
                table: "Reservations");
        }
    }
}
