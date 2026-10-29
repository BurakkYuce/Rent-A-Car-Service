using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// F13.1b (#350) pilot kapısını kaldırdı; <c>Ayarlar.YeniArayuzPilot</c> o günden beri hiçbir karar vermiyor.
    /// Kolon düşer; Down eski tanımı (boolean, NOT NULL, varsayılan false) geri kurar. RLS/grant tablo düzeyinde,
    /// dokunulmaz.
    /// </summary>
    public partial class DropYeniArayuzPilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YeniArayuzPilot",
                table: "Ayarlar");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "YeniArayuzPilot",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
