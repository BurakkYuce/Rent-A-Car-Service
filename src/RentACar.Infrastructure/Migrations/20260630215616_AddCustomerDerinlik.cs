using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankaAdi",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankaIban",
                table: "Customers",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EkAdres",
                table: "Customers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaAdresi",
                table: "Customers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaUnvan",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KvkkOnay",
                table: "Customers",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KvkkOnayTarih",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankaAdi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BankaIban",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EkAdres",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FaturaAdresi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FaturaUnvan",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "KvkkOnay",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "KvkkOnayTarih",
                table: "Customers");
        }
    }
}
