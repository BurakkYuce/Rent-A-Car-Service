using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YakitOlcegiOnIki : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- Karar (3), 2026-09-25: TEK iç yakıt ölçeği 0–12 (Domain.Common.YakitOlcegi) ----
            // Şema DEĞİŞMEZ; yalnız VERİ çevrilir. Hangi yol hangi ölçekte YAZMIŞTI (ampirik, kod taraması):
            //
            //  * Rentals.CikisYakit/DonusYakit — KARIŞIK. Blazor kira formu (max=12) ve /api/ui (0–12 çiti)
            //    on ikide bir yazıyordu; harici JWT API (/api/v1/rentals deliver/return) YÜZDE geçiriyordu
            //    (servis çiti 0–100). 0–12 aralığındaki bir değerin hangi yoldan geldiği bilinemez → iç
            //    ölçekte sayılır (formlar birincil yol). >12 olan değer yalnız yüzde olabilir → çevrilir.
            //    <0 (F4.1 L6 çitinden önce) → 0.
            //  * Baflar.CikisYakit/DonusYakit — TAMAMI YÜZDE. Blazor BAF formu "Yakıt %" (max=100), /api/ui
            //    BAF ucu 0–100 çiti, SPA formu max(100); 0–12 yazan hiçbir yol yoktu → HER dolu değer çevrilir.
            //  * ServiceRecords — servis katmanı zaten 0–12 zorluyordu; dokunulmaz.
            //
            // Çeviri: round(v × 12 / 100) — tamsayı aritmetiğiyle (v*12+50)/100; C# YakitOlcegi.YuzdedenOnIkiye
            // ile BİREBİR (tamsayı girdide x.5 oluşmaz → yuvarlama yönü belirsizliği yok). Sonuç 0–12'ye kıstırılır.
            //
            // Kapanmış kiraların EksikYakit/YakitBedeli kolonlarına DOKUNULMAZ: onlar o günkü girdilerle
            // hesaplanıp faturalanmış/deftere yazılmış tarihsel tutarlardır; yeniden hesaplamak para geçmişini
            // değiştirirdi. Yalnız seviye göstergeleri çevrilir.
            //
            // TUZAK (YakitNullable/PiiBackfill deseni): iki tablo FORCE ROW LEVEL SECURITY; racar_owner'ın
            // BYPASSRLS'i yok → düz UPDATE SESSİZCE 0 satır etkiler. Tenant döngüsü + işlem-yerel set_config.
            // Down() yok: yüzdeden on ikide bire çeviri kayıplıdır (80 → 10 → 83), geri dönüş tanımsız.
            migrationBuilder.Sql("""
                DO $$
                DECLARE t uuid;
                BEGIN
                    FOR t IN SELECT "Id" FROM "Tenants" LOOP
                        PERFORM set_config('app.tenant_id', t::text, true);

                        UPDATE "Rentals"
                           SET "CikisYakit" = LEAST(12, (GREATEST("CikisYakit", 0) * 12 + 50) / 100)
                         WHERE "CikisYakit" > 12;
                        UPDATE "Rentals" SET "CikisYakit" = 0 WHERE "CikisYakit" < 0;
                        UPDATE "Rentals"
                           SET "DonusYakit" = LEAST(12, (GREATEST("DonusYakit", 0) * 12 + 50) / 100)
                         WHERE "DonusYakit" > 12;
                        UPDATE "Rentals" SET "DonusYakit" = 0 WHERE "DonusYakit" < 0;

                        UPDATE "Baflar"
                           SET "CikisYakit" = LEAST(12, (GREATEST("CikisYakit", 0) * 12 + 50) / 100)
                         WHERE "CikisYakit" IS NOT NULL;
                        UPDATE "Baflar"
                           SET "DonusYakit" = LEAST(12, (GREATEST("DonusYakit", 0) * 12 + 50) / 100)
                         WHERE "DonusYakit" IS NOT NULL;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Bilinçli boş: çeviri kayıplı, eski yüzde değeri geri üretilemez (bkz. Up).
        }
    }
}
