using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AracGrubuKuralAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AsimKmUcreti",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BagajSayisi",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EhliyetMinYil",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GencSurucuYas",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GunlukKmLimiti",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KapiSayisi",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KasaTuru",
                table: "AracGruplari",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KoltukSayisi",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MuafiyetTutari",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Provizyon",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segment",
                table: "AracGruplari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sipp",
                table: "AracGruplari",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SurucuMinYas",
                table: "AracGruplari",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AsimKmUcreti",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "BagajSayisi",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "EhliyetMinYil",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "GencSurucuYas",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "GunlukKmLimiti",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "KapiSayisi",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "KasaTuru",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "KoltukSayisi",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "MuafiyetTutari",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Provizyon",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Segment",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Sipp",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "SurucuMinYas",
                table: "AracGruplari");
        }
    }
}
