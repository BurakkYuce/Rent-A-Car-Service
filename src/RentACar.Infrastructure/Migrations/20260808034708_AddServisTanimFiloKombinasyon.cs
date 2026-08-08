using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServisTanimFiloKombinasyon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Marka",
                table: "ServisTanimlari",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tip",
                table: "ServisTanimlari",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Vites",
                table: "ServisTanimlari",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yakit",
                table: "ServisTanimlari",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServisTanimlari_TenantId_Marka_Tip_Yakit_Vites",
                table: "ServisTanimlari",
                columns: new[] { "TenantId", "Marka", "Tip", "Yakit", "Vites" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServisTanimlari_TenantId_Marka_Tip_Yakit_Vites",
                table: "ServisTanimlari");

            migrationBuilder.DropColumn(
                name: "Marka",
                table: "ServisTanimlari");

            migrationBuilder.DropColumn(
                name: "Tip",
                table: "ServisTanimlari");

            migrationBuilder.DropColumn(
                name: "Vites",
                table: "ServisTanimlari");

            migrationBuilder.DropColumn(
                name: "Yakit",
                table: "ServisTanimlari");
        }
    }
}
