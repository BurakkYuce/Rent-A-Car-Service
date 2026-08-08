using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerFormDerinlik2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "Customers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AileSira",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AnonimAd",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnonimAdres",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnonimBelge",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnonimMail",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnonimTc",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AnonimTelefon",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AracVerilmez",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BakiyeGor",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "BayiKomisyon",
                table: "Customers",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Broker",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CiltNo",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DogumGunuTakip",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DogumYeri",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntegrasyonKodu",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FaturaAdresFarkli",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FaturaKiralayanIsim",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FaturaTekSatir",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FindexZorunlu",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "FirmaId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IsAdresi",
                table: "Customers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IsTelefonu",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IslemSubeId",
                table: "Customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KaraZamani",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KayitliIl",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KayitliIlce",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KurumsalNo",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MahalleKoy",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MerkezKurumsal",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OzelKod",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PasaportTarihi",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasaportYeri",
                table: "Customers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiskIzin",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriNo",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SifreHash",
                table: "Customers",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiraNo",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TcDogrulama",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tel2",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TevkifatKodu",
                table: "Customers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ulke",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UyariSerbest",
                table: "Customers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WebIndirim",
                table: "Customers",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "YasEhliyetSerbest",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_EntegrasyonKodu",
                table: "Customers",
                columns: new[] { "TenantId", "EntegrasyonKodu" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_FirmaId",
                table: "Customers",
                columns: new[] { "TenantId", "FirmaId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_EntegrasyonKodu",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_FirmaId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AileSira",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AnonimAd",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AnonimAdres",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AnonimBelge",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AnonimMail",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AnonimTc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AnonimTelefon",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "AracVerilmez",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BakiyeGor",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BayiKomisyon",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Broker",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CiltNo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DogumGunuTakip",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DogumYeri",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "EntegrasyonKodu",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FaturaAdresFarkli",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FaturaKiralayanIsim",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FaturaTekSatir",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FindexZorunlu",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsAdresi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsTelefonu",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IslemSubeId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "KaraZamani",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "KayitliIl",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "KayitliIlce",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "KurumsalNo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MahalleKoy",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MerkezKurumsal",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OzelKod",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PasaportTarihi",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PasaportYeri",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RiskIzin",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SeriNo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SifreHash",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SiraNo",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TcDogrulama",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Tel2",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TevkifatKodu",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Ulke",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "UyariSerbest",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WebIndirim",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "YasEhliyetSerbest",
                table: "Customers");
        }
    }
}
