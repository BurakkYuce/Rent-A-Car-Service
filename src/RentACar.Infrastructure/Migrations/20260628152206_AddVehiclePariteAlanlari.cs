using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehiclePariteAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AlimBedeli",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AlimTarihi",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlisKdv",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlisOtv",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlisVergisiz",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AracSahibi",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AylikMaliyet",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FiloCikisTarih",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FiloGirisTarih",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiloYonetimMaliyeti",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IkinciElDeger",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MotorGucu",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod1",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod2",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod3",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod4",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod5",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuhsatNo",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SilindirHacmi",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TescilTarihi",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlimBedeli",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlimTarihi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlisKdv",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlisOtv",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlisVergisiz",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AracSahibi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AylikMaliyet",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "FiloCikisTarih",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "FiloGirisTarih",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "FiloYonetimMaliyeti",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "IkinciElDeger",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "MotorGucu",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OzelKod1",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OzelKod2",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OzelKod3",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OzelKod4",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OzelKod5",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "RuhsatNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SilindirHacmi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TescilTarihi",
                table: "Vehicles");
        }
    }
}
