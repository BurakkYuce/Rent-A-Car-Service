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
public static class VadeBildirimUretici
{
    /// <summary>Üretilen yeni bildirim sayısını döndürür. db, tenantId'ye kapsanmış olmalı (GUC + filter).</summary>
    public static async Task<int> RunAsync(AppDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        var insurance = await db.InsurancePolicies.AsNoTracking()
            .Select(x => new { x.VehicleId, Tur = x.Tip == InsuranceType.Kasko ? "Kasko" : "Trafik", Bitis = x.Bitis })
            .ToListAsync(ct);
        var mtv = await db.MtvRecords.AsNoTracking().Where(x => !x.Odendi)
            .Select(x => new { x.VehicleId, Tur = "MTV", Bitis = x.Vade }).ToListAsync(ct);
        var inspection = await db.InspectionRecords.AsNoTracking()
            .Select(x => new { x.VehicleId, Tur = "Muayene", Bitis = x.Bitis }).ToListAsync(ct);

        // Yaklaşan/geçmiş (bucket != Ileri) → bildirim adayı.
        var adaylar = insurance.Concat(mtv).Concat(inspection)
            .Select(s => (s.VehicleId, s.Tur, s.Bitis, C: VadeHesap.Classify(now, s.Bitis)))
            .Where(x => x.C.Bucket != VadeBucket.Ileri)
            .ToList();
        if (adaylar.Count == 0) return 0;

        // İdempotency: mevcut bildirimlerin (Tur,VehicleId,VadeTarihi) anahtarları.
        var mevcut = (await db.Bildirimler.AsNoTracking()
                .Select(x => new { x.Tur, x.VehicleId, x.VadeTarihi }).ToListAsync(ct))
            .Select(x => (x.Tur, x.VehicleId, x.VadeTarihi)).ToHashSet();

        var yeni = 0;
        foreach (var a in adaylar)
        {
            if (!mevcut.Add((a.Tur, a.VehicleId, a.Bitis))) continue; // bu parti içi + mevcut çift-koruma
            db.Bildirimler.Add(new Bildirim
            {
                TenantId = tenantId, // interceptor'sız job yolu → açık damga
                Tur = a.Tur, VehicleId = a.VehicleId, VadeTarihi = a.Bitis,
                Mesaj = Mesaj(a.Tur, a.C.KalanGun, a.Bitis), Okundu = false, OlusturmaTarihi = now
            });
            yeni++;
        }
        if (yeni == 0) return 0;
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
        return yeni;
    }

    private static string Mesaj(string tur, int kalanGun, DateTimeOffset bitis) =>
        kalanGun < 0
            ? $"{tur} vadesi GEÇTİ ({bitis.LocalDateTime:dd.MM.yyyy}, {-kalanGun} gün önce)."
            : $"{tur} vadesi yaklaşıyor ({bitis.LocalDateTime:dd.MM.yyyy}, {kalanGun} gün kaldı).";
}
