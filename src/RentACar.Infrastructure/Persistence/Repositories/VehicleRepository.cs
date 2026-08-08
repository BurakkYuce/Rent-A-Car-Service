using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// IVehicleRepository implementasyonu. Her işlemde factory'den kısa-ömürlü context
/// açar (Blazor Server'da uzun-ömürlü scoped DbContext eşzamanlılık hatasını önler).
/// Update, audit eski/yeni farkı için entity'yi yükleyip mutasyonu uygular.
/// </summary>
public sealed class VehicleRepository(IDbContextFactory<AppDbContext> factory) : IVehicleRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    public async Task<IReadOnlyList<Vehicle>> ListAsync(string? sube = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Vehicles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sube)) q = q.Where(v => v.Sube == sube);
        return await q.OrderBy(v => v.Plaka).ToListAsync(ct);
    }

    public async Task<PagedResult<Vehicle>> SearchAsync(VehicleFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Vehicles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Sube)) q = q.Where(v => v.Sube == filter.Sube);
        // C3 ŞABLON (BranchScope.InScope ile birebir): FK-eşit VEYA metin-eşit (Ordinal).
        if (!filter.Kapsam.Unrestricted)
        {
            var kid = filter.Kapsam.SubeId; var kad = filter.Kapsam.SubeAd;
            q = q.Where(v => (kid != null && v.SubeId == kid)
                          || ((kid == null || v.SubeId == null) && kad != null && v.Sube != null && v.Sube.Trim() == kad)); // C5
        }
        if (filter.Durum is { } d) q = q.Where(v => v.Durum == d);
        if (!string.IsNullOrWhiteSpace(filter.Grup))
        {
            // FAZ-11: aynı kutu, iki hedef. SIPP kodu kayıtta BÜYÜK HARF normalize edilir
            // (VehicleService.NormalizeSipp) → arama terimi de aynı normalizasyondan geçmeli,
            // yoksa listede gördüğü "cdmd"yi yazan kullanıcı boş sonuç alır.
            var deger = filter.Grup.Trim();
            q = filter.GrupTuru == AracGrupTuru.Sipp
                ? q.Where(v => v.Sipp == deger.ToUpperInvariant())
                : q.Where(v => v.Grup == filter.Grup);
        }
        // FAZ-11 tarih aralığı — TİP seçilmemişse aralık HİÇ uygulanmaz (bkz. AracTarihTuru.Yok).
        if (filter.TarihTuru != AracTarihTuru.Yok && (filter.TarihBas is not null || filter.TarihBit is not null))
        {
            var bas = filter.TarihBas;
            // Bitiş GÜN DAHİL: kullanıcı "31.12'ye kadar" derken 31.12'yi de kastediyor
            // (TCMB kur fazındaki "BitTar gün-dahil" dersiyle aynı). TarihBit sözleşme gereği
            // bitiş GÜNÜNÜN başlangıç anıdır → +1 gün ve STRICT "<" ile o günün tamamı kapsanır.
            // Tuzak: burada UtcDateTime.Date almak, tarihlerin yerel gece-yarısı olarak yazıldığı
            // (FormParse.Date) bu şemada günü bir geri kaydırırdı.
            var bit = filter.TarihBit?.AddDays(1);
            q = filter.TarihTuru switch
            {
                AracTarihTuru.FiloGiris => q.Where(v => v.FiloGirisTarih != null
                    && (bas == null || v.FiloGirisTarih >= bas) && (bit == null || v.FiloGirisTarih < bit)),
                AracTarihTuru.FiloCikis => q.Where(v => v.FiloCikisTarih != null
                    && (bas == null || v.FiloCikisTarih >= bas) && (bit == null || v.FiloCikisTarih < bit)),
                _ => q.Where(v => v.TescilTarihi != null
                    && (bas == null || v.TescilTarihi >= bas) && (bit == null || v.TescilTarihi < bit)),
            };
        }
        // FAZ-11 araç sahibi: özel "girilmemiş" kovası ya da belirli bir sahip (bkz. AracSahiplik).
        if (filter.Sahiplik == AracSahiplik.Girilmemis)
        {
            q = q.Where(v => v.AracSahibi == null || v.AracSahibi.Trim() == "");
        }
        else if (!string.IsNullOrWhiteSpace(filter.AracSahibi))
        {
            var sahip = filter.AracSahibi.Trim();
            q = q.Where(v => v.AracSahibi != null && v.AracSahibi.Trim() == sahip);
        }
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = $"%{filter.Query.Trim()}%";
            q = q.Where(v => EF.Functions.ILike(v.Plaka, term)
                || (v.Marka != null && EF.Functions.ILike(v.Marka, term)));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(v => v.Plaka)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(ct);
        return new PagedResult<Vehicle>(items, total, filter.Page, filter.PageSize);
    }

    public async Task<IReadOnlyList<VehicleDetayRow>> ListDetayAsync(
        VehicleDetayFilter? filter = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Vehicles.AsNoTracking();
        if (filter is not null)
        {
            if (filter.Durum is { } d) q = q.Where(v => v.Durum == d);
            if (!string.IsNullOrWhiteSpace(filter.Sube))
            {
                var sb = filter.Sube.Trim();
                q = q.Where(v => v.Sube != null && v.Sube.Trim() == sb);
            }
            if (!string.IsNullOrWhiteSpace(filter.Ara))
            {
                var a = filter.Ara.Trim();
                // Plaka normalize saklanıyor → arama terimi de normalize (FAZ-63 dersi).
                var p = a.ToUpperInvariant().Replace(" ", string.Empty);
                q = q.Where(v => EF.Functions.ILike(v.Plaka, $"%{p}%")
                              || (v.Marka != null && EF.Functions.ILike(v.Marka, $"%{a}%"))
                              || (v.Tip != null && EF.Functions.ILike(v.Tip, $"%{a}%"))
                              || (v.BelgeNo != null && EF.Functions.ILike(v.BelgeNo, $"%{a}%"))
                              || (v.RuhsatSahibi != null && EF.Functions.ILike(v.RuhsatSahibi, $"%{a}%")));
            }
        }

        var limit = Math.Clamp(filter?.EnFazla ?? 1000, 1, 10000);
        var araclar = await q.OrderBy(v => v.Plaka).Take(limit).ToListAsync(ct);
        if (araclar.Count == 0) return [];
        var ids = araclar.Select(v => v.Id).ToList();

        // Son kredi bankası (araç başına EN YENİ kredi).
        var krediler = (await db.AracKredileri.AsNoTracking()
                .Where(k => k.VehicleId != null && ids.Contains(k.VehicleId.Value))
                .Select(k => new { Arac = k.VehicleId!.Value, k.BankaAdi, k.CreatedAtUtc })
                .ToListAsync(ct))
            .GroupBy(k => k.Arac)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(k => k.CreatedAtUtc).First().BankaAdi);

        // Muayene: araç başına EN GEÇ biten kayıt (yürürlükteki muayene).
        var muayene = (await db.InspectionRecords.AsNoTracking()
                .Where(m => ids.Contains(m.VehicleId))
                .Select(m => new { m.VehicleId, m.Bitis }).ToListAsync(ct))
            .GroupBy(m => m.VehicleId)
            .ToDictionary(g => g.Key, g => g.Max(m => m.Bitis));

        // Sigorta: TİPE GÖRE ayrı — Kasko ve Trafik farklı poliçelerdir, tek "sigorta bitişi"
        // kolonu ikisini karıştırırdı.
        var policeler = await db.InsurancePolicies.AsNoTracking()
            .Where(p => ids.Contains(p.VehicleId))
            .Select(p => new { p.VehicleId, p.Tip, p.Bitis }).ToListAsync(ct);
        var kasko = policeler.Where(p => p.Tip == InsuranceType.Kasko)
            .GroupBy(p => p.VehicleId).ToDictionary(g => g.Key, g => g.Max(p => p.Bitis));
        var trafik = policeler.Where(p => p.Tip == InsuranceType.Trafik)
            .GroupBy(p => p.VehicleId).ToDictionary(g => g.Key, g => g.Max(p => p.Bitis));

        // Satış (araç başına en yeni).
        var satislar = (await db.VehicleSales.AsNoTracking()
                .Where(x => ids.Contains(x.VehicleId))
                .Select(x => new { x.VehicleId, x.HedefFiyat, x.IhaleTarihi, x.IhaleFirmasi, x.NoterSatisTarihi, x.Tarih })
                .ToListAsync(ct))
            .GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Tarih).First());

        // AKTİF kira CANLI çözülür — araçta böyle bir kolon YOK (bkz. VehicleDetayRow özeti).
        var aktifKiralar = (await (
                from r in db.Rentals.AsNoTracking().Where(r => r.Durum == RentalStatus.Kirada && ids.Contains(r.VehicleId))
                join c in db.Customers.AsNoTracking() on r.MusteriId equals c.Id into cg
                from c in cg.DefaultIfEmpty()
                select new
                {
                    r.VehicleId, r.BitTar, r.SozlesmeNo, r.BasTar,
                    Musteri = c == null ? null : (c.Tip == CariType.Bireysel
                        ? ((c.Ad ?? "") + " " + (c.Soyad ?? "")) : c.Unvan)
                }).ToListAsync(ct))
            .GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.BasTar).First());

        return araclar.Select(v =>
        {
            satislar.TryGetValue(v.Id, out var sat);
            aktifKiralar.TryGetValue(v.Id, out var kira);
            return new VehicleDetayRow(
                v,
                krediler.GetValueOrDefault(v.Id),
                muayene.TryGetValue(v.Id, out var mb) ? mb : null,
                kasko.TryGetValue(v.Id, out var kb) ? kb : null,
                trafik.TryGetValue(v.Id, out var tb) ? tb : null,
                sat?.HedefFiyat, sat?.IhaleTarihi, sat?.IhaleFirmasi, sat?.NoterSatisTarihi,
                string.IsNullOrWhiteSpace(kira?.Musteri) ? null : kira!.Musteri!.Trim(),
                kira?.BitTar, kira?.SozlesmeNo);
        }).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, VehicleListeEk>> ListeEkAsync(
        IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct = default)
    {
        if (vehicleIds.Count == 0) return new Dictionary<Guid, VehicleListeEk>();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var ids = vehicleIds.ToList();

        // Aktif kira sözleşme no (araç başına en geç başlayan açık kira).
        var kiraNo = (await db.Rentals.AsNoTracking()
                .Where(r => r.Durum == RentalStatus.Kirada && ids.Contains(r.VehicleId))
                .Select(r => new { r.VehicleId, r.SozlesmeNo, r.BasTar })
                .ToListAsync(ct))
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.BasTar).First().SozlesmeNo);

        // Bayraklar: "var mı" sorusu → Select yerine küme; EF bunları EXISTS'e indirger.
        var servisli = (await db.ServiceRecords.AsNoTracking()
            .Where(s => ids.Contains(s.VehicleId)
                     && (s.Durum == ServisDurum.Acik || s.Durum == ServisDurum.Serviste))
            .Select(s => s.VehicleId).Distinct().ToListAsync(ct)).ToHashSet();
        var bafli = (await db.Baflar.AsNoTracking()
            .Where(b => ids.Contains(b.VehicleId) && b.Durum == BafDurum.Acik)
            .Select(b => b.VehicleId).Distinct().ToListAsync(ct)).ToHashSet();
        // Satış bayrağı İPTAL'i saymaz: iptal edilmiş satış girişimi aracı "satılıyor" göstermez.
        var satisli = (await db.VehicleSales.AsNoTracking()
            .Where(x => ids.Contains(x.VehicleId) && x.Durum != SatisDurum.Iptal)
            .Select(x => x.VehicleId).Distinct().ToListAsync(ct)).ToHashSet();

        // Kasko: araç başına EN GEÇ biten poliçe (yürürlükteki). Trafik poliçesi AYRI bir üründür,
        // buraya karıştırılmaz (VehicleDetayRow'daki ayrımla aynı).
        var kasko = (await db.InsurancePolicies.AsNoTracking()
                .Where(p => ids.Contains(p.VehicleId) && p.Tip == InsuranceType.Kasko)
                .Select(p => new { p.VehicleId, p.Bitis, p.Prim })
                .ToListAsync(ct))
            .GroupBy(p => p.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Bitis).First());

        // Kredi: araç başına en yeni kayıt. "Son tarih" kolonu YOK → başlangıç + taksit sayısı (ay).
        var kredi = (await db.AracKredileri.AsNoTracking()
                .Where(k => k.VehicleId != null && ids.Contains(k.VehicleId.Value))
                .Select(k => new { Arac = k.VehicleId!.Value, k.BankaAdi, k.BaslangicTarihi, k.TaksitSayisi, k.CreatedAtUtc })
                .ToListAsync(ct))
            .GroupBy(k => k.Arac)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(k => k.CreatedAtUtc).First());

        return ids.ToDictionary(id => id, id =>
        {
            kasko.TryGetValue(id, out var ks);
            kredi.TryGetValue(id, out var kr);
            return new VehicleListeEk(
                kiraNo.GetValueOrDefault(id),
                servisli.Contains(id),
                bafli.Contains(id),
                satisli.Contains(id),
                ks?.Bitis,
                ks?.Prim,
                kr?.BankaAdi,
                kr is null ? null : kr.BaslangicTarihi.AddMonths(kr.TaksitSayisi));
        });
    }

    public async Task<Vehicle?> FindAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<bool> PlakaExistsAsync(string plaka, Guid? excludeId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Vehicles
            .AsNoTracking()
            .Where(v => v.Plaka == plaka && (excludeId == null || v.Id != excludeId))
            .AnyAsync(ct);
    }

    public async Task CreateAsync(Vehicle vehicle, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Vehicles.Add(vehicle);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicatePlakaException(vehicle.Plaka);
        }
    }

    public async Task<bool> UpdateAsync(Guid id, Action<Vehicle> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return false;

        apply(vehicle); // mutasyon → ChangeTracker eski/yeni farkı yakalar (audit)
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            throw new DuplicatePlakaException(vehicle.Plaka);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, ct);
        if (vehicle is null) return false;

        db.Vehicles.Remove(vehicle);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ManuelKmEkleAsync(Guid id, int km, DateTimeOffset tarih, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // FOR UPDATE: eşzamanlı manuel girişler serileşir — geriye-gitme kararı taze Km ile verilir.
            var vehicle = await db.Vehicles
                .FromSqlRaw("SELECT * FROM \"Vehicles\" WHERE \"Id\" = {0} FOR UPDATE", id)
                .FirstOrDefaultAsync(ct);
            if (vehicle is null) return false;
            if (km < vehicle.Km)
                throw new ValidationException($"KM geriye gidemez (araç odometresi {vehicle.Km}).");

            vehicle.Km = km;
            vehicle.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.KmLoglari.Add(new VehicleKmLog
            { VehicleId = id, Tarih = tarih, Km = km, Kaynak = KmLogKaynak.Manuel });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    public async Task<IReadOnlyList<VehicleKmLog>> KmLoglariAsync(
        Guid vehicleId, int limit = 20, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.KmLoglari.AsNoTracking()
            .Where(k => k.VehicleId == vehicleId)
            .OrderByDescending(k => k.Tarih).ThenByDescending(k => k.Km)
            .Take(limit).ToListAsync(ct);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
