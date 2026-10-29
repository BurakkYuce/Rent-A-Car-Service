using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// T4 — eski site temaları (Pazar, Rota, Vitrin) söküldü (YALNIZ veri; şema ve model snapshot'ı değişmez):
    /// kayıtlı <c>Ayarlar.SiteTemasi</c> değerlerinde <c>pazar</c>/<c>tarife</c> → <c>anahtar</c>,
    /// <c>rota</c>/<c>kontuar</c> → <c>kiyas</c>, <c>vitrin</c> → <c>paket</c> (<c>SiteThemeKeys.LegacyAliases</c> ile
    /// aynı eşleme). Ardından kayıtlı palet (<c>SitePaleti</c>) yeni temanın setlerinden biri değilse NULL'a çekilir →
    /// sitede temanın varsayılan seti (teması boş satırın teması varsayılan <c>anahtar</c> sayılır). Eşleşme büyük/küçük
    /// harf ve baş/son boşluktan bağımsız. Firma rengi (<c>SiteVurguRengi</c>) dokunulmaz.
    ///
    /// <para>Set listeleri <c>SitePaletteKeys.ByTheme</c> ile aynı (bu migration yazıldığı andaki hâli; ileride set
    /// eklenirse bu SQL değişmez — eski veriye uygulanır).</para>
    ///
    /// <para>RLS: "Ayarlar" FORCE RLS'li ve racar_owner'da BYPASSRLS yok → NO FORCE / FORCE parantezi, aynı transaction
    /// (<c>RenameSiteThemeKeys</c> deseni).</para>
    ///
    /// <para>Down NO-OP: bu migration'dan önceki kod de <c>anahtar</c>/<c>kiyas</c>/<c>paket</c> anahtarlarını tanır
    /// (geri dönüş gerekmez); NULL'a çekilen palet zaten bilinemez.</para>
    /// </summary>
    public partial class RetireOldSiteThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                UPDATE "Ayarlar"
                SET "SiteTemasi" = CASE lower(btrim("SiteTemasi"))
                                       WHEN 'pazar' THEN 'anahtar'
                                       WHEN 'tarife' THEN 'anahtar'
                                       WHEN 'rota' THEN 'kiyas'
                                       WHEN 'kontuar' THEN 'kiyas'
                                       WHEN 'vitrin' THEN 'paket'
                                   END
                WHERE lower(btrim("SiteTemasi")) IN ('pazar', 'tarife', 'rota', 'kontuar', 'vitrin');
                """);
            migrationBuilder.Sql("""
                UPDATE "Ayarlar"
                SET "SitePaleti" = NULL
                WHERE "SitePaleti" IS NOT NULL
                  AND NOT CASE coalesce(nullif(lower(btrim("SiteTemasi")), ''), 'anahtar')
                              WHEN 'kiyas' THEN lower(btrim("SitePaleti")) IN ('mavi', 'turkuaz', 'kiremit', 'gece')
                              WHEN 'paket' THEN lower(btrim("SitePaleti")) IN ('kiremit', 'lacivert', 'zeytin', 'grafit')
                              ELSE lower(btrim("SitePaleti")) IN ('zumrut', 'lacivert', 'visne', 'grafit')
                          END;
                """);
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" FORCE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bilinçli no-op (bkz. sınıf açıklaması).
        }
    }
}
