using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Regulation;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Vade → uygulama-içi bildirim üretimi (scheduler işi). Verilen tenant için (db query filter + RLS
/// GUC ile kapsanmış) yaklaşan/geçmiş sigorta/MTV/muayene vadelerini tarar, HER kaynak için TEK
/// bildirim yazar (idempotent: mevcut (Tur,VehicleId,VadeTarihi) atlanır). Kaynak birleşimi
/// RegulationRepository.GetVadeSourcesAsync ile birebir. Job doğrudan-context yolunda çağırır
/// (interceptor'sız) → TenantId açıkça damgalanır.
/// </summary>
public static class DueNotificationGenerator
{
    /// <summary>Üretilen yeni bildirim sayısını döndürür. db, tenantId'ye kapsanmış olmalı (GUC + filter).</summary>
    public static async Task<int> RunAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        // now → OlusturmaTarihi (timestamptz). Npgsql yalnız Offset=0 yazar; çağıran yerel ofsetli
        // bir an verse de (aynı an) kayıt düşmesin diye girişte UTC'ye normalize edilir.
        now = now.ToUniversalTime();

        // Vade kaynak birleşimi TEK doğruluk kaynağından (denetim O12a) — vade panosu ile birebir aynı liste.
        var sources = await SharedQueries.DueSourcesAsync(db, ct);

        // Yaklaşan/geçmiş (bucket != Ileri) → bildirim adayı.
        var candidates = sources
            .Select(s => (s.VehicleId, s.Tur, s.Bitis, C: DueCalculation.Classify(now, s.Bitis)))
            .Where(x => x.C.Bucket != DueBucket.Ileri)
            .ToList();
        if (candidates.Count == 0) return 0;

        // İdempotency: mevcut bildirimlerin (Tur,VehicleId,VadeTarihi) anahtarları.
        var existing = (await db.Bildirimler.AsNoTracking()
                .Select(x => new { x.Tur, x.VehicleId, x.VadeTarihi }).ToListAsync(ct))
            .Select(x => (x.Tur, x.VehicleId, x.VadeTarihi)).ToHashSet();

        var newItem = 0;
        foreach (var a in candidates)
        {
            if (!existing.Add((a.Tur, a.VehicleId, a.Bitis))) continue; // bu parti içi + mevcut çift-koruma
            db.Bildirimler.Add(new Bildirim
            {
                TenantId = tenantId, // interceptor'sız job yolu → açık damga
                Tur = a.Tur, VehicleId = a.VehicleId, VadeTarihi = a.Bitis,
                Mesaj = Message(a.Tur, a.C.KalanGun, a.Bitis), Okundu = false, OlusturmaTarihi = now
            });
            newItem++;
        }
        if (newItem == 0) return 0;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Çok-replikalı yarış (adversarial F2): başka replika aynı kaynağı yazmış → kısmi-unique
            // index reddetti. Atomik batch tümden geri alındı; bildirimler diğer replikada zaten var
            // → idempotent no-op. (Tek-instance job'da bu yol tetiklenmez; mevcut+HashSet dedup yeter.)
            db.ChangeTracker.Clear();
            return 0;
        }
        return newItem;
    }

    private static string Message(string type, int remainingDays, DateTimeOffset end) =>
        remainingDays < 0
            ? $"{type} vadesi GEÇTİ ({end.LocalDateTime:dd.MM.yyyy}, {-remainingDays} gün önce)."
            : $"{type} vadesi yaklaşıyor ({end.LocalDateTime:dd.MM.yyyy}, {remainingDays} gün kaldı).";
}
