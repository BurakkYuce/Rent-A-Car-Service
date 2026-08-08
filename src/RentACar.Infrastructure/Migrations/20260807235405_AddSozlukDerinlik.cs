using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSozlukDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HediyeCek",
                table: "Hesaplar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod",
                table: "Hesaplar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UyariMailListesi",
                table: "Hesaplar",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ulke",
                table: "Dovizler",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntegrasyonKod1",
                table: "AracGruplari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provizyon2Doviz",
                table: "AracGruplari",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvizyonDoviz",
                table: "AracGruplari",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServisId",
                table: "AracGruplari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Vites",
                table: "AracGruplari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebId",
                table: "AracGruplari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YakitTuru",
                table: "AracGruplari",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HediyeCek",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "OzelKod",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "UyariMailListesi",
                table: "Hesaplar");

            migrationBuilder.DropColumn(
                name: "Ulke",
                table: "Dovizler");

            migrationBuilder.DropColumn(
                name: "EntegrasyonKod1",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Provizyon2Doviz",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "ProvizyonDoviz",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "ServisId",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "Vites",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "WebId",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "YakitTuru",
                table: "AracGruplari");
        }
    }
}
