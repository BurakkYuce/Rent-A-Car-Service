using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSettingsRenkKodlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RenkAlacakli",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkBugunCikacaklar",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkBugunDonecekler",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkGecikenler",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkKiralanmayan",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkLimitBakiye",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkOpsiyonlu",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RenkRezAtananPlaka",
                table: "Ayarlar",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenkAlacakli",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkBugunCikacaklar",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkBugunDonecekler",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkGecikenler",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkKiralanmayan",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkLimitBakiye",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkOpsiyonlu",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RenkRezAtananPlaka",
                table: "Ayarlar");
        }
    }
}
