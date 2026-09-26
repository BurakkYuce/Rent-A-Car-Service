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
public static class FiloBildirimUretici
{
    /// <summary>Üretilen yeni bildirim sayısı. db, tenantId'ye kapsanmış olmalı (GUC + filter).</summary>
    public static async Task<int> RunAsync(AppDbContext db, Guid tenantId, DateTimeOffset now,
        TutSatEsikleri esik, CancellationToken ct = default)
    {
        // now → tut/sat pencere parametreleri + OlusturmaTarihi (timestamptz). Npgsql yalnız Offset=0
        // yazar; girişte UTC'ye normalize (ay çıpası da böylece UTC ayından hesaplanır — eski davranış).
        now = now.ToUniversalTime();

        var mevcut = (await db.Bildirimler.AsNoTracking()
                .Select(x => new { x.Tur, x.VehicleId, x.VadeTarihi }).ToListAsync(ct))
            .Select(x => (x.Tur, x.VehicleId, x.VadeTarihi)).ToHashSet();

        var yeni = 0;
        void Ekle(string tur, Guid vehicleId, DateTimeOffset anahtar, string mesaj)
        {
            if (!mevcut.Add((tur, vehicleId, anahtar))) return; // parti-içi + mevcut çift-koruma
            db.Bildirimler.Add(new Bildirim
            {
                TenantId = tenantId, // interceptor'sız job yolu → açık damga
                Tur = tur, VehicleId = vehicleId, VadeTarihi = anahtar,
                Mesaj = mesaj.Length <= 256 ? mesaj : mesaj[..256],
                Okundu = false, OlusturmaTarihi = now
            });
            yeni++;
        }

        // (1) Bakım-Km — kalan ≤ 1000 (geçmişse "AŞILDI").
        foreach (var r in await OrtakSorgular.PeriyodikServisAsync(db, ct))
        {
            if (r.KalanKm is not int kalan || kalan > 1000 || r.SonrakiBakimKm is not int hedef) continue;
            var anahtar = DateTimeOffset.FromUnixTimeSeconds(hedef); // sentetik anahtar — özet yukarıda
            Ekle("Bakım-Km", r.VehicleId, anahtar, kalan < 0
                ? $"{r.Plaka}: periyodik bakım km'si AŞILDI (hedef {hedef:N0}, güncel {r.GuncelKm:N0})."
                : $"{r.Plaka}: periyodik bakım yaklaşıyor — kalan {kalan:N0} km (hedef {hedef:N0}).");
        }

        // (2) Tut/Sat — ≥2 kural (filo panosuyla aynı ham + aynı hesap).
        var paket = await OrtakSorgular.TutSatHamAsync(db, now, ct);
        var hamById = paket.Ham.ToDictionary(h => h.VehicleId);
        var grupOrt = GroupAverage.Calculate(paket.Araclar, a => a.Grup,
            a => a.IkinciElDeger is > 0m ? hamById[a.Id].Gider12 / a.IkinciElDeger.Value : null);
        var ayCipasi = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var a in paket.Araclar)
        {
            var s = HoldSellCalculation.Calculate(hamById[a.Id], a.IkinciElDeger, GroupAverage.Value(grupOrt, a.Grup), esik);
            if (s.Sinyal < 2) continue;
            Ekle("Tut/Sat", a.Id, ayCipasi, $"{a.Plaka}: tut/sat sinyali ({s.Sinyal}/3) — {s.Gerekceler[0]}");
        }

        if (yeni == 0) return 0;
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
        return yeni;
    }
}
