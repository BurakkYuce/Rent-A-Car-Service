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
/// </summary>
public static class PiiBackfill
{
    /// <summary>Şifrelenen cari sayısını döndürür (gözlemlenebilirlik/log için).</summary>
    public static async Task<int> RunAsync(
        AppDbContext db, ISecretProtector secrets, IPiiHasher pii,
        ILogger? log = null, CancellationToken ct = default)
    {
        var tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        if (tenantIds.Count == 0) return 0;

        // GUC bağlantıya yazılır → bağlantı context ömrünce açık kalmalı.
        await db.Database.OpenConnectionAsync(ct);

        // Denetim izi değişmezlik trigger'ı, KVKK maskeleme için KONTROLLÜ ve geçici olarak
        // kapatılır (yalnız owner; tek seferlik scrub — hukuki silme yükümlülüğü değişmezlikten önce gelir).
        await db.Database.ExecuteSqlRawAsync("""
            DO $$ BEGIN
              IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'auditlogs_immutable') THEN
                EXECUTE 'ALTER TABLE "AuditLogs" DISABLE TRIGGER auditlogs_immutable';
              END IF;
            END $$;
            """, ct);
        var total = 0;
        try
        {
            foreach (var tenantId in tenantIds)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.tenant_id', {tenantId.ToString()}, false)", ct);

                total += await BackfillTenantAsync(db, secrets, pii, log, ct);

                // Tarihsel audit satırlarındaki düz PII değerlerini maskele (RLS: GUC bu tenant'ı açar).
                // jsonb-YERLİSİ jsonb_set kullanılır — metin/regex yaklaşımı kaçışlı tırnak (\") içeren
                // legacy değerde geçersiz JSON üretip açılışı çökertiyordu (adversarial re-verify Medium).
                // Anahtar birebir eşleşir ("TcKimlikEnc" ≠ "TcKimlik"); idempotent ('***' atlanır).
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
            await db.Database.ExecuteSqlRawAsync("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'auditlogs_immutable') THEN
                    EXECUTE 'ALTER TABLE "AuditLogs" ENABLE TRIGGER auditlogs_immutable';
                  END IF;
                END $$;
                """, ct);
        }
        return total;
    }

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
}
