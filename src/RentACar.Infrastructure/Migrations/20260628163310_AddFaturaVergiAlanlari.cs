using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFaturaVergiAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DamgaVergisi",
                table: "Invoices",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IadeMi",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ManuelMi",
                table: "Invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "Otv",
                table: "Invoices",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TevkifatOran",
                table: "Invoices",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TevkifatTutar",
                table: "Invoices",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DamgaVergisi",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IadeMi",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ManuelMi",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Otv",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TevkifatOran",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TevkifatTutar",
                table: "Invoices");
        }
    }
}
