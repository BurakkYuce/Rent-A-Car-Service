using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-41 — HukukDosyalari alan derinliği (canlı hukuk_birimi.aspx): fatura no + 2 avukatın
    /// iletişimi + BİLGİ amaçlı Tahsilat.
    ///
    /// <para>RLS bloğu YOK — tablo zaten mevcut ve tenant_isolation policy'si aktif; yeni tablo
    /// açılmıyor. Backfill de YOK: tüm kolonlar NULLABLE ve default'suz eklendi. Bilinçli — Tahsilat'a
    /// 0 default'u basmak mevcut satırlara "hiç tahsilat yapılmadı" ANLAMINI yüklerdi; NULL "girilmemiş"
    /// demektir. (Kalan zaten türetilmiş: Tutar − COALESCE(Tahsilat,0).)</para>
    /// </summary>
    public partial class AddHukukDosyaDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Avukat2Ad",
                table: "HukukDosyalari",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Avukat2Mail",
                table: "HukukDosyalari",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Avukat2Tel",
                table: "HukukDosyalari",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvukatMail",
                table: "HukukDosyalari",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvukatTel",
                table: "HukukDosyalari",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaNoTemp",
                table: "HukukDosyalari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Tahsilat",
                table: "HukukDosyalari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HukukDosyalari_TenantId_CariId",
                table: "HukukDosyalari",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_HukukDosyalari_TenantId_FaturaNoTemp",
                table: "HukukDosyalari",
                columns: new[] { "TenantId", "FaturaNoTemp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HukukDosyalari_TenantId_CariId",
                table: "HukukDosyalari");

            migrationBuilder.DropIndex(
                name: "IX_HukukDosyalari_TenantId_FaturaNoTemp",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "Avukat2Ad",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "Avukat2Mail",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "Avukat2Tel",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "AvukatMail",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "AvukatTel",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "FaturaNoTemp",
                table: "HukukDosyalari");

            migrationBuilder.DropColumn(
                name: "Tahsilat",
                table: "HukukDosyalari");
        }
    }
}
