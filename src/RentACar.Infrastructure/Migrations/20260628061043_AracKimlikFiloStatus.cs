using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AracKimlikFiloStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FiloDurum",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModelYili",
                table: "Vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MotorNo",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Renk",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SasiNo",
                table: "Vehicles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segment",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sipp",
                table: "Vehicles",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tip",
                table: "Vehicles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Vites",
                table: "Vehicles",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiloDurum",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ModelYili",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "MotorNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Renk",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "SasiNo",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Segment",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Sipp",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Tip",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "Vites",
                table: "Vehicles");
        }
    }
}
