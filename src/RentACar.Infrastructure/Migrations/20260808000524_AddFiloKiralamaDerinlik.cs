using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiloKiralamaDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CikisKm",
                table: "FiloKiralamalar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DosyaNo",
                table: "FiloKiralamalar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaTuru",
                table: "FiloKiralamalar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiyatTuru",
                table: "FiloKiralamalar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImzaTarih",
                table: "FiloKiralamalar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kaynak",
                table: "FiloKiralamalar",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MakbuzNo",
                table: "FiloKiralamalar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatisTemsilcisi",
                table: "FiloKiralamalar",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SozlesmeNo",
                table: "FiloKiralamalar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SozlesmeTarihi",
                table: "FiloKiralamalar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToplamKm",
                table: "FiloKiralamalar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VadeGun",
                table: "FiloKiralamalar",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiloKiralamalar_TenantId_BasTar",
                table: "FiloKiralamalar",
                columns: new[] { "TenantId", "BasTar" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FiloKiralamalar_TenantId_BasTar",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "CikisKm",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "DosyaNo",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "FaturaTuru",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "FiyatTuru",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "ImzaTarih",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "Kaynak",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "MakbuzNo",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "SatisTemsilcisi",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "SozlesmeNo",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "SozlesmeTarihi",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "ToplamKm",
                table: "FiloKiralamalar");

            migrationBuilder.DropColumn(
                name: "VadeGun",
                table: "FiloKiralamalar");
        }
    }
}
