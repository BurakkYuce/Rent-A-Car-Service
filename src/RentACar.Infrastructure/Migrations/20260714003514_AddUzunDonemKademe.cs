using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUzunDonemKademe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GunAylik",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GunHaftalik",
                table: "TarifeMatris",
                type: "numeric(19,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GunAylik",
                table: "TarifeMatris");

            migrationBuilder.DropColumn(
                name: "GunHaftalik",
                table: "TarifeMatris");
        }
    }
}
