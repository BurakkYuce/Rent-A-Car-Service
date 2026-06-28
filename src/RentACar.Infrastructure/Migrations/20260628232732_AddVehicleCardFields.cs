using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleCardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlimFaturaNo",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlimYapilanFirma",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetayTipi",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HgsNo",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KasaTipi",
                table: "Vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KiraKmLimiti",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OgsNo",
                table: "Vehicles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlimFaturaNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "AlimYapilanFirma",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DetayTipi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "HgsNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KasaTipi",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "KiraKmLimiti",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OgsNo",
                table: "Vehicles");
        }
    }
}
