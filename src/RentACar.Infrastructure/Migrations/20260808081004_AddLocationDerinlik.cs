using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BinaNo",
                table: "Locations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BulusmaNoktasi",
                table: "Locations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DropCalismaSekli",
                table: "Locations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DropKarsilamaTuru",
                table: "Locations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EkAciklama",
                table: "Locations",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            // EF varsayılanı defaultValue: "" üretti — BOŞ STRING GEÇERLİ JSON DEĞİLDİR ve
            // Postgres `''::jsonb` dönüşümünde "invalid input syntax for type json" ile migration'ı
            // KIRAR. Boş dizi ile değiştirildi (mevcut ofisler "haftalık saat girilmemiş" olur).
            migrationBuilder.AddColumn<string>(
                name: "HaftalikCalismaSaatleri",
                table: "Locations",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Iata",
                table: "Locations",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IngilizceAd",
                table: "Locations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LokasyonTuru",
                table: "Locations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MapsKonumu",
                table: "Locations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelMail",
                table: "Locations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelTelefon",
                table: "Locations",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostaKodu",
                table: "Locations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tarif",
                table: "Locations",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ulke",
                table: "Locations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WebSira",
                table: "Locations",
                type: "integer",
                nullable: true);

            // Varsayılan FALSE bilinçli: mevcut ofisler halka açık sitede GÖRÜNMEYE DEVAM EDER.
            // true olsaydı bu migration tüm ofisleri siteden bir anda kaldırırdı.
            migrationBuilder.AddColumn<bool>(
                name: "WebdeGizle",
                table: "Locations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_Iata",
                table: "Locations",
                columns: new[] { "TenantId", "Iata" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Locations_TenantId_Iata",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "BinaNo",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "BulusmaNoktasi",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "DropCalismaSekli",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "DropKarsilamaTuru",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "EkAciklama",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "HaftalikCalismaSaatleri",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Iata",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "IngilizceAd",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "LokasyonTuru",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "MapsKonumu",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "OzelMail",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "OzelTelefon",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "PostaKodu",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Tarif",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Ulke",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "WebSira",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "WebdeGizle",
                table: "Locations");
        }
    }
}
