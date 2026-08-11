using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceBilgiAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvrakNo",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaOzelKod",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GonderimSekli",
                table: "Invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IslemSube",
                table: "Invoices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KdvSifirSebep",
                table: "Invoices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdemeTuru",
                table: "Invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvrakNo",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FaturaOzelKod",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "GonderimSekli",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IslemSube",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "KdvSifirSebep",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OdemeTuru",
                table: "Invoices");
        }
    }
}
