using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// #378 adversarial M2 — gelen e-Fatura ETTN tekilliği büyük/küçük harften bağımsız.
    /// <list type="number">
    /// <item><b>Çakışma sayımı (önce):</b> aynı tenant'ta yalnız yazımı farklı (büyük/küçük harf, baş/son boşluk) iki ETTN
    /// varsa migration GÜRÜLTÜLÜ durur (RAISE EXCEPTION, sayıyla). Veri SİLİNMEZ, birleştirilmez — hangi kaydın
    /// geçerli olduğu (giderleştirilmiş mi, reddedilmiş mi) iş kararıdır, migration tahmin etmez.</item>
    /// <item><b>Normalleştirme:</b> UUID biçimli mevcut ETTN'ler kanonik büyük harfe çevrilir (servisin yeni yazdığı
    /// biçim). UUID olmayan eski serbest metinlere DOKUNULMAZ.</item>
    /// <item><b>Index:</b> <c>(TenantId, upper(Ettn))</c> unique — yarışta da aynı belge iki kez yazılamaz.</item>
    /// </list>
    /// RLS: tablo FORCE RLS'li ve racar_owner'da BYPASSRLS yok → sayım ve UPDATE boş <c>app.tenant_id</c> ile SIFIR satır
    /// görürdü. Repo deseni: NO FORCE / FORCE parantezi, aynı transaction.
    /// </summary>
    public partial class NormalizeGelenEFaturaEttn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"GelenEFaturalar\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                DO $$
                DECLARE dup integer;
                BEGIN
                    SELECT count(*) INTO dup FROM (
                        SELECT "TenantId", upper(btrim("Ettn"))
                        FROM "GelenEFaturalar"
                        GROUP BY 1, 2
                        HAVING count(*) > 1) d;
                    IF dup > 0 THEN
                        RAISE EXCEPTION 'GelenEFaturalar: % ETTN yalnız yazım farkıyla (büyük/küçük harf, boşluk) birden çok kayıtta. Migration durduruldu, veri silinmedi; kayıtları elle birleştirin (SELECT "TenantId", upper(btrim("Ettn")), count(*) FROM "GelenEFaturalar" GROUP BY 1,2 HAVING count(*) > 1).', dup;
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                UPDATE "GelenEFaturalar"
                SET "Ettn" = upper(btrim("Ettn"))
                WHERE btrim("Ettn") ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
                  AND "Ettn" <> upper(btrim("Ettn"));
                """);
            migrationBuilder.Sql("ALTER TABLE \"GelenEFaturalar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_GelenEFaturalar_TenantId_UpperEttn\" ON \"GelenEFaturalar\" (\"TenantId\", upper(\"Ettn\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Normalleştirilen ETTN'ler geri çevrilmez (eski küçük harf bilgisi tutulmadı; anlamı aynı belge).
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_GelenEFaturalar_TenantId_UpperEttn\";");
        }
    }
}
