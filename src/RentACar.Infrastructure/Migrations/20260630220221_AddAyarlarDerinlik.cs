using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAyarlarDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Ayarlar",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxKiraGun",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinKiraGun",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RezOnayZorunlu",
                table: "Ayarlar",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpKullanici",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpSifreEnc",
                table: "Ayarlar",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpSsl",
                table: "Ayarlar",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VarsayilanDoviz",
                table: "Ayarlar",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VarsayilanKdvOrani",
                table: "Ayarlar",
                type: "numeric(5,4)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "MaxKiraGun",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "MinKiraGun",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "RezOnayZorunlu",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SmtpKullanici",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SmtpSifreEnc",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SmtpSsl",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "VarsayilanDoviz",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "VarsayilanKdvOrani",
                table: "Ayarlar");
        }
    }
}
