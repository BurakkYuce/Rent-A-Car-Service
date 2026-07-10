using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiraFormAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AksIlkYardimCikis",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksIlkYardimDonus",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AksLastikCikis",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AksLastikDonus",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksStepneCikis",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksStepneDonus",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksYedekAnahtarCikis",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksYedekAnahtarDonus",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksZincirCikis",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AksZincirDonus",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssistFirma",
                table: "Rentals",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EkKosullar",
                table: "Rentals",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FaturaListesindeGizle",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmaKodu",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GeldigiBirim",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KabisCikis",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KabisDonus",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kaynak",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KefilBilgisi",
                table: "Rentals",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManuelFindexPuan",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnayKodu",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OtomatikUzat",
                table: "Rentals",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelFaturaAciklama",
                table: "Rentals",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelSoforBilgisi",
                table: "Rentals",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjeAdi",
                table: "Rentals",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvizyonNo",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProvizyonTarih",
                table: "Rentals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TalepTuru",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UcusNo",
                table: "Rentals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UyariAciklama",
                table: "Rentals",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AksIlkYardimCikis",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksIlkYardimDonus",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksLastikCikis",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksLastikDonus",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksStepneCikis",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksStepneDonus",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksYedekAnahtarCikis",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksYedekAnahtarDonus",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksZincirCikis",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AksZincirDonus",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "AssistFirma",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "EkKosullar",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FaturaListesindeGizle",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "FirmaKodu",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "GeldigiBirim",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KabisCikis",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KabisDonus",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "Kaynak",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KefilBilgisi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ManuelFindexPuan",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OnayKodu",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OtomatikUzat",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OzelFaturaAciklama",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OzelKod",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "OzelSoforBilgisi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ProjeAdi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ProvizyonNo",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "ProvizyonTarih",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "TalepTuru",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "UcusNo",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "UyariAciklama",
                table: "Rentals");
        }
    }
}
