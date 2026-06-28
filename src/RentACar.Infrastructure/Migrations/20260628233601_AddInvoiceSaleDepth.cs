using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceSaleDepth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Devir",
                table: "VehicleSales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HedefFiyat",
                table: "VehicleSales",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatisKanali",
                table: "VehicleSales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SatisKm",
                table: "VehicleSales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VadeTarihi",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Devir",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "HedefFiyat",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "SatisKanali",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "SatisKm",
                table: "VehicleSales");

            migrationBuilder.DropColumn(
                name: "VadeTarihi",
                table: "Invoices");
        }
    }
}
