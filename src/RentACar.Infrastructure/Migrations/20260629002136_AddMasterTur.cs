using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterTur : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Turu",
                table: "OzelKodlar",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tur",
                table: "GiderTurleri",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Turu",
                table: "OzelKodlar");

            migrationBuilder.DropColumn(
                name: "Tur",
                table: "GiderTurleri");
        }
    }
}
