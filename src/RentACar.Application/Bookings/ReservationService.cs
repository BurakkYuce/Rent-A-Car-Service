using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Rezervasyon iş mantığı + durum makinesi (Rezerv→Onaylı→KirayaCevrildi/İptal).
/// Tenant izolasyonu/audit alt katmanda otomatik. Liste rol bazlı şube kapsamıyla (çıkış ofisi).
/// </summary>
public sealed class ReservationService(
    IBookingRepository repository, ICurrentUser currentUser, PricingService pricing, FeeLineService feeLines,
    RezKaynakKuralService kaynakKural)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    private readonly FeeLineService _feeLines = feeLines;
    private readonly RezKaynakKuralService _kaynakKural = kaynakKural; // FAZ-49

    public Task<IReadOnlyList<Reservation>> ListAsync(CancellationToken ct = default)
        => _repository.ListReservationsAsync(BranchScope.EffectiveFilter(_currentUser), ct); // C4

    /// <summary>FAZ-48 — rezervasyon listesi: filtre + müşteri/araç adları. Rol bazlı şube kapsamı
    /// BURADA zorlanır (çağıranın filtredeki Kapsam'ı yok sayılır — genişletme yolu yok).</summary>
    public Task<IReadOnlyList<ReservationRow>> SearchAsync(ReservationFilter filter, CancellationToken ct = default)
    {
        filter.Kapsam = BranchScope.EffectiveFilter(_currentUser); // C4: FK-farkındalı
        return _repository.SearchReservationsAsync(filter, ct);
    }

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
        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA (fiyatlamadan/kabulden ÖNCE): kaynağın gün sınırı
        // ve drop yasağı burada reddedilir, km sınırsızlığı burada sabitlenir.
        var kaynak = await _kaynakKural.CozAsync(input.Kaynak, ct);
        RezKaynakKural.MaxGunGuard(kaynak, BookingMath.ComputeGun(input.BasTar, input.BitTar));
        RezKaynakKural.DropGuard(kaynak, input.CikisOfisi, input.DonusOfisi);
        var kmLimit = RezKaynakKural.KmLimitUygula(kaynak, input.KmLimit);
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
            KmLimit = kmLimit,                 // FAZ-49: KmSinirsiz kaynakta 0'a sabitlenir
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
            OtaEkSurucu = input.OtaEkSurucu,
            // FAZ-48 — talep/organizasyon bilgisi (deftere/fiyata GİRMEZ; dönüşümde kiraya taşınır).
            TalepTuru = BookingMath.Kirp(input.TalepTuru, 64, "Talep türü"),
            GeldigiBirim = BookingMath.Kirp(input.GeldigiBirim, 64, "Geldiği birim"),
            OnayKodu = BookingMath.Kirp(input.OnayKodu, 64, "Onay kodu"),
            ProjeAdi = BookingMath.Kirp(input.ProjeAdi, 128, "Proje adı")
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

        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA. Kaynak bu istekte DEĞİŞEBİLDİĞİ için İKİ kaynak
        // da çözülür: yasak kural (tarih kilidi/uzatma yasağı) hangisinden gelirse gelsin geçerlidir
        // — aksi halde "kaynağı serbest olana çevir + tarihi değiştir" tek istekte kuralı delerdi.
        // Sınır/biçim kuralları (MaxGun/drop/km) SONUÇ durumu tanımladığından YENİ kaynağa bakar.
        //
        // <b>YALNIZ GERÇEKTEN DEĞİŞEN alan denetlenir</b> (tarih politikasıyla AYNI ders): kural
        // kaynağa SONRADAN konabilir. Kural konmadan önce açılmış bir rezervasyonun (ör. 10 günlük
        // ya da drop'lu) not/araç düzenlemesini de reddetseydik, kayıt hiç düzenlenemez hale gelir
        // ve tüm yaşlanmış-kayıt akışı kilitlenirdi. Yeni bir ihlal YARATMAK hâlâ reddedilir.
        var mevcutKaynak = await _kaynakKural.CozAsync(existing.Kaynak, ct);
        var yeniKaynak = await _kaynakKural.CozAsync(input.Kaynak, ct);
        var tarihDegisti = input.BasTar != existing.BasTar || input.BitTar != existing.BitTar;
        var kaynakDegisti = !string.Equals(input.Kaynak?.Trim() ?? "", existing.Kaynak ?? "",
            StringComparison.OrdinalIgnoreCase);
        var ofisDegisti =
            !string.Equals(input.CikisOfisi ?? "", existing.CikisOfisi ?? "", StringComparison.Ordinal)
            || !string.Equals(input.DonusOfisi ?? "", existing.DonusOfisi ?? "", StringComparison.Ordinal);

        if (tarihDegisti)
            RezKaynakKural.TarihDegisiklikGuard(mevcutKaynak, yeniKaynak);
        if (input.BitTar > existing.BitTar) // bitişi ileri almak = uzatma
        {
            RezKaynakKural.UzatmaGuard(mevcutKaynak);
            RezKaynakKural.UzatmaGuard(yeniKaynak);
        }
        if (tarihDegisti || kaynakDegisti)
            RezKaynakKural.MaxGunGuard(yeniKaynak, BookingMath.ComputeGun(input.BasTar, input.BitTar));
        if (ofisDegisti || kaynakDegisti)
            RezKaynakKural.DropGuard(yeniKaynak, input.CikisOfisi, input.DonusOfisi);
        // Km sabitlemesi bir RED değil, sonucu yazma biçimidir → koşulsuz (kilitleme riski yok).
        var kmLimit = RezKaynakKural.KmLimitUygula(yeniKaynak, input.KmLimit);

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

        // FAZ-48 — uzunluk çitleri YAZMADAN ÖNCE (delege içinde patlamak yarım iş bırakma riski taşır).
        var talepTuru = BookingMath.Kirp(input.TalepTuru, 64, "Talep türü");
        var geldigiBirim = BookingMath.Kirp(input.GeldigiBirim, 64, "Geldiği birim");
        var onayKodu = BookingMath.Kirp(input.OnayKodu, 64, "Onay kodu");
        var projeAdi = BookingMath.Kirp(input.ProjeAdi, 128, "Proje adı");

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
            r.KmLimit = kmLimit;               // FAZ-49: KmSinirsiz kaynakta 0'a sabitlenir
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
            // FAZ-48 — talep/organizasyon bilgisi (fiyat-etkisiz; reprice'tan bağımsız güncellenir).
            r.TalepTuru = talepTuru;
            r.GeldigiBirim = geldigiBirim;
            r.OnayKodu = onayKodu;
            r.ProjeAdi = projeAdi;
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
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // inceltme: belge iptali ayrı izin
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
            KdvOranSnapshot = res.KdvOranSnapshot,
            // FAZ-48 — talep/organizasyon bilgisi rezervasyondan sözleşmeye AYNEN taşınır (aynı adlı
            // alanlar RentalContract'ta zaten vardı); operatör kira mega-formunda tekrar girmez.
            TalepTuru = res.TalepTuru,
            GeldigiBirim = res.GeldigiBirim,
            OnayKodu = res.OnayKodu,
            ProjeAdi = res.ProjeAdi
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
