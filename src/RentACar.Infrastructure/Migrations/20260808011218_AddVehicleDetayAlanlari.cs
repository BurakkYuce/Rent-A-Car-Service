using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleDetayAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IhaleFirmasi",
                table: "VehicleSales",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IhaleTarihi",
                table: "VehicleSales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NoterSatisTarihi",
                table: "VehicleSales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AlisEuro",
                table: "Vehicles",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlisEuroFiyat",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AraciAlan",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssistanFirma",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BelgeNo",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DisKmLimit",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HgsFirma",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KiraBekTar",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KiraBitTar",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KiraFiyat",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KiraGun",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KiraMusteriId",
                table: "Vehicles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kiralayan",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdemeSekli",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasifSebep",
                table: "Vehicles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuhsatSahibi",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SatisEuroFiyat",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SonDurum",
                table: "Vehicles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SonTeslimKm",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SonTeslimTarihi",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SozNo",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TsbKaskoDegeri",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TsbKodu",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IhaleFirmasi",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "IhaleTarihi",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "NoterSatisTarihi",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "AlisEuro",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlisEuroFiyat",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AraciAlan",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AssistanFirma",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "BelgeNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DisKmLimit",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "HgsFirma",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KiraBekTar",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KiraBitTar",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KiraFiyat",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KiraGun",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KiraMusteriId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Kiralayan",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OdemeSekli",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "PasifSebep",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "RuhsatSahibi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SatisEuroFiyat",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SonDurum",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SonTeslimKm",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SonTeslimTarihi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SozNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TsbKaskoDegeri",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TsbKodu",
                table: "Vehicles");
        }
    }
}
