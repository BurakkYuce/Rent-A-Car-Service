using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiraListesiVeKuralDerinligi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VadeTar",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HaftaGunKisiti",
                table: "KiralamaKurallari",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HesaplamaTipi",
                table: "KiralamaKurallari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HizliIslem",
                table: "KiralamaKurallari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "KuponGecerlilik",
                table: "KiralamaKurallari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PromosyonTuru",
                table: "KiralamaKurallari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TalepBas",
                table: "KiralamaKurallari",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TalepBit",
                table: "KiralamaKurallari",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VadeTar",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "HaftaGunKisiti",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "HesaplamaTipi",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "HizliIslem",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "KuponGecerlilik",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "PromosyonTuru",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "TalepBas",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "TalepBit",
                table: "KiralamaKurallari");
        }
    }
}
