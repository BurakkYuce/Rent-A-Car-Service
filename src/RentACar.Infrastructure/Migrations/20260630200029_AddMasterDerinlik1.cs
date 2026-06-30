using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterDerinlik1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Banka",
                table: "Hesaplar",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HesapNo",
                table: "Hesaplar",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Iban",
                table: "Hesaplar",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sube",
                table: "Hesaplar",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Grup",
                table: "AracTipleri",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vites",
                table: "AracTipleri",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yakit",
                table: "AracTipleri",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Banka",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "HesapNo",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "Iban",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "Sube",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "Grup",
                table: "AracTipleri");

            migrationBuilder.DropColumn(
                name: "Vites",
                table: "AracTipleri");

            migrationBuilder.DropColumn(
                name: "Yakit",
                table: "AracTipleri");
        }
    }
}
