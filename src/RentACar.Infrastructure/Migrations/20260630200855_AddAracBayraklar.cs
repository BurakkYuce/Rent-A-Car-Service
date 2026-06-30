using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAracBayraklar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KarLastigi",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastikDurumu",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OfisRezKapat",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Rehin",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SonBakimKm",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SonBakimTarih",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Temizlik",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "Utts",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WebRezKapat",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "YedekAnahtar",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ZIzni",
                table: "Vehicles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KarLastigi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LastikDurumu",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OfisRezKapat",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Rehin",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SonBakimKm",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SonBakimTarih",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Temizlik",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Utts",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "WebRezKapat",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "YedekAnahtar",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ZIzni",
                table: "Vehicles");
        }
    }
}
