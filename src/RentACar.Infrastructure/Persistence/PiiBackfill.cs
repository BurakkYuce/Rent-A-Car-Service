using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// KVKK/F2 geriye-dönük PII şifreleme: eski düz-metin TC/ehliyet/pasaport değerlerini şifreler
/// (+tenant-tuzlu TC blind-index), düz metni NULL'lar ve TARİHSEL denetim izlerindeki düz PII
/// değerlerini maskeler (adversarial HIGH-1). Başlangıçta (DbInitializer, owner bağlantısı)
/// koşar; İDEMPOTENT.
/// BYPASSRLS'e GÜVENMEZ: owner rolü kurulumlara göre NOBYPASSRLS olabilir ve FORCE RLS owner'ı
/// da kapsar → tek geçiş SESSİZCE 0 satır işlerdi. Tenant listesi (platform tablosu, RLS'siz)
/// üzerinden dolaşıp her tenant için app.tenant_id GUC'u set ederek çalışır.
///
/// HER BOOT'TA ÇALIŞIR ama STEADY-STATE'te UCUZDUR: göç tamamlandıktan sonra bu tip yalnız
/// tenant başına iki salt-okuma tespit sorgusu koşar (legacy cari? + maskesiz audit?) ve
/// erken çıkar — immutability trigger'ına DOKUNMAZ, hiçbir yazma yapmaz. Trigger'ı geçici
/// kapatan + audit satırı yeniden yazan pahalı/hassas bölüm YALNIZ gerçekten maskelenecek
/// tarihsel iz bulunan boot'ta çalışır (opsiyonel iyileştirme: audit tablosu çok büyürse
/// bir "göç tamam" işareti tespit sorgularını da atlatır).
/// </summary>
public static class PiiBackfill
{
    private const string DisableTriggerSql = """
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'auditlogs_immutable') THEN
            EXECUTE 'ALTER TABLE "AuditLogs" DISABLE TRIGGER auditlogs_immutable';
          END IF;
        END $$;
        """;

    private const string EnableTriggerSql = """
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'auditlogs_immutable') THEN
            EXECUTE 'ALTER TABLE "AuditLogs" ENABLE TRIGGER auditlogs_immutable';
          END IF;
        END $$;
        """;

    /// <summary>Şifrelenen cari sayısını döndürür (gözlemlenebilirlik/log için).</summary>
    public static async Task<int> RunAsync(
        AppDbContext db, ISecretProtector secrets, IPiiHasher pii,
        ILogger? log = null, CancellationToken ct = default)
    {
        var tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        if (tenantIds.Count == 0) return 0;

        // GUC bağlantıya yazılır → bağlantı context ömrünce açık kalmalı.
        await db.Database.OpenConnectionAsync(ct);

        // FAZ 1 — cari düz-metnini şifrele + hangi tenant'ların TARİHSEL audit izi hâlâ maskesiz
        // PII taşıdığını TESPİT et. Yazmalar (cari UPDATE + interceptor'ın YENİ maskeli audit
        // INSERT'leri) immutability trigger'ından ETKİLENMEZ (trigger yalnız UPDATE/DELETE'i
        // engeller) → bu faz trigger'a dokunmaz. Legacy yoksa iki ucuz SELECT ile geçilir.
        var total = 0;
        var scrubTenants = new List<Guid>();
        foreach (var tenantId in tenantIds)
        {
            await SetTenantAsync(db, tenantId, ct);
            total += await BackfillTenantAsync(db, secrets, pii, log, ct);
            if (await AuditHasPlaintextAsync(db, ct))
                scrubTenants.Add(tenantId);
        }

        // FAZ 2 — SADECE maskelenecek tarihsel iz varsa immutability trigger'ını geçici kapat
        // ve o tenant'ların audit izlerini maskele. Steady-state'te scrubTenants BOŞ → burası
        // hiç çalışmaz (her boot'ta trigger toggle + yazma YOK).
        if (scrubTenants.Count > 0)
            await ScrubHistoricalAuditAsync(db, scrubTenants, log, ct);

        return total;
    }

    private static Task SetTenantAsync(AppDbContext db, Guid tenantId, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenantId.ToString()}, false)", ct);

    private static async Task<int> BackfillTenantAsync(
        AppDbContext db, ISecretProtector secrets, IPiiHasher pii, ILogger? log, CancellationToken ct)
    {
        // IgnoreQueryFilters: context'in tenant'ı yok (platform context) — kapsamı RLS + GUC verir.
        var legacy = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.TcKimlik != null || c.EhliyetNo != null || c.PasaportNo != null)
            .ToListAsync(ct);
        if (legacy.Count == 0) return 0;

        // Hash çakışması startup'ı DÜŞÜRMEMELİ (adversarial Low: trim-eşdeğeri eski kayıtlar):
        // dolu hash'ler + bu partideki hash'ler izlenir; çakışan satır HASH'siz ama ŞİFRELİ kalır.
        var taken = new HashSet<string>(await db.Customers.IgnoreQueryFilters()
            .Where(c => c.TcKimlikHash != null).Select(c => c.TcKimlikHash!).ToListAsync(ct));

        foreach (var c in legacy)
        {
            if (c.TcKimlik is not null)
            {
                if (c.TcKimlikHash is null)
                {
                    var hash = pii.Hash(c.TenantId, c.TcKimlik);
                    if (hash is not null && taken.Add(hash))
                        c.TcKimlikHash = hash;
                    else
                        log?.LogWarning(
                            "PII backfill: cari {Id} TC hash çakışması — şifrelendi ama blind-index'siz bırakıldı (elle inceleyin).",
                            c.Id);
                }
                c.TcKimlikEnc ??= secrets.Protect(c.TcKimlik);
                c.TcKimlik = null;
            }
            if (c.EhliyetNo is not null)
            {
                c.EhliyetNoEnc ??= secrets.Protect(c.EhliyetNo);
                c.EhliyetNo = null;
            }
            if (c.PasaportNo is not null)
            {
                c.PasaportNoEnc ??= secrets.Protect(c.PasaportNo);
                c.PasaportNo = null;
            }
        }

        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear(); // sonraki tenant'a temiz izleme
        return legacy.Count;
    }

    /// <summary>
    /// Geçerli GUC-tenant'ının denetim izinde HÂLÂ maskesiz düz PII var mı? (salt-okuma, EXISTS
    /// kısa-devre). Tarihsel (F2-öncesi) satırlar yalnız düz-metin anahtarları taşır — *Enc yok —
    /// bu yüzden scrub ile aynı üç anahtar kontrol edilir. JSON null / anahtar-yok → maskesiz DEĞİL.
    /// </summary>
    private static async Task<bool> AuditHasPlaintextAsync(AppDbContext db, CancellationToken ct)
    {
        const string sql = """
            SELECT EXISTS (
              SELECT 1 FROM "AuditLogs"
              WHERE "EntityName" = 'Customers'
                AND (
                     ("OldValues"->>'TcKimlik'   IS NOT NULL AND "OldValues"->>'TcKimlik'   <> '***')
                  OR ("OldValues"->>'EhliyetNo'  IS NOT NULL AND "OldValues"->>'EhliyetNo'  <> '***')
                  OR ("OldValues"->>'PasaportNo' IS NOT NULL AND "OldValues"->>'PasaportNo' <> '***')
                  OR ("NewValues"->>'TcKimlik'   IS NOT NULL AND "NewValues"->>'TcKimlik'   <> '***')
                  OR ("NewValues"->>'EhliyetNo'  IS NOT NULL AND "NewValues"->>'EhliyetNo'  <> '***')
                  OR ("NewValues"->>'PasaportNo' IS NOT NULL AND "NewValues"->>'PasaportNo' <> '***')
                )
            ) AS "Value"
            """;
        return await db.Database.SqlQueryRaw<bool>(sql).SingleAsync(ct);
    }

    /// <summary>
    /// Tarihsel audit satırlarındaki düz PII değerlerini maskeler. YALNIZ maskelenecek iz bulunan
    /// tenant'lar için çağrılır → immutability trigger'ı yalnız gerçek yazma olacaksa (her boot'ta
    /// değil) kapatılır. jsonb-YERLİSİ jsonb_set: metin/regex yaklaşımı kaçışlı tırnak (\") içeren
    /// legacy değerde geçersiz JSON üretip açılışı çökertiyordu (adversarial re-verify Medium).
    /// </summary>
    private static async Task ScrubHistoricalAuditAsync(
        AppDbContext db, IReadOnlyList<Guid> tenantIds, ILogger? log, CancellationToken ct)
    {
        log?.LogInformation(
            "PII backfill: {Count} tenant'ın tarihsel denetim izinde maskesiz PII bulundu — maskeleniyor (KVKK/F2).",
            tenantIds.Count);

        await db.Database.ExecuteSqlRawAsync(DisableTriggerSql, ct);
        try
        {
            foreach (var tenantId in tenantIds)
            {
                await SetTenantAsync(db, tenantId, ct);
                foreach (var col in new[] { "OldValues", "NewValues" })
                foreach (var key in new[] { "TcKimlik", "EhliyetNo", "PasaportNo" })
                {
                    // col/key SABİT dizilerden gelir (kullanıcı girdisi değil → injection imkânsız).
                    // jsonb_set yol formatı; ExecuteSqlRaw String.Format uyguladığından {} ÇİFTLENİR.
                    var path = "{{" + key + "}}";
                    var sql = $"""
                        UPDATE "AuditLogs"
                        SET "{col}" = jsonb_set("{col}", '{path}', '"***"')
                        WHERE "EntityName" = 'Customers'
                          AND "{col}" IS NOT NULL
                          AND jsonb_exists("{col}", '{key}')
                          AND "{col}"->>'{key}' IS NOT NULL
                          AND "{col}"->>'{key}' <> '***';
                        """;
                    await db.Database.ExecuteSqlRawAsync(sql, ct);
                }
            }
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync(EnableTriggerSql, ct);
        }
    }
}
