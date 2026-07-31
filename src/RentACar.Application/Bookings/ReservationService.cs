using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon iş mantığı + durum makinesi (Rezerv→Onaylı→KirayaCevrildi/İptal).
/// Tenant izolasyonu/audit alt katmanda otomatik. Liste rol bazlı şube kapsamıyla (çıkış ofisi).
/// </summary>
public sealed class ReservationService(
    IBookingRepository repository, ICurrentUser currentUser, PricingService pricing, FeeLineService feeLines)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    private readonly FeeLineService _feeLines = feeLines;

    public Task<IReadOnlyList<Reservation>> ListAsync(CancellationToken ct = default)
        => _repository.ListReservationsAsync(BranchScope.EffectiveFilter(_currentUser), ct); // C4

    public async Task<Reservation?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _repository.FindReservationAsync(id, ct);
        if (r is not null) BranchScope.RequireInScope(_currentUser, r.CikisSubeId, r.CikisOfisi); // adversarial M3
        return r;
    }

    public async Task<Guid> CreateAsync(BookingInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        BookingMath.Validate(input);
        TarihPolitikasi.RezervasyonBaslangic(input.BasTar); // geçmişe kapalı; gelecek ≤ +1yıl
        var pr = await _pricing.PriceAsync(input, ct: ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife

        // Aktif kira çakışması varsa rezervasyon alınamaz (yumuşak ön-kontrol).
        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        var reservation = new Reservation
        {
            Durum = ReservationStatus.Rezerv,
            MusteriId = input.MusteriId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            CikisOfisi = input.CikisOfisi,
            DonusOfisi = input.DonusOfisi,
            Gun = pr.Gun,
            GunlukUcret = input.GunlukUcret,
            Tutar = pr.Tutar,
            HediyeGun = pr.HediyeGun, FaturalananGun = pr.FaturalananGun, IskontoTutar = pr.IskontoTutar, HaftaSonuFark = pr.HaftaSonuFark,
            KmLimit = input.KmLimit,
            FazlaKmUcret = input.FazlaKmUcret,
            YakitBirimUcret = input.YakitBirimUcret,
            Provizyon = input.Provizyon,
            Depozito = input.Depozito,
            KomisyonOran = input.KomisyonOran,
            KomisyonTutar = input.KomisyonTutar,
            DropUcreti = input.DropUcreti,
            SonraOdeOran = input.SonraOdeOran,
            Aciklama = input.Aciklama,
            Kaynak = string.IsNullOrWhiteSpace(input.Kaynak) ? null : input.Kaynak.Trim(),
            KampanyaKodu = string.IsNullOrWhiteSpace(input.KampanyaKodu) ? null : input.KampanyaKodu.Trim(),
            FiyatTuru = string.IsNullOrWhiteSpace(input.FiyatTuru) ? null : input.FiyatTuru.Trim(), // A6-B2
            KdvOranSnapshot = pr.KdvOranSnapshot,
            // FAZ 4.5 — OTA bileşen fiyatları (bilgi; REST/kanal doldurur)
            OtaKiraBedeli = input.OtaKiraBedeli, OtaDropBedeli = input.OtaDropBedeli,
            OtaBebekKoltugu = input.OtaBebekKoltugu, OtaNavigasyon = input.OtaNavigasyon,
            OtaLcf = input.OtaLcf, OtaCdw = input.OtaCdw, OtaScdw = input.OtaScdw,
            OtaEkSurucu = input.OtaEkSurucu
        };
        await _repository.CreateReservationAsync(reservation, ct);
        return reservation.Id;
    }

    /// <summary>
    /// Rezervasyon düzenleme (roadmap I2): yalnız Rezerv/Onaylı durumda — tarih/araç/fiyat/ek alanlar/kaynak
    /// güncellenir, fiyat yeniden hesaplanır, aktif kira çakışması yeniden kontrol edilir. Defter etkilemez.
    /// </summary>
    public async Task<bool> UpdateAsync(Guid id, BookingInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        BookingMath.Validate(input);
        var existing = await _repository.FindReservationAsync(id, ct);
        if (existing is null) return false;
        if (existing.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
            throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon düzenlenebilir.");
        // Tarih politikası YALNIZ başlangıç GERÇEKTEN değişiyorsa (typo koruması) — yaşlanmış rezervasyonun
        // (başlangıcı doğal olarak geçmişte kalmış, hâlâ Rezerv/Onaylı) not/araç/fiyat düzenlemesini KİLİTLEME
        // (adversarial H5). Yeni bir geçmiş/aşırı-ileri tarihe taşıma hâlâ reddedilir.
        if (input.BasTar != existing.BasTar)
            TarihPolitikasi.RezervasyonBaslangic(input.BasTar);

        // FAZ 3.A7 adversarial B4: FİYAT-ETKİLEYEN girdiler değişmedikçe REPRICE ATLANIR — no-op/not
        // düzenlemesi kabul edilmiş fiyatı (surge dahil) SESSİZCE düşüremez/yükseltemez. Girdiler
        // değiştiyse (tarih/araç/müşteri/ücret/mod/kod/kaynak/ofis) yeni koşullarla TAM reprice —
        // surge dahil (yeni fiyatlama zaten meşru; eski "surge'süz reprice" yaklaşımı her düzenlemede
        // fiyatı tabana indiriyordu).
        var fiyatDegisti =
            existing.BasTar != input.BasTar || existing.BitTar != input.BitTar
            || existing.VehicleId != input.VehicleId || existing.MusteriId != input.MusteriId
            || existing.GunlukUcret != input.GunlukUcret
            || !string.Equals(existing.FiyatTuru ?? "", input.FiyatTuru?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.KampanyaKodu ?? "", input.KampanyaKodu?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.Kaynak ?? "", input.Kaynak?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(existing.CikisOfisi ?? "", input.CikisOfisi ?? "", StringComparison.Ordinal);
        // KDV MODU YALNIZ ÜCRET VEYA MOD DEĞİŞTİYSE UYGULANIR. Düzenleme formu ücreti KAYITLI (brüte
        // normalize edilmiş) değerle doldurup fiyat türünü aynen geri gönderiyor; net modda ("Günlük"/
        // "Toplam") dönüşümü tekrar uygulamak her kayıtta sessiz %20 zam üretiyordu (1.000 → 1.200 →
        // 1.440 …). Kullanıcı ücrete de moda da dokunmadıysa saklanan değer zaten brüttür.
        var ucretVeyaModDegisti =
            existing.GunlukUcret != input.GunlukUcret
            || !string.Equals(existing.FiyatTuru ?? "", input.FiyatTuru?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
        var pr = fiyatDegisti
            ? await _pricing.PriceAsync(input, kdvModuUygula: ucretVeyaModDegisti, ct: ct)
            : null;

        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        return await _repository.UpdateReservationAsync(id, r =>
        {
            BranchScope.RequireInScope(_currentUser, r.CikisSubeId, r.CikisOfisi); // adversarial M3
            if (r.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
                throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon düzenlenebilir.");
            r.MusteriId = input.MusteriId;
            r.VehicleId = input.VehicleId;
            r.BasTar = input.BasTar;
            r.BitTar = input.BitTar;
            r.CikisOfisi = input.CikisOfisi;
            r.DonusOfisi = input.DonusOfisi;
            if (pr is not null) // fiyat-etkileyen girdi değişti → yeni fiyat; aksi halde mevcut korunur
            {
                r.Gun = pr.Gun;
                r.GunlukUcret = input.GunlukUcret;
                r.Tutar = pr.Tutar;
                r.HediyeGun = pr.HediyeGun; r.FaturalananGun = pr.FaturalananGun; r.IskontoTutar = pr.IskontoTutar; r.HaftaSonuFark = pr.HaftaSonuFark;
                // KDV modu atlandıysa yeni fiyatlama snapshot ÜRETMEZ; mevcut snapshot korunur —
                // aksi halde tarih düzenlemesi faturanın ayrıştırma oranını sessizce sıfırlardı.
                r.KdvOranSnapshot = ucretVeyaModDegisti ? pr.KdvOranSnapshot : r.KdvOranSnapshot;
            }
            r.KmLimit = input.KmLimit;
            r.FazlaKmUcret = input.FazlaKmUcret;
            r.YakitBirimUcret = input.YakitBirimUcret;
            r.Provizyon = input.Provizyon;
            r.Depozito = input.Depozito;
            r.KomisyonOran = input.KomisyonOran;
            r.KomisyonTutar = input.KomisyonTutar;
            r.DropUcreti = input.DropUcreti;
            r.SonraOdeOran = input.SonraOdeOran;
            r.Aciklama = input.Aciklama;
            r.Kaynak = string.IsNullOrWhiteSpace(input.Kaynak) ? null : input.Kaynak.Trim();
            // FAZ 4.5 — OTA alanları (bilgi; reprice'tan bağımsız güncellenir)
            r.OtaKiraBedeli = input.OtaKiraBedeli; r.OtaDropBedeli = input.OtaDropBedeli;
            r.OtaBebekKoltugu = input.OtaBebekKoltugu; r.OtaNavigasyon = input.OtaNavigasyon;
            r.OtaLcf = input.OtaLcf; r.OtaCdw = input.OtaCdw; r.OtaScdw = input.OtaScdw;
            r.OtaEkSurucu = input.OtaEkSurucu;
            r.KampanyaKodu = string.IsNullOrWhiteSpace(input.KampanyaKodu) ? null : input.KampanyaKodu.Trim();
            r.FiyatTuru = string.IsNullOrWhiteSpace(input.FiyatTuru) ? null : input.FiyatTuru.Trim(); // A6-B2
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> ConfirmAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        return Transition(id, ReservationStatus.Onayli, [ReservationStatus.Rezerv], ct);
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        return Transition(id, ReservationStatus.Iptal, [ReservationStatus.Rezerv, ReservationStatus.Onayli], ct);
    }

    /// <summary>Tasfiye: rezervasyonu kira sözleşmesine çevirir. Yeni kira Id döner.
    /// FAZ 3.A3a: dönüşüm sonrası sistem ücret satırları uygulanır (ApplyContractFeesAsync
    /// İDEMPOTENT — adversarial gündemi "rez→kira çift ücret" bu yüzden imkânsız).</summary>
    public async Task<Guid> ConvertToRentalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        var reservation = await _repository.FindReservationAsync(id, ct)
            ?? throw new ValidationException("Rezervasyon bulunamadı.");
        BranchScope.RequireInScope(_currentUser, reservation.CikisSubeId, reservation.CikisOfisi); // adversarial M3
        if (reservation.Durum is not (ReservationStatus.Rezerv or ReservationStatus.Onayli))
            throw new ValidationException("Yalnız Rezerv/Onaylı rezervasyon kiraya çevrilebilir.");

        var kiraId = await _repository.ConvertToRentalAsync(id, res => new RentalContract
        {
            Durum = RentalStatus.Kirada,
            ReservationId = res.Id,
            MusteriId = res.MusteriId,
            VehicleId = res.VehicleId,
            BasTar = res.BasTar,
            BitTar = res.BitTar,
            CikisOfisi = res.CikisOfisi,
            DonusOfisi = res.DonusOfisi,
            Gun = res.Gun,
            GunlukUcret = res.GunlukUcret,
            KmLimit = res.KmLimit,
            FazlaKmUcret = res.FazlaKmUcret,
            YakitBirimUcret = res.YakitBirimUcret,
            Tutar = res.Tutar,
            HediyeGun = res.HediyeGun, FaturalananGun = res.FaturalananGun, IskontoTutar = res.IskontoTutar, HaftaSonuFark = res.HaftaSonuFark,
            GenelToplam = res.Tutar,
            Tahsilat = 0m,
            Bakiye = res.Tutar,
            Provizyon = res.Provizyon,
            Depozito = res.Depozito,
            KomisyonOran = res.KomisyonOran,
            KomisyonTutar = res.KomisyonTutar,
            DropUcreti = res.DropUcreti,
            SonraOdeOran = res.SonraOdeOran,
            Aciklama = res.Aciklama,
            // Dönüşüm REZERVASYON FİYAT TAAHHÜDÜNÜ taşır — yeniden fiyatlama/kod doğrulaması YAPILMAZ
            // (müşteriye verilen fiyat dönüşümde değişmez). Kod + kaynak İZ olarak kopyalanır
            // (adversarial A5-B3: iz kopyalanmayınca indirimli tutarın gerekçesi denetimde kayboluyordu).
            Kaynak = res.Kaynak,
            KampanyaKodu = res.KampanyaKodu,
            // A6-B2: net-mod niyeti + gross-up oranı kiraya taşınır — fatura SNAPSHOT'tan ayrışır,
            // tenant oranı rez-create ile fatura arasında değişse bile matrah niyetten sapmaz.
            FiyatTuru = res.FiyatTuru,
            KdvOranSnapshot = res.KdvOranSnapshot
        }, ct);
        await _feeLines.ApplyContractFeesAsync(kiraId, ct);
        return kiraId;
    }

    private async Task<bool> Transition(
        Guid id, ReservationStatus to, ReservationStatus[] allowedFrom, CancellationToken ct)
    {
        return await _repository.UpdateReservationAsync(id, r =>
        {
            BranchScope.RequireInScope(_currentUser, r.CikisSubeId, r.CikisOfisi); // adversarial M3 (Confirm/Cancel)
            if (Array.IndexOf(allowedFrom, r.Durum) < 0)
                throw new ValidationException($"Rezervasyon '{r.Durum}' durumundan '{to}' durumuna geçemez.");
            r.Durum = to;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }
}
