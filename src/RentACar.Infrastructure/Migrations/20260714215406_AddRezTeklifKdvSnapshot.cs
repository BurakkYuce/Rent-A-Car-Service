using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRezTeklifKdvSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiyatTuru",
                table: "Reservations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KdvOranSnapshot",
                table: "Reservations",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiyatTuru",
                table: "Quotations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KdvOranSnapshot",
                table: "Quotations",
                type: "numeric(9,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiyatTuru",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "KdvOranSnapshot",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "FiyatTuru",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "KdvOranSnapshot",
                table: "Quotations");
        }
    }
}
