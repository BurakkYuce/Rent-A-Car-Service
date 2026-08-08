using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleKayitAlanlari2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "Vehicles",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AlimBedeliKur",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AltGrupAdi",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Arac2FiyatKur",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AracSahibi2",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AracSahibiNo",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AracSatisKm",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AylikMaliyetDoviz",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CikmasiPlananTarih",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntegrasyonKodu",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KapatmaTarih",
                table: "Vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Konum",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KrediFirma",
                table: "Vehicles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SahipGrup",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SimdiKur",
                table: "Vehicles",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakipMarka",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TakipNo",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeypKodu",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TsrbMarkaKodu",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TsrbTipKodu",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlimBedeliKur",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AltGrupAdi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Arac2FiyatKur",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AracSahibi2",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AracSahibiNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AracSatisKm",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AylikMaliyetDoviz",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "CikmasiPlananTarih",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "EntegrasyonKodu",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KapatmaTarih",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Konum",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KrediFirma",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SahipGrup",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SimdiKur",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TakipMarka",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TakipNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TeypKodu",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TsrbMarkaKodu",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "TsrbTipKodu",
                table: "Vehicles");
        }
    }
}
