using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Repositories;

/// <summary>
/// Rezervasyon + kira kalıcılığı. Boşluksuz sıra tahsisi ve insert AYNI transaction'da.
/// Kira insert'inde DB exclusion constraint (23P01) çakışmayı engeller → AvailabilityConflict.
/// </summary>
public sealed class BookingRepository(IDbContextFactory<AppDbContext> factory) : IBookingRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory = factory;

    // ---- Rezervasyon ----

    public async Task<IReadOnlyList<Reservation>> ListReservationsAsync(RentACar.Application.Authorization.BranchScope.BranchFilter kapsam = default, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Reservations.AsNoTracking();
        // C4 ŞABLON (InScope ile birebir): türetilmiş-FK-eşit VEYA ofis-metni-eşit (Ordinal).
        if (!kapsam.Unrestricted)
        {
            var kid = kapsam.SubeId; var kad = kapsam.SubeAd;
            q = q.Where(r => (kid != null && r.CikisSubeId == kid)
                          || ((kid == null || r.CikisSubeId == null) && kad != null && r.CikisOfisi != null && r.CikisOfisi.Trim() == kad)); // C5
        }
        return await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
    }

    /// <summary>
    /// FAZ-48 — rezervasyon arama. Şube kapsamı SearchRentalRowsAsync ile BİREBİR aynı C4/C5
    /// şablonuyla uygulanır (kendi kopyası yazılmaz). Serbest metin araması müşteri adı/plaka
    /// içerdiğinden çözümlemeden SONRA (bellek-içi) uygulanır — kira tarafındaki desen.
    /// </summary>
    public async Task<IReadOnlyList<ReservationRow>> SearchReservationsAsync(
        ReservationFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Reservations.AsNoTracking();
        // C4 ŞABLON (InScope ile birebir): türetilmiş-FK-eşit VEYA ofis-metni-eşit (Ordinal).
        if (!filter.Kapsam.Unrestricted)
        {
            var kid = filter.Kapsam.SubeId; var kad = filter.Kapsam.SubeAd;
            q = q.Where(r => (kid != null && r.CikisSubeId == kid)
                          || ((kid == null || r.CikisSubeId == null) && kad != null && r.CikisOfisi != null && r.CikisOfisi.Trim() == kad)); // C5
        }
        if (filter.Durum is { } d) q = q.Where(r => r.Durum == d);
        if (filter.TarihMin is { } min) q = q.Where(r => r.BasTar >= min);
        if (filter.TarihMax is { } max) q = q.Where(r => r.BasTar <= max);

        var rezler = await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
        if (rezler.Count == 0) return [];

        var custIds = rezler.Select(r => r.MusteriId).Distinct().ToList();
        var vehIds = rezler.Select(r => r.VehicleId).Distinct().ToList();

        // PII çözülmez: DisplayName girdileri (Unvan/Ad/Soyad) ve CepTel düz-metin kolonlar.
        var cariler = (await db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Tip, c.Unvan, c.Ad, c.Soyad, c.CepTel }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => (
                Ad: new Customer { Tip = c.Tip, Unvan = c.Unvan, Ad = c.Ad, Soyad = c.Soyad }.DisplayName,
                c.CepTel));
        var plakalar = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
                .Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);

        var rows = rezler.Select(r =>
        {
            var cari = cariler.TryGetValue(r.MusteriId, out var c) ? c : (Ad: "—", CepTel: (string?)null);
            return new ReservationRow(r, cari.Ad, cari.CepTel, plakalar.GetValueOrDefault(r.VehicleId, "—"));
        }).AsEnumerable();

        // Kaynak eşleşmesi BELLEK-İÇİ ve ORDINAL: SQL'e `lower()` olarak itmek karşılaştırmayı iki ayrı
        // kültüre (C# ToLower + PG collation) böler — Türkçe I/İ çiftinde ikisi ayrışır ve "Web Sitesi"
        // gibi bir kaynak sessizce eşleşmez. Ekranın FAZ-85'teki davranışı da tam olarak buydu.
        if (!string.IsNullOrWhiteSpace(filter.Kaynak))
        {
            var kaynak = filter.Kaynak.Trim();
            rows = rows.Where(r => string.Equals(r.Rez.Kaynak?.Trim(), kaynak, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var t = filter.Query.Trim();
            // Plaka DB'de boşluksuz saklanıyor: kullanıcı "34 AA 11" yazınca da bulunsun diye terim
            // ayrıca harf/rakama indirgenip DENENİR (yalnız GENİŞLETİR — ham eşleşme aynen korunur).
            var plakaTerim = new string(t.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            rows = rows.Where(r =>
                r.Rez.ReservationNo.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.MusteriAd.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.Plaka.Contains(t, StringComparison.OrdinalIgnoreCase)
                || (plakaTerim.Length > 0 && r.Plaka.Contains(plakaTerim, StringComparison.OrdinalIgnoreCase)));
        }
        return rows.ToList();
    }

    public async Task<Reservation?> FindReservationAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task CreateReservationAsync(Reservation reservation, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            reservation.ReservationNo = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.Rezervasyon, ct);
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }, ct);
    }

    public async Task<bool> UpdateReservationAsync(Guid id, Action<Reservation> apply, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var r = await db.Reservations.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        apply(r);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ---- Kira ----

    public async Task<IReadOnlyList<RentalContract>> ListRentalsAsync(RentACar.Application.Authorization.BranchScope.BranchFilter kapsam = default, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Rentals.AsNoTracking();
        // C4 ŞABLON (InScope ile birebir): türetilmiş-FK-eşit VEYA ofis-metni-eşit (Ordinal).
        if (!kapsam.Unrestricted)
        {
            var kid = kapsam.SubeId; var kad = kapsam.SubeAd;
            q = q.Where(r => (kid != null && r.CikisSubeId == kid)
                          || ((kid == null || r.CikisSubeId == null) && kad != null && r.CikisOfisi != null && r.CikisOfisi.Trim() == kad)); // C5
        }
        return await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
    }

    public async Task<RentalContract?> FindRentalAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<RentalRow>> SearchRentalRowsAsync(RentalFilter filter, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var q = db.Rentals.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Sube)) q = q.Where(r => r.CikisOfisi == filter.Sube);
        // C4 ŞABLON (InScope ile birebir): türetilmiş-FK-eşit VEYA ofis-metni-eşit (Ordinal).
        if (!filter.Kapsam.Unrestricted)
        {
            var kkid = filter.Kapsam.SubeId; var kkad = filter.Kapsam.SubeAd;
            q = q.Where(r => (kkid != null && r.CikisSubeId == kkid)
                          || ((kkid == null || r.CikisSubeId == null) && kkad != null && r.CikisOfisi != null && r.CikisOfisi.Trim() == kkad)); // C5
        }
        if (filter.Durum is { } d) q = q.Where(r => r.Durum == d);

        // FAZ-46 — tarih aralığı HANGİ alana uygulanacak (canlı "Tarih Listesi" seçicisi).
        // null → Baslangic: bu fazdan önceki davranış birebir korunur.
        var min = filter.BaslangicMin; var max = filter.BaslangicMax;
        if (min is not null || max is not null)
        {
            switch (filter.TarihTuru ?? TarihListesiTuru.Baslangic)
            {
                case TarihListesiTuru.Bitis:
                    if (min is { } bmin) q = q.Where(r => r.BitTar >= bmin);
                    if (max is { } bmax) q = q.Where(r => r.BitTar <= bmax);
                    break;
                case TarihListesiTuru.Islem:
                    if (min is { } imin) q = q.Where(r => r.CreatedAtUtc >= imin);
                    if (max is { } imax) q = q.Where(r => r.CreatedAtUtc <= imax);
                    break;
                case TarihListesiTuru.Vade:
                    // Vadesi GİRİLMEMİŞ sözleşme vade aralığına DÜŞMEZ (null sessizce eşleşmez).
                    if (min is { } vmin) q = q.Where(r => r.VadeTar != null && r.VadeTar >= vmin);
                    if (max is { } vmax) q = q.Where(r => r.VadeTar != null && r.VadeTar <= vmax);
                    break;
                default:
                    if (min is { } smin) q = q.Where(r => r.BasTar >= smin);
                    if (max is { } smax) q = q.Where(r => r.BasTar <= smax);
                    break;
            }
        }

        // FAZ-46 — ofis filtresi çıkış/dönüş ayrımıyla. Seçim yoksa eski davranış (herhangi biri).
        if (!string.IsNullOrWhiteSpace(filter.Ofis))
        {
            var ofis = filter.Ofis;
            q = filter.OfisDurum switch
            {
                OfisDurumu.Cikis => q.Where(r => r.CikisOfisi == ofis),
                OfisDurumu.Donus => q.Where(r => r.DonusOfisi == ofis),
                _ => q.Where(r => r.CikisOfisi == ofis || r.DonusOfisi == ofis)
            };
        }

        // FAZ-46 — sözleşmenin KENDİ Kaynak alanı. Karşılaştırma bellek-içi yapılamaz (sayfa
        // tümünü çekmesin diye) ama kültür tuzağına düşmemek için SQL'e lower() itilmez:
        // eşitlik Ordinal'e denk gelen doğrudan karşılaştırmadır (Trim uygulaması yazma yolunda).
        if (!string.IsNullOrWhiteSpace(filter.RezKaynak))
        {
            var kaynak = filter.RezKaynak.Trim();
            q = q.Where(r => r.Kaynak == kaynak);
        }
        if (filter.PersonelId is { } pid) q = q.Where(r => r.TeslimAlanPersonelId == pid);

        // FAZ-46 — araç boyutundan süzme (sahip / grup). Araç kümesi ÖNCE çözülür: aksi hâlde
        // her satır için araç sorgusu gerekirdi. Eşleşen araç yoksa sonuç boştur (erken çıkış).
        if (!string.IsNullOrWhiteSpace(filter.SahipGrup) || !string.IsNullOrWhiteSpace(filter.AracGrubu))
        {
            var vq = db.Vehicles.AsNoTracking().Select(v => new { v.Id, v.Grup, v.AracSahibi });
            if (!string.IsNullOrWhiteSpace(filter.SahipGrup))
            {
                var sahip = filter.SahipGrup.Trim();
                vq = vq.Where(v => v.AracSahibi == sahip);
            }
            if (!string.IsNullOrWhiteSpace(filter.AracGrubu))
            {
                var grup = filter.AracGrubu.Trim();
                vq = vq.Where(v => v.Grup == grup);
            }
            var eslesen = await vq.Select(v => v.Id).ToListAsync(ct);
            if (eslesen.Count == 0) return [];
            q = q.Where(r => eslesen.Contains(r.VehicleId));
        }

        var rentals = await q.OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);

        var custIds = rentals.Select(r => r.MusteriId).Distinct().ToList();
        var vehIds = rentals.Select(r => r.VehicleId).Distinct().ToList();
        var rentalIds = rentals.Select(r => r.Id).ToList();

        var custNames = (await db.Customers.AsNoTracking().Where(c => custIds.Contains(c.Id)).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.DisplayName);
        var plakalar = (await db.Vehicles.AsNoTracking().Where(v => vehIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Plaka }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => v.Plaka);
        var invoicedSet = (await db.Invoices.AsNoTracking()
            .Where(i => i.RentalId != null && rentalIds.Contains(i.RentalId!.Value))
            .Select(i => i.RentalId!.Value).ToListAsync(ct)).ToHashSet();

        var rows = rentals.Select(r => new RentalRow
        {
            Id = r.Id,
            SozlesmeNo = r.SozlesmeNo,
            MusteriId = r.MusteriId,
            Doviz = r.Doviz,
            MusteriAd = custNames.GetValueOrDefault(r.MusteriId, "—"),
            Plaka = plakalar.GetValueOrDefault(r.VehicleId, "—"),
            BasTar = r.BasTar,
            BitTar = r.BitTar,
            Gun = r.Gun,
            Tutar = r.Tutar,
            Bakiye = r.Bakiye,
            Durum = r.Durum,
            Faturali = invoicedSet.Contains(r.Id),
            // FAZ-46 — entity'de zaten var olan kolonlar projeksiyona taşındı (hesap YOK).
            Kaynak = r.Kaynak,
            Provizyon = r.Provizyon,
            Depozito = r.Depozito,
            KomisyonOran = r.KomisyonOran,
            KomisyonTutar = r.KomisyonTutar,
            VadeTar = r.VadeTar,
            OnayKodu = r.OnayKodu,
            ProjeAdi = r.ProjeAdi,
            AssistFirma = r.AssistFirma,
            OzelSoforBilgisi = r.OzelSoforBilgisi,
            HediyeGun = r.HediyeGun,
            FaturalananGun = r.FaturalananGun,
            CikisOfisi = r.CikisOfisi,
            DonusOfisi = r.DonusOfisi
        }).AsEnumerable();

        if (filter.Faturali is { } fat) rows = rows.Where(r => r.Faturali == fat);
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var t = filter.Query.Trim();
            rows = rows.Where(r =>
                r.SozlesmeNo.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.MusteriAd.Contains(t, StringComparison.OrdinalIgnoreCase)
                || r.Plaka.Contains(t, StringComparison.OrdinalIgnoreCase));
        }
        return rows.ToList();
    }

    public async Task CreateRentalAsync(RentalContract contract, CancellationToken ct = default)
    {
        await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            contract.SozlesmeNo = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.KiraSozlesmesi, ct);
            db.Rentals.Add(contract);
            try
            {
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (IsExclusionViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new AvailabilityConflictException();
            }
        }, ct);
    }

    public async Task<bool> UpdateRentalAsync(Guid id, Action<RentalContract> apply, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var r = await db.Rentals.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return false;
            apply(r);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsExclusionViolation(ex))
            {
                // Kira uzatma (ExtendAsync) tarih aralığını değiştirdiğinde GiST exclusion'a takılabilir →
                // CreateRentalAsync ile aynı zarif hata (adversarial I1 MEDIUM-1).
                throw new AvailabilityConflictException();
            }
            return true;
        }, ct);
    }

    public async Task<bool> UpdateRentalWithVehicleAsync(
        Guid id, Action<RentalContract> applyRental, Action<Vehicle> applyVehicle,
        Func<RentalContract, VehicleKmLog>? kmLog = null, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // deadlock/serialization çakışmasında baştan dene
        {
            // ServiceRecordRepository.TransitionAsync deseni: tek context + TX + iki entity + tek SaveChanges.
            // Araç TX İÇİNDE okunur — PgRetry tekrarında bayat vehicle okunmaz (adversarial inceleme 3a).
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var r = await db.Rentals.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return false;
            applyRental(r);

            var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == r.VehicleId, ct);
            if (vehicle is not null)
            {
                applyVehicle(vehicle);
                vehicle.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            // FAZ 2.5: km zaman-serisi — kira güncellemesiyle AYNI transaction (yarım seri kalmaz).
            if (kmLog is not null) db.KmLoglari.Add(kmLog(r));

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }, ct);
    }

    public async Task<bool> HasOverlappingActiveRentalAsync(
        Guid vehicleId, DateTimeOffset basTar, DateTimeOffset bitTar,
        Guid? excludeRentalId = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Rentals.AsNoTracking()
            .Where(r => r.VehicleId == vehicleId
                && r.Durum == RentalStatus.Kirada
                && (excludeRentalId == null || r.Id != excludeRentalId)
                && r.BasTar < bitTar && basTar < r.BitTar) // [bas,bit) ∩ [r.Bas,r.Bit)
            .AnyAsync(ct);
    }

    public async Task<Guid> ConvertToRentalAsync(
        Guid reservationId, Func<Reservation, RentalContract> buildRental, CancellationToken ct = default)
    {
        return await PgRetry.RunAsync(async () => // P0-5: deadlock/serialization çakışmasında baştan dene
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var reservation = await db.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId, ct)
                ?? throw new ValidationException("Rezervasyon bulunamadı.");

            var rental = buildRental(reservation);
            rental.SozlesmeNo = await BelgeNoUretici.UretAsync(db, db.TenantId, BelgeNoTuru.KiraSozlesmesi, ct);
            db.Rentals.Add(rental);

            reservation.Durum = ReservationStatus.KirayaCevrildi;
            reservation.RentalContractId = rental.Id;
            reservation.UpdatedAtUtc = DateTimeOffset.UtcNow;

            try
            {
                await db.SaveChangesAsync(ct); // rental insert + reservation update + audit, atomik
                await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex) when (IsExclusionViolation(ex))
            {
                await tx.RollbackAsync(ct);
                throw new AvailabilityConflictException();
            }

            return rental.Id;
        }, ct);
    }

    private static bool IsExclusionViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };
}
