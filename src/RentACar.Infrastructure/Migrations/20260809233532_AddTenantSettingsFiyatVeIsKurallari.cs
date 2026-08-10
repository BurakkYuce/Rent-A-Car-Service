using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-82 — Ayarlar tablosuna fiyat/muhasebe varsayılanları + kur elle-giriş kilidi (6 additive kolon).
    ///
    /// <para>RLS: "Ayarlar" zaten ENABLE + FORCE ROW LEVEL SECURITY + tenant_isolation politikası ve
    /// tablo-seviyesi GRANT taşıyor (20260628200851_AddTenantSettings) — additive kolonlar için ne yeni
    /// politika ne de ek grant gerekir (kolon-seviyesi grant kullanılmıyor).</para>
    ///
    /// <para>BACKFILL YOK — bilinçli. Nullable kolonlar mevcut satırlarda NULL kalır ve NULL burada
    /// "yapılandırılmadı = bugünkü davranış" demektir (yakıt 8, fiyat türü seçilmemiş, beklemede alanlar
    /// pasif). Tek non-null kolon KurElleGirisKilitli'nin varsayılanı false, yani "kilit kapalı" =
    /// bugünkü davranış. Yani EF'in verdiği varsayılanlar mevcut satırlara YANLIŞ anlam yüklemiyor;
    /// bu yüzden NO FORCE ROW LEVEL SECURITY / UPDATE / FORCE üçlüsüne de gerek yok.</para>
    /// </summary>
    public partial class AddTenantSettingsFiyatVeIsKurallari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DropMesafeYokIseSifir",
                table: "Ayarlar",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IadeIslemSaatSiniri",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KurElleGirisKilitli",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SaatFarkiToleransDk",
                table: "Ayarlar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VarsayilanFiyatTuru",
                table: "Ayarlar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VarsayilanYakitSeviyesi",
                table: "Ayarlar",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DropMesafeYokIseSifir",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "IadeIslemSaatSiniri",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "KurElleGirisKilitli",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SaatFarkiToleransDk",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "VarsayilanFiyatTuru",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "VarsayilanYakitSeviyesi",
                table: "Ayarlar");
        }
    }
}
