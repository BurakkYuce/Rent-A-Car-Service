using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// Halka açık site tema anahtarı göçü (YALNIZ veri; şema ve model snapshot'ı değişmez): kayıtlı
    /// <c>Ayarlar.SiteTemasi</c> değerlerinde <c>tarife</c> → <c>pazar</c>, <c>kontuar</c> → <c>rota</c>
    /// (<c>SiteThemeKeys</c>). <c>vitrin</c> ve null dokunulmaz. Eşleşme büyük/küçük harf ve baş/son boşluktan
    /// bağımsızdır (servis zaten normalize yazar; elle girilmiş eski satır da kaçmasın).
    ///
    /// <para>Eski anahtar uygulamada ayrıca KALICI olarak normalize edilir (<c>SiteThemeKeys.LegacyAliases</c>):
    /// migration'dan önce okunan değer ve paylaşılmış <c>?tema=tarife</c> bağlantısı da doğru temayı basar. Bu
    /// migration DB'de eski değeri bırakmaz ki ERP ayar ekranı seçili temayı göstersin.</para>
    ///
    /// <para>RLS: "Ayarlar" FORCE RLS'li ve racar_owner'da BYPASSRLS yok → düz UPDATE boş <c>app.tenant_id</c> ile
    /// SIFIR satır görürdü (sessizce hiçbir şey yapmazdı). Repo deseni: NO FORCE / FORCE parantezi, aynı
    /// transaction (migration kendi transaction'ında koşar; hata olursa FORCE geri gelir).</para>
    ///
    /// <para>Down eski anahtarlara çevirir (pazar → tarife, rota → kontuar): eski kod yalnız onları tanır.</para>
    /// </summary>
    public partial class RenameSiteThemeKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                UPDATE "Ayarlar"
                SET "SiteTemasi" = CASE lower(btrim("SiteTemasi"))
                                       WHEN 'tarife' THEN 'pazar'
                                       WHEN 'kontuar' THEN 'rota'
                                   END
                WHERE lower(btrim("SiteTemasi")) IN ('tarife', 'kontuar');
                """);
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" FORCE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                UPDATE "Ayarlar"
                SET "SiteTemasi" = CASE lower(btrim("SiteTemasi"))
                                       WHEN 'pazar' THEN 'tarife'
                                       WHEN 'rota' THEN 'kontuar'
                                   END
                WHERE lower(btrim("SiteTemasi")) IN ('pazar', 'rota');
                """);
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" FORCE ROW LEVEL SECURITY;");
        }
    }
}
