using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CariZenginlestirme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EhliyetNo",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EhliyetSinifi",
                table: "Customers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EhliyetTarihi",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EhliyetYeri",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Gsm2",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HgsYansitmaTuru",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IysIzinli",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kaynak",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusteriTemsilcisi",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiskMesaji",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RiskTarihi",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Uyari",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UyariNedeni",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EhliyetNo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EhliyetSinifi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EhliyetTarihi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EhliyetYeri",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Gsm2",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "HgsYansitmaTuru",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IysIzinli",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Kaynak",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MusteriTemsilcisi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RiskMesaji",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RiskTarihi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Uyari",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UyariNedeni",
                table: "Customers");
        }
    }
}
