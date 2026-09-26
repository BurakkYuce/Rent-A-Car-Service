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
public static class OperationSummaryGenerator
{
    public static async Task<OperasyonOzet> BuildAsync(AppDbContext db, DateOnly day, TimeZoneInfo tz, CancellationToken ct = default)
    {
        // İstanbul günü → UTC aralığı [gun 00:00 İst, +1 gün)
        var localStart = new DateTime(day.Year, day.Month, day.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(localStart, tz.GetUtcOffset(localStart)).ToUniversalTime();
        var bit = new DateTimeOffset(localStart.AddDays(1), tz.GetUtcOffset(localStart.AddDays(1))).ToUniversalTime();

        // Beklenen çıkış: bugün başlayan aktif rezervasyonlar
        var pickup = await db.Reservations.AsNoTracking().CountAsync(
            r => (r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli)
                 && r.BasTar >= start && r.BasTar < bit, ct);
        // Beklenen dönüş: bugün bitmesi gereken aktif kiralar
        var returnInfo = await db.Rentals.AsNoTracking().CountAsync(
            r => r.Durum == RentalStatus.Kirada && r.BitTar >= start && r.BitTar < bit, ct);
        // Açık rezervasyon (toplam pipeline)
        var openReservation = await db.Reservations.AsNoTracking().CountAsync(
            r => r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli, ct);

        // Tahsilat + filo: TEK doğruluk kaynağı (denetim O12b) — dashboard/rapor ile AYNI tanım, ayrışamaz.
        var (_, collection) = await SharedQueries.CollectionTryAsync(db, start, bit, ct);
        var statuses = await SharedQueries.VehicleStatusesAsync(db, ct);
        int Say(VehicleStatus s) => statuses.Count(x => x == s);

        return new OperasyonOzet(pickup, returnInfo, openReservation, collection,
            Say(VehicleStatus.Kirada), Say(VehicleStatus.Musait), Say(VehicleStatus.Serviste));
    }
}
