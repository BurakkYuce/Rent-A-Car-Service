using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSurucuUcretleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EkSurucuUcretGunluk",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GencSurucuUcretGunluk",
                table: "AracGruplari",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EkSurucuUcretGunluk",
                table: "AracGruplari");

            migrationBuilder.DropColumn(
                name: "GencSurucuUcretGunluk",
                table: "AracGruplari");
        }
    }
}
