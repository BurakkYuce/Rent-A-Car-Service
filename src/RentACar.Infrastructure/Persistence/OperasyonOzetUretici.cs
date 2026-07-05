using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence;

/// <summary>Günlük operasyon özeti sonucu (İstanbul günü). Tahsilat = TL-baz (çok-döviz Σ Amount×Rate).</summary>
public readonly record struct OperasyonOzet(
    int Cikis, int Donus, int AcikRez, decimal Tahsilat, int Kiradaki, int Bosta, int Serviste);

/// <summary>
/// Günlük operasyon özeti üretimi (raw query — job modeli, VadeBildirimUretici deseni; db tenant-kapsamlı).
/// Gün DIŞARIDAN (İstanbul DateOnly) verilir → idempotency ile AYNI kaynak. Metrikler Home.razor/GetFleetUtilization/
/// ReportRepository ile tutarlı. Yalnız OKUMA.
/// </summary>
public static class OperasyonOzetUretici
{
    public static async Task<OperasyonOzet> BuildAsync(AppDbContext db, DateOnly gun, TimeZoneInfo tz, CancellationToken ct = default)
    {
        // İstanbul günü → UTC aralığı [gun 00:00 İst, +1 gün)
        var localBas = new DateTime(gun.Year, gun.Month, gun.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var bas = new DateTimeOffset(localBas, tz.GetUtcOffset(localBas)).ToUniversalTime();
        var bit = new DateTimeOffset(localBas.AddDays(1), tz.GetUtcOffset(localBas.AddDays(1))).ToUniversalTime();

        // Beklenen çıkış: bugün başlayan aktif rezervasyonlar
        var cikis = await db.Reservations.AsNoTracking().CountAsync(
            r => (r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli)
                 && r.BasTar >= bas && r.BasTar < bit, ct);
        // Beklenen dönüş: bugün bitmesi gereken aktif kiralar
        var donus = await db.Rentals.AsNoTracking().CountAsync(
            r => r.Durum == RentalStatus.Kirada && r.BitTar >= bas && r.BitTar < bit, ct);
        // Açık rezervasyon (toplam pipeline)
        var acikRez = await db.Reservations.AsNoTracking().CountAsync(
            r => r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli, ct);

        // Tahsilat TL-BAZ (çok-döviz): Σ Amount×Rate; ters-kayıt hariç (ReportRepository:321 birebir).
        var tahsilatlar = await db.CashTransactions.AsNoTracking()
            .Where(c => c.Tip == CashTransactionType.Tahsilat && !c.TersKayitMi && c.Tarih >= bas && c.Tarih < bit)
            .Select(c => new { c.Amount.Amount, c.Amount.Rate }).ToListAsync(ct);
        var tahsilat = tahsilatlar.Sum(t => t.Amount * t.Rate);

        // Filo = Vehicle.Durum SAYIMI (GetFleetUtilization birebir)
        var durumlar = await db.Vehicles.AsNoTracking().Select(v => v.Durum).ToListAsync(ct);
        int Say(VehicleStatus s) => durumlar.Count(x => x == s);

        return new OperasyonOzet(cikis, donus, acikRez, tahsilat,
            Say(VehicleStatus.Kirada), Say(VehicleStatus.Musait), Say(VehicleStatus.Serviste));
    }
}
