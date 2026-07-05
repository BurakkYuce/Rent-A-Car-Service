using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiraTurevAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Doviz",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturalamaTipi",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiyatTuru",
                table: "Rentals",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KiralamaTuru",
                table: "Rentals",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Doviz",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FaturalamaTipi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FiyatTuru",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KiralamaTuru",
                table: "Rentals");
        }
    }
}
