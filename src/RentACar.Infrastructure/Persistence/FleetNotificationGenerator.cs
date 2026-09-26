using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Filo sinyalleri → uygulama-içi bildirim üretimi (FAZ 6.2; VadeBildirimUretici deseni — job'un
/// tenant döngüsünden çağrılır, PermissionGuard/ICurrentUser yüzeyi genişletilmez). İki tür:
/// (1) "Bakım-Km": periyodik bakım kalan-km ≤ 1000 — rapor sayfasıyla AYNI birleşimden
///     (OrtakSorgular.PeriyodikServisAsync). İdempotens anahtarının VadeTarihi'si SENTETİKTİR:
///     hedef-km Unix-SANİYE olarak kodlanır (1970 yakını bir tarih — gerçek vade değil, anahtar) →
///     bakım döngüsü (hedef) başına TEK bildirim; bakım yapılıp hedef ilerleyince yeni döngü yeni üretir.
/// (2) "Tut/Sat": sinyal ≥ 2 kural — FiloAnaliz ile AYNI ham (OrtakSorgular.TutSatHamAsync) + AYNI saf
///     hesap (TutSatHesap/GrupOrtalama; eşikler appsettings-override'lı). VadeTarihi = ay çıpası
///     (ayın 1'i UTC) → araç-ay başına en çok bir bildirim (sinyal sürdükçe aylık; kaybolursa üretilmez).
/// İdempotency: mevcut (Tur,VehicleId,VadeTarihi) seti + kısmi-unique index (çok-replika yarışında no-op).
/// </summary>
public static class FleetNotificationGenerator
{
    /// <summary>Üretilen yeni bildirim sayısı. db, tenantId'ye kapsanmış olmalı (GUC + filter).</summary>
    public static async Task<int> RunAsync(AppDbContext db, Guid tenantId, DateTimeOffset now,
        TutSatEsikleri threshold, CancellationToken ct = default)
    {
        // now → tut/sat pencere parametreleri + OlusturmaTarihi (timestamptz). Npgsql yalnız Offset=0
        // yazar; girişte UTC'ye normalize (ay çıpası da böylece UTC ayından hesaplanır — eski davranış).
        now = now.ToUniversalTime();

        var existing = (await db.Bildirimler.AsNoTracking()
                .Select(x => new { x.Tur, x.VehicleId, x.VadeTarihi }).ToListAsync(ct))
            .Select(x => (x.Tur, x.VehicleId, x.VadeTarihi)).ToHashSet();

        var newItem = 0;
        void Add(string type, Guid vehicleId, DateTimeOffset key, string message)
        {
            if (!existing.Add((type, vehicleId, key))) return; // parti-içi + mevcut çift-koruma
            db.Bildirimler.Add(new Bildirim
            {
                TenantId = tenantId, // interceptor'sız job yolu → açık damga
                Tur = type, VehicleId = vehicleId, VadeTarihi = key,
                Mesaj = message.Length <= 256 ? message : message[..256],
                Okundu = false, OlusturmaTarihi = now
            });
            newItem++;
        }

        // (1) Bakım-Km — kalan ≤ 1000 (geçmişse "AŞILDI").
        foreach (var r in await SharedQueries.PeriodicServiceAsync(db, ct))
        {
            if (r.KalanKm is not int remaining || remaining > 1000 || r.SonrakiBakimKm is not int target) continue;
            var key = DateTimeOffset.FromUnixTimeSeconds(target); // sentetik anahtar — özet yukarıda
            Add("Bakım-Km", r.VehicleId, key, remaining < 0
                ? $"{r.Plaka}: periyodik bakım km'si AŞILDI (hedef {target:N0}, güncel {r.GuncelKm:N0})."
                : $"{r.Plaka}: periyodik bakım yaklaşıyor — kalan {remaining:N0} km (hedef {target:N0}).");
        }

        // (2) Tut/Sat — ≥2 kural (filo panosuyla aynı ham + aynı hesap).
        var bundle = await SharedQueries.HoldSellRawAsync(db, now, ct);
        var rawById = bundle.Ham.ToDictionary(h => h.VehicleId);
        var groupAvg = GroupAverage.Calculate(bundle.Araclar, a => a.Grup,
            a => a.IkinciElDeger is > 0m ? rawById[a.Id].Gider12 / a.IkinciElDeger.Value : null);
        var monthAnchor = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var a in bundle.Araclar)
        {
            var s = HoldSellCalculation.Calculate(rawById[a.Id], a.IkinciElDeger, GroupAverage.Value(groupAvg, a.Grup), threshold);
            if (s.Sinyal < 2) continue;
            Add("Tut/Sat", a.Id, monthAnchor, $"{a.Plaka}: tut/sat sinyali ({s.Sinyal}/3) — {s.Gerekceler[0]}");
        }

        if (newItem == 0) return 0;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Çok-replikalı yarış: başka replika aynı anahtarı yazmış → idempotent no-op (Vade emsali).
            db.ChangeTracker.Clear();
            return 0;
        }
        return newItem;
    }
}
