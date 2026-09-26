using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.DolulukFiyat;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>IDolulukFiyatKuralRepository implementasyonu (FAZ 3.A7) — master CRUD deseni.</summary>
public sealed class DolulukFiyatKuralRepository(IDbContextFactory<AppDbContext> factory) : IOccupancyPriceRuleRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<DolulukFiyatKural>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DolulukFiyatKurallari.AsNoTracking()
            .OrderBy(c => c.Kod).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DolulukFiyatKural>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DolulukFiyatKurallari.AsNoTracking()
            .Where(c => c.Aktif).OrderBy(c => c.Kod).ToListAsync(ct);
    }

    public async Task<DolulukFiyatKural?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DolulukFiyatKurallari.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<bool> CodeExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DolulukFiyatKurallari.AsNoTracking()
            .AnyAsync(c => c.Kod == kod && (excludeId == null || c.Id != excludeId), ct);
    }

    public async Task CreateAsync(DolulukFiyatKural row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.DolulukFiyatKurallari.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { throw new ValidationException($"'{row.Kod}' kodlu doluluk kuralı zaten var."); }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<DolulukFiyatKural> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DolulukFiyatKurallari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        apply(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DolulukFiyatKurallari.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row is null) return false;
        db.DolulukFiyatKurallari.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // F11.1a — IVersionedRepository<DolulukFiyatKural> (generic RowVersion helper).
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => RowVersion.ReadAsync<DolulukFiyatKural>(_factory, id, ct);

    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => RowVersion.ReadAllAsync<DolulukFiyatKural>(_factory, ct);

    public Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<DolulukFiyatKural> apply, CancellationToken ct = default)
        => RowVersion.UpdateAsync(_factory, id, expectedVersion, apply, r => $"'{r.Kod}' kodlu doluluk kuralı zaten var.", ct);
}

/// <summary>
/// Grup doluluk sağlayıcısı (FAZ 3.A7). Araç-gün matematiği GetDolulukAsync desenine paralel ama
/// pencere BİTİŞ-HARİÇ gün farkıdır (ComputeGun hizası: 5 günlük kira = araç başına 5 araç-gün;
/// aynı-takvim-günü penceresi 1 gün sayılır — adversarial B5). PAYDA yalnız KİRALANABİLİR filo:
/// Satildi/Pasif araçlar dışlanır (adversarial B1 — satılan araçlar paydayı kalıcı şişirip surge'ü
/// öldürüyordu; Serviste geçici olduğundan paydada kalır). PAY: kiralar (İptal hariç; Tamamlandi'da
/// EFEKTİF bitiş = min(BitTar, GercekDonusTar) — erken dönüş hayalet doluluk üretmez, adversarial B2)
/// + Rezerv/Onaylı REZERVASYONLAR (ileri tarihli talebin ana kaynağı — adversarial B3; KirayaCevrildi
/// zaten kira olarak sayılır; aynı araçta rez+kira örtüşmesi teorik çifte sayım — yüzde şişebilir ama
/// kural seçimi bozulmaz [>%100 probe'la doğrulandı] ve çakışma guard'ları bu durumu pratikte engeller).
/// Grupta araç yoksa NULL (surge yok). ŞUBE BOYUTU YOK: doluluk tenant genelinde grup bazındadır
/// (çok şubeli tenant'ta şube-lokal yoğunluk sinyali sulanır — bilinçli sadelik, master UI'da not).
/// TOCTOU bilinçli kabul: doluluk create ANINDA okunur ve fiyat sözleşmede kilitlenir.
/// </summary>
public sealed class OccupancyProvider(IDbContextFactory<AppDbContext> factory) : IOccupancyProvider
{
    public async Task<decimal?> GetGroupOccupancyPercentAsync(
        string grupKod, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var kod = grupKod.Trim();
        var fromD = from.UtcDateTime.Date;
        var toD = to.UtcDateTime.Date;
        var donemGun = Math.Max(1, (toD - fromD).Days); // B5: aynı-gün penceresi 1 gün sayılır
        if (to <= from) return null;

        await using var db = await factory.CreateDbContextAsync(ct);
        var aracIdler = await db.Vehicles.AsNoTracking()
            .Where(v => v.Grup != null && v.Grup.Trim().ToUpper() == kod.ToUpper()
                && v.Durum != VehicleStatus.Satildi && v.Durum != VehicleStatus.Pasif) // B1
            .Select(v => v.Id).ToListAsync(ct);
        if (aracIdler.Count == 0) return null;

        var kiralar = await db.Rentals.AsNoTracking()
            .Where(r => aracIdler.Contains(r.VehicleId) && r.Durum != RentalStatus.Iptal
                && r.BasTar < to && r.BitTar > from)
            .Select(r => new { r.BasTar, r.BitTar, r.GercekDonusTar }).ToListAsync(ct);
        var rezervasyonlar = await db.Reservations.AsNoTracking() // B3: ileri talep sinyali
            .Where(r => aracIdler.Contains(r.VehicleId)
                && (r.Durum == ReservationStatus.Rezerv || r.Durum == ReservationStatus.Onayli)
                && r.BasTar < to && r.BitTar > from)
            .Select(r => new { r.BasTar, r.BitTar }).ToListAsync(ct);

        var bitSiniri = toD == fromD ? fromD.AddDays(1) : toD; // B5 ile tutarlı üst sınır

        // Bitiş-hariç gün örtüşmesi (tarih düzeyinde); B2: erken dönüşte efektif bitiş.
        int Ortusme(DateTimeOffset bas, DateTimeOffset bit)
        {
            var lo = bas.UtcDateTime.Date > fromD ? bas.UtcDateTime.Date : fromD;
            var hi = bit.UtcDateTime.Date < bitSiniri ? bit.UtcDateTime.Date : bitSiniri;
            return hi > lo ? (hi - lo).Days : 0;
        }
        var doluGun = kiralar.Sum(r =>
        {
            var efektifBit = r.GercekDonusTar is { } gd && gd < r.BitTar ? gd : r.BitTar;
            return Ortusme(r.BasTar, efektifBit);
        }) + rezervasyonlar.Sum(r => Ortusme(r.BasTar, r.BitTar));

        var aracGun = aracIdler.Count * donemGun;
        return Math.Round((decimal)doluGun * 100m / aracGun, 2, MidpointRounding.AwayFromZero);
    }
}
