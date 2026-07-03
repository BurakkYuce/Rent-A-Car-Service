using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Şube-FK geriye-dönük doldurma (roadmap F1 tamamlama): mevcut satırların serbest-metin şubesini
/// (Sube/AtanmisSube) tenant Branch master'ından çözüp FK'yi (SubeId/AtanmisSubeId) doldurur.
/// BranchFkInterceptor yeni/güncellenen satırları zaten çözer; bu tip yalnız hiç dokunulmayan ESKİ
/// satırları düzeltir. İDEMPOTENT (FK dolunca no-op). Başlangıçta (DbInitializer, owner) koşar.
/// BYPASSRLS varsayımı YOK: tenant listesi üzerinden GUC set + AÇIK TenantId (Users FORCE-RLS değil,
/// owner bypass eder → yalnız GUC yetmez). Eşleşme BranchRepository.FindByAdAsync ile birebir
/// (lower(btrim)=lower(btrim), aynı adda Kod sırası). Expense ATLANIR (immutable mali kayıt; yeni
/// giderler interceptor ile insert'te dolar).
/// </summary>
public static class BranchBackfill
{
    // (tablo, sube-metin kolonu, FK kolonu) — 6 mutable branch-scoped tablo.
    private static readonly (string Table, string SubeCol, string FkCol)[] Targets =
    [
        ("Vehicles", "Sube", "SubeId"),
        ("Locations", "Sube", "SubeId"),
        ("Personeller", "Sube", "SubeId"),
        ("TarifeMatris", "Sube", "SubeId"),
        ("KiralamaKurallari", "Sube", "SubeId"),
        ("Users", "AtanmisSube", "AtanmisSubeId"),
    ];

    /// <summary>FK'si doldurulan satır sayısını döndürür.</summary>
    public static async Task<int> RunAsync(AppDbContext db, ILogger? log = null, CancellationToken ct = default)
    {
        var tenantIds = await db.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync(ct);
        if (tenantIds.Count == 0) return 0;

        await db.Database.OpenConnectionAsync(ct); // GUC bağlantı ömrünce açık kalmalı
        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.tenant_id', {tenantId.ToString()}, false)", ct);

            var t = tenantId.ToString(); // Tenants'tan gelen güvenilir Guid → literal (injection yok)
            foreach (var (table, subeCol, fkCol) in Targets)
            {
                // Açık TenantId scope: FORCE-RLS olmayan Users'ta owner bypass'ına karşı korur; Branches
                // FORCE-RLS'te GUC ile zaten kapsanır ama açık koşul defense-in-depth.
                var sql = $"""
                    UPDATE "{table}" x SET "{fkCol}" = (
                        SELECT b."Id" FROM "Branches" b
                        WHERE b."TenantId" = '{t}' AND lower(btrim(b."Ad")) = lower(btrim(x."{subeCol}"))
                        ORDER BY b."Kod" LIMIT 1)
                    WHERE x."TenantId" = '{t}' AND x."{fkCol}" IS NULL AND x."{subeCol}" IS NOT NULL
                        AND btrim(x."{subeCol}") <> ''
                        AND EXISTS (SELECT 1 FROM "Branches" b2 WHERE b2."TenantId" = '{t}'
                            AND lower(btrim(b2."Ad")) = lower(btrim(x."{subeCol}")));
                    """;
                total += await db.Database.ExecuteSqlRawAsync(sql, ct);
            }
        }
        if (total > 0) log?.LogInformation("Şube-FK backfill: {Count} satır dolduruldu (F1).", total);
        return total;
    }
}
