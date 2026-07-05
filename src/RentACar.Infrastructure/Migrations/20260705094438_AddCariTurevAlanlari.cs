using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCariTurevAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dil",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Doviz",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EhliyetUlke",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusteriTipi",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelCariTip",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TevkifatDurum",
                table: "Customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dil",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Doviz",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EhliyetUlke",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MusteriTipi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OzelCariTip",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TevkifatDurum",
                table: "Customers");
        }
    }
}
