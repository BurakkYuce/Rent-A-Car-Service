using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonelFormDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "Personeller",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Adres",
                table: "Personeller",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AileSiraNo",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnaAdi",
                table: "Personeller",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BabaAdi",
                table: "Personeller",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CepTel",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CiltNo",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DogumTarihi",
                table: "Personeller",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DogumYeri",
                table: "Personeller",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvTelefonu",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GorevTanimi",
                table: "Personeller",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Il",
                table: "Personeller",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ilce",
                table: "Personeller",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IsTelefonu",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KanGrubu",
                table: "Personeller",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mahalle",
                table: "Personeller",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailAdresi",
                table: "Personeller",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RacTabletNo",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Referans",
                table: "Personeller",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SSinifi",
                table: "Personeller",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SVerilisTarihi",
                table: "Personeller",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SVerilisYeri",
                table: "Personeller",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiraNo",
                table: "Personeller",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_TenantId_GorevTanimi",
                table: "Personeller",
                columns: new[] { "TenantId", "GorevTanimi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Personeller_TenantId_GorevTanimi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Adres",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "AileSiraNo",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "AnaAdi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "BabaAdi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "CepTel",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "CiltNo",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "DogumTarihi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "DogumYeri",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "EvTelefonu",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "GorevTanimi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Il",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Ilce",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "IsTelefonu",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "KanGrubu",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Mahalle",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "MailAdresi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "RacTabletNo",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "Referans",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SSinifi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SVerilisTarihi",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SVerilisYeri",
                table: "Personeller");

            migrationBuilder.DropColumn(
                name: "SiraNo",
                table: "Personeller");
        }
    }
}
