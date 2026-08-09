using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAracSiparisCariFkFiyat : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// FAZ-17 — MEVCUT "AracSiparisleri" tablosuna 16 NULLABLE kolon (cari-FK, dosya/temsilci/
        /// spesifikasyon alanları, üç fiyat katmanı, TSB kayıt no, kredi-FK) + iki composite tenant-FK.
        /// <para><b>Yeni RLS bloğu GEREKMEZ:</b> "AracSiparisleri" tablosunda ENABLE + FORCE ROW LEVEL
        /// SECURITY ve tenant_isolation policy ilk migration'da (AddAracSiparis) kurulmuştu; kolon
        /// eklemek policy'yi etkilemez ve racar_app'in tablo düzeyindeki GRANT'i yeni kolonları da
        /// kapsar.</para>
        /// <para><b>BACKFILL YOK — bilinçli:</b> tüm yeni kolonlar nullable ve varsayılansız. Eski
        /// siparişlerde cari/kredi bağı ve fiyat katmanları gerçekten YOKTUR; non-null bir kolona
        /// 0 default'u vermek "piyasa fiyatı 0 TL" anlamına gelir ve mevcut satırlara yanlış anlam
        /// yüklerdi. NULL burada "girilmemiş" demektir, ekranda "—" görünür.</para>
        /// <para><b>AK_AracKredileri_TenantId_Id:</b> AracSiparis.KrediId'nin composite tenant-FK'si
        /// için hedef alternatif anahtar. Tenant sınırını FK'nin KENDİSİ taşır → başka tenant'ın
        /// kredisine sipariş bağlanması yapısal olarak imkânsız. (TenantId+Id zaten benzersiz olduğu
        /// için mevcut veri üzerinde kısıt ihlali üretmez.)</para>
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DosyaNo",
                table: "AracSiparisleri",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FiloFiyat",
                table: "AracSiparisleri",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IcRenk",
                table: "AracSiparisleri",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImzaTarih",
                table: "AracSiparisleri",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KaynakTip",
                table: "AracSiparisleri",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "KrediId",
                table: "AracSiparisleri",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpsFiyat",
                table: "AracSiparisleri",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Opsiyon",
                table: "AracSiparisleri",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OzelTemsilci",
                table: "AracSiparisleri",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PiyasaFiyat",
                table: "AracSiparisleri",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Renk",
                table: "AracSiparisleri",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatisTemsilci",
                table: "AracSiparisleri",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatisTipi",
                table: "AracSiparisleri",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TedarikciCariId",
                table: "AracSiparisleri",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TsbKayitNo",
                table: "AracSiparisleri",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Versiyon",
                table: "AracSiparisleri",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AracKredileri_TenantId_Id",
                table: "AracKredileri",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AracSiparisleri_TenantId_DosyaNo",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "DosyaNo" });

            migrationBuilder.CreateIndex(
                name: "IX_AracSiparisleri_TenantId_KrediId",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "KrediId" });

            migrationBuilder.CreateIndex(
                name: "IX_AracSiparisleri_TenantId_SiparisTarihi",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "SiparisTarihi" });

            migrationBuilder.CreateIndex(
                name: "IX_AracSiparisleri_TenantId_TedarikciCariId",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "TedarikciCariId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AracSiparisleri_AracKredileri_TenantId_KrediId",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "KrediId" },
                principalTable: "AracKredileri",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AracSiparisleri_Customers_TenantId_TedarikciCariId",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "TedarikciCariId" },
                principalTable: "Customers",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AracSiparisleri_AracKredileri_TenantId_KrediId",
                table: "AracSiparisleri");

            migrationBuilder.DropForeignKey(
                name: "FK_AracSiparisleri_Customers_TenantId_TedarikciCariId",
                table: "AracSiparisleri");

            migrationBuilder.DropIndex(
                name: "IX_AracSiparisleri_TenantId_DosyaNo",
                table: "AracSiparisleri");

            migrationBuilder.DropIndex(
                name: "IX_AracSiparisleri_TenantId_KrediId",
                table: "AracSiparisleri");

            migrationBuilder.DropIndex(
                name: "IX_AracSiparisleri_TenantId_SiparisTarihi",
                table: "AracSiparisleri");

            migrationBuilder.DropIndex(
                name: "IX_AracSiparisleri_TenantId_TedarikciCariId",
                table: "AracSiparisleri");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AracKredileri_TenantId_Id",
                table: "AracKredileri");

            migrationBuilder.DropColumn(
                name: "DosyaNo",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "FiloFiyat",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "IcRenk",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "ImzaTarih",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "KaynakTip",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "KrediId",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "OpsFiyat",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "Opsiyon",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "OzelTemsilci",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "PiyasaFiyat",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "Renk",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "SatisTemsilci",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "SatisTipi",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "TedarikciCariId",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "TsbKayitNo",
                table: "AracSiparisleri");

            migrationBuilder.DropColumn(
                name: "Versiyon",
                table: "AracSiparisleri");
        }
    }
}
