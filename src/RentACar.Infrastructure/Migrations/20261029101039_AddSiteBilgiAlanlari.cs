using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteBilgiAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SiteDepozitoAciklama",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SiteDepozitoTutari",
                table: "Ayarlar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteGoogleProfilUrl",
                table: "Ayarlar",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SiteGooglePuanTarihiUtc",
                table: "Ayarlar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SiteGooglePuani",
                table: "Ayarlar",
                type: "numeric(2,1)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteGoogleYorumSayisi",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteGunlukKmSiniri",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteIptalKosulu",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SiteKmSinirsiz",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SiteKurulusYili",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteMinEhliyetYili",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SiteMinYas",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteSigortaOzeti",
                table: "Ayarlar",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SiteDepozitoAciklama",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteDepozitoTutari",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteGoogleProfilUrl",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteGooglePuanTarihiUtc",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteGooglePuani",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteGoogleYorumSayisi",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteGunlukKmSiniri",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteIptalKosulu",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteKmSinirsiz",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteKurulusYili",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteMinEhliyetYili",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteMinYas",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SiteSigortaOzeti",
                table: "Ayarlar");
        }
    }
}
