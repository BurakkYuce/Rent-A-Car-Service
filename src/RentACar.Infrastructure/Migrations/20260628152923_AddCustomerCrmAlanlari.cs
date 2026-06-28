using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerCrmAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnaAdi",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BabaAdi",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DogumTarihi",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaDonemi",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MailIzin",
                table: "Customers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasaportNo",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sinif",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmsIzin",
                table: "Customers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TelefonIzin",
                table: "Customers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TevkifatOrani",
                table: "Customers",
                type: "numeric(9,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili1Ad",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili1Mail",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili1Tel",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili2Ad",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili2Mail",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili2Tel",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili3Ad",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili3Mail",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili3Tel",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnaAdi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BabaAdi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DogumTarihi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FaturaDonemi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MailIzin",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PasaportNo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Sinif",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SmsIzin",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TelefonIzin",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TevkifatOrani",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili1Ad",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili1Mail",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili1Tel",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili2Ad",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili2Mail",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili2Tel",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili3Ad",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili3Mail",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Yetkili3Tel",
                table: "Customers");
        }
    }
}
