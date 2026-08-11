using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-18 — VehicleSale + Baf additive bilgi alanları (canlı arac_satis / baf_islemleri paritesi).
    /// <para>
    /// TÜM yeni kolonlar BİLGİDİR: hiçbiri deftere (AccountLedgerEntry) yazılmaz, hiçbir rapor
    /// toplamına girmez (KARARLAR.md "yeni tutar alanları deftere yazmaz").
    /// </para>
    /// <para>
    /// RLS: her iki tablo da kendi migration'ında ENABLE+FORCE ROW LEVEL SECURITY + tenant_isolation
    /// policy ile korunuyor; policy SATIR düzeyindedir → yeni KOLONLAR için ek RLS bloğu GEREKMEZ.
    /// Grant'lar da tablo düzeyindedir (kolon-grant kullanılmıyor) → yeni grant gerekmez.
    /// </para>
    /// <para>
    /// BACKFILL YOK — bilinçli: <c>KirayaVerme</c>/<c>SatisiVerildi</c>/<c>KirayaVer</c> birer
    /// KUTU İŞARETİDİR; mevcut satırlarda kimse işaretlemediği için <c>false</c> ("işaretlenmemiş")
    /// DOĞRU anlamdır — tahmin/varsayım yüklenmiyor. Ölçüm taşıyan alanlar (IlanKm, saatler, şube,
    /// kullanım amacı) NULLABLE bırakıldı: "bilinmiyor" ile "0/boş" karışmasın. VehicleSales zaten
    /// DB-immutable (vehiclesales_immutable trigger) → geriye dönük UPDATE de yapılamazdı.
    /// </para>
    /// <para>
    /// NOT: bu dosya ELLE sadeleştirildi — `dotnet ef migrations add`, main'deki bazı migration
    /// Designer snapshot'ları bayat olduğu için başka fazların (Reservations/Ayarlar/AracSiparisleri)
    /// kolonlarını da bu diff'e katmıştı; o kolonlar kendi migration'larında zaten ekleniyor
    /// (çift ekleme "column already exists" ile patlıyordu). Burada YALNIZ FAZ-18 işlemleri var.
    /// </para>
    /// </summary>
    public partial class AddSatisVeBafAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- VehicleSale (canlı arac_satis.aspx) ----
            migrationBuilder.AddColumn<string>(
                name: "Aciklama2",
                table: "VehicleSales",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IhaleSayisi",
                table: "VehicleSales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IlanKm",
                table: "VehicleSales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KirayaVerme",
                table: "VehicleSales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ListeDoviz",
                table: "VehicleSales",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatisNoktasi",
                table: "VehicleSales",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SatisiVerildi",
                table: "VehicleSales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UygulananKampanya",
                table: "VehicleSales",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YevmiyeNumarasi",
                table: "VehicleSales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // ---- Baf (canlı baf_islemleri.aspx) ----
            migrationBuilder.AddColumn<TimeOnly>(
                name: "CikisSaat",
                table: "Baflar",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "DonusSaat",
                table: "Baflar",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DonusSube",
                table: "Baflar",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KirayaVer",
                table: "Baflar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "KullanimAmaci",
                table: "Baflar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Onaylayan",
                table: "Baflar",
                type: "uuid",
                nullable: true);

            // Liste filtrelerinin tarih aralığı için (FAZ-18 arama formları).
            migrationBuilder.CreateIndex(
                name: "IX_VehicleSales_TenantId_Tarih",
                table: "VehicleSales",
                columns: new[] { "TenantId", "Tarih" });

            migrationBuilder.CreateIndex(
                name: "IX_Baflar_TenantId_CikisTarihi",
                table: "Baflar",
                columns: new[] { "TenantId", "CikisTarihi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_VehicleSales_TenantId_Tarih", table: "VehicleSales");
            migrationBuilder.DropIndex(name: "IX_Baflar_TenantId_CikisTarihi", table: "Baflar");

            migrationBuilder.DropColumn(name: "Aciklama2", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "IhaleSayisi", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "IlanKm", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "KirayaVerme", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "ListeDoviz", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "SatisNoktasi", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "SatisiVerildi", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "UygulananKampanya", table: "VehicleSales");
            migrationBuilder.DropColumn(name: "YevmiyeNumarasi", table: "VehicleSales");

            migrationBuilder.DropColumn(name: "CikisSaat", table: "Baflar");
            migrationBuilder.DropColumn(name: "DonusSaat", table: "Baflar");
            migrationBuilder.DropColumn(name: "DonusSube", table: "Baflar");
            migrationBuilder.DropColumn(name: "KirayaVer", table: "Baflar");
            migrationBuilder.DropColumn(name: "KullanimAmaci", table: "Baflar");
            migrationBuilder.DropColumn(name: "Onaylayan", table: "Baflar");
        }
    }
}
