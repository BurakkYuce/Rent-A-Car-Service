using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlogSeoAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AltBaslik",
                table: "BlogYazilari",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnahtarKelimeler",
                table: "BlogYazilari",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AramaDisi",
                table: "BlogYazilari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KapakAlt",
                table: "BlogYazilari",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaAciklama",
                table: "BlogYazilari",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeoBaslik",
                table: "BlogYazilari",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yazar",
                table: "BlogYazilari",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AltBaslik",
                table: "BlogYazilari");

            migrationBuilder.DropColumn(
                name: "AnahtarKelimeler",
                table: "BlogYazilari");

            migrationBuilder.DropColumn(
                name: "AramaDisi",
                table: "BlogYazilari");

            migrationBuilder.DropColumn(
                name: "KapakAlt",
                table: "BlogYazilari");

            migrationBuilder.DropColumn(
                name: "MetaAciklama",
                table: "BlogYazilari");

            migrationBuilder.DropColumn(
                name: "SeoBaslik",
                table: "BlogYazilari");

            migrationBuilder.DropColumn(
                name: "Yazar",
                table: "BlogYazilari");
        }
    }
}
