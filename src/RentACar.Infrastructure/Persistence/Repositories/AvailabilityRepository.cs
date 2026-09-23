using Microsoft.EntityFrameworkCore;
using RentACar.Application.Availability;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Müsaitlik sorgusu. Kiralanabilir havuz (Müsait/Kirada) − çakışan aktif kira (Kirada) −
/// çakışan açık rezervasyon (Rezerv/Onayli). Çakışma: [from,to) ∩ [kayıt.Bas,kayıt.Bit) ≠ ∅,
/// yani Bas &lt; to AND from &lt; Bit. Tenant izolasyonu query filter + RLS ile otomatik.
/// </summary>
public sealed class AvailabilityRepository(IDbContextFactory<AppDbContext> factory) : IAvailabilityRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Vehicle>> GetAvailableAsync(
        DateTimeOffset from, DateTimeOffset to, string? grup, string? sube,
        RentACar.Application.Authorization.BranchScope.BranchFilter kapsam = default, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var pool = db.Vehicles.AsNoTracking()
            // Kiralanabilir havuz: Musait/Kirada. Kirada DAHİL — çünkü şu an çıkıştaki araç, döndükten
            // SONRAKİ (çakışmayan) tarihler için müsait; gerçek dışlamayı aşağıdaki tarih-çakışması yapar.
            // (Serviste/Pasif/Satildi havuz dışı.)
            .Where(v => v.Durum == VehicleStatus.Musait || v.Durum == VehicleStatus.Kirada);
        if (!string.IsNullOrWhiteSpace(grup)) pool = pool.Where(v => v.Grup == grup);
        if (!string.IsNullOrWhiteSpace(sube)) pool = pool.Where(v => v.Sube == sube);
        // C3 ŞABLON (BranchScope.InScope ile birebir): FK-eşit VEYA metin-eşit (Ordinal).
        if (!kapsam.Unrestricted)
        {
            var kid = kapsam.SubeId; var kad = kapsam.SubeAd;
            pool = pool.Where(v => (kid != null && v.SubeId == kid)
                                || ((kid == null || v.SubeId == null) && kad != null && v.Sube != null && v.Sube.Trim() == kad)); // C5
        }

        var busyByRental = db.Rentals.AsNoTracking()
            .Where(r => r.Durum == RentalStatus.Kirada && r.BasTar < to && from < r.BitTar)
            .Select(r => r.VehicleId);

        var busyByReservation = db.Reservations.AsNoTracking()
            .Where(r => (r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli)
                && r.BasTar < to && from < r.BitTar)
            .Select(r => r.VehicleId);

        return await pool
            .Where(v => !busyByRental.Contains(v.Id) && !busyByReservation.Contains(v.Id))
            .OrderBy(v => v.Plaka)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SonKullanimRow>> GetSonKullanimAsync(
        IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct = default)
    {
        if (vehicleIds.Count == 0) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;

        // Efektif dönüş = gerçek dönüş ?? planlı bitiş. İptal kiralar hariç; GELECEKTE biten kira
        // "son kullanım" değildir (araç hâlâ o kirada olabilir) → yalnız geçmiş.
        var ham = await db.Rentals.AsNoTracking()
            .Where(r => r.Durum != RentACar.Domain.Enums.RentalStatus.Iptal && vehicleIds.Contains(r.VehicleId))
            .Select(r => new { r.VehicleId, r.MusteriId, Bit = r.GercekDonusTar ?? r.BitTar })
            .Where(r => r.Bit <= now)
            .ToListAsync(ct);
        if (ham.Count == 0) return [];

        var son = ham.GroupBy(r => r.VehicleId)
            .Select(g => g.OrderByDescending(r => r.Bit).First())
            .ToList();

        // Müşteri adları TEK sorguda (araç başına sorgu atmak N+1 olurdu).
        var cariIdler = son.Select(x => x.MusteriId).Distinct().ToList();
        var cariler = await db.Customers.AsNoTracking()
            .Where(c => cariIdler.Contains(c.Id))
            .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad, c.AnonimAd })
            .ToListAsync(ct);
        var anonim = cariler.Where(c => c.AnonimAd).Select(c => c.Id).ToHashSet(); // F5.1 (KVKK bayrağı)
        var adlar = cariler.ToDictionary(
            c => c.Id,
            c => c.Tip == RentACar.Domain.Enums.CariType.Kurumsal
                ? c.Unvan
                : $"{c.Ad} {c.Soyad}".Trim());

        return son
            .Select(x => new SonKullanimRow(x.VehicleId, x.Bit, adlar.GetValueOrDefault(x.MusteriId), anonim.Contains(x.MusteriId)))
            .ToList();
    }
}
