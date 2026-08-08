using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSikayetTeslimBagli : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CikisOfisi",
                table: "Sikayetler",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Puan",
                table: "Sikayetler",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RentalId",
                table: "Sikayetler",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SikayetKanali",
                table: "Sikayetler",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SikayetYeri",
                table: "Sikayetler",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeslimAlanPersonelId",
                table: "Sikayetler",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeslimEdenPersonelId",
                table: "Sikayetler",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sikayetler_TenantId_RentalId",
                table: "Sikayetler",
                columns: new[] { "TenantId", "RentalId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sikayetler_TenantId_RentalId",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "CikisOfisi",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "Puan",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "RentalId",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "SikayetKanali",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "SikayetYeri",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "TeslimAlanPersonelId",
                table: "Sikayetler");

            migrationBuilder.DropColumn(
                name: "TeslimEdenPersonelId",
                table: "Sikayetler");
        }
    }
}
