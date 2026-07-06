using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDonusAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BitisSebebi",
                table: "Rentals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KmHediye",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeslimAlanPersonelId",
                table: "Rentals",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BitisSebebi",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KmHediye",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "TeslimAlanPersonelId",
                table: "Rentals");
        }
    }
}
