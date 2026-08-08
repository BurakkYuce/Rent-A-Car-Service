using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRateMatrixKmKademe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Km1",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Km1Ucret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Km2",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Km2Ucret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Km3",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Km3Ucret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Km4",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Km4Ucret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Km5",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Km5Ucret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Km6",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Km6Ucret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KmAylik",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KmAylikUcret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KmHaftalik",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KmHaftalikUcret",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Km1",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km1Ucret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km2",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km2Ucret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km3",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km3Ucret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km4",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km4Ucret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km5",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km5Ucret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km6",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Km6Ucret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "KmAylik",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "KmAylikUcret",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "KmHaftalik",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "KmHaftalikUcret",
                table: "TarifeMatris");
        }
    }
}
