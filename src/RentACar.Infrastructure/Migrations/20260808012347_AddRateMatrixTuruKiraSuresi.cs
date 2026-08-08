using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRateMatrixTuruKiraSuresi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KiraSuresi",
                table: "TarifeMatris",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Turu",
                table: "TarifeMatris",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KiraSuresi",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "Turu",
                table: "TarifeMatris");
        }
    }
}
