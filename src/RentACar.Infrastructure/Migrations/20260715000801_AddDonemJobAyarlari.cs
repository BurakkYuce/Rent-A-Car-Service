using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDonemJobAyarlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DonemselFaturalama",
                table: "Rentals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DonemselFaturalamaJob",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DonemselOtomatikTahsilat",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DonemselFaturalama",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "DonemselFaturalamaJob",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "DonemselOtomatikTahsilat",
                table: "Ayarlar");
        }
    }
}
