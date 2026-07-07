using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Bookings;

/// <summary>
/// Kira sözleşmesi iş mantığı. Doğrudan kira oluşturma + iptal. Double-booking,
/// DB exclusion constraint ile garanti edilir (eşzamanlı istekte tek kazanan).
/// Liste rol bazlı şube kapsamıyla (çıkış ofisi).
/// </summary>
public sealed class RentalService(
    IBookingRepository repository,
    ICurrentUser currentUser,
    PricingService pricing,
    RentACar.Application.RentalAddOns.IRentalAddOnRepository addOnRepository,
    RentACar.Application.Kur.KurService kurService,
    RentACar.Application.Personnel.IPersonelRepository personelRepository,
    RentACar.Application.Customers.ICustomerRepository customerRepository)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    private readonly RentACar.Application.RentalAddOns.IRentalAddOnRepository _addOnRepository = addOnRepository;

    public Task<IReadOnlyList<RentalContract>> ListAsync(CancellationToken ct = default)
        => _repository.ListRentalsAsync(BranchScope.Effective(_currentUser), ct);

    /// <summary>Kira listesi: filtre + müşteri/araç/fatura-durumu. Rol bazlı şube kapsamı zorlanır.</summary>
    public Task<IReadOnlyList<RentalRow>> SearchAsync(RentalFilter filter, CancellationToken ct = default)
    {
        var scope = BranchScope.Effective(_currentUser);
        if (scope is not null) filter.Sube = scope; // operatör kendi şubesi dışına çıkamaz
        return _repository.SearchRentalRowsAsync(filter, ct);
    }

    public async Task<RentalContract?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _repository.FindRentalAsync(id, ct);
        if (r is not null) BranchScope.RequireInScope(_currentUser, r.CikisOfisi); // adversarial M3
        return r;
    }

    public async Task<Guid> CreateDirectAsync(BookingInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1: çift savunma (web + servis)
        BookingMath.Validate(input);
        TarihPolitikasi.KiraBaslangic(input.BasTar); // geçmişe açık (retroaktif); gelecek anti-typo ≤ +1yıl
        // 2. sürücü: aynı tenant'ta var olmalı (RLS çapraz-tenant'ı zaten keser; bu erken temiz hata) +
        // kendisiyle aynı olamaz.
        if (input.IkinciSurucuId is Guid ikinci)
        {
            if (ikinci == input.MusteriId)
                throw new ValidationException("2. sürücü müşteriyle aynı olamaz.");
            if (await customerRepository.FindAsync(ikinci, ct) is null)
                throw new ValidationException("2. sürücü (cari) bulunamadı.");
        }
        var pr = await _pricing.PriceAsync(input, ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife (tam teklif)

        // Yumuşak ön-kontrol (kullanıcı dostu hata); kesin garanti exclusion constraint.
        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        // Kur snapshot (denetim O5 — yalnız RAPORLAMA: CRM ciro TL-baz): TRY→1; FX→oluşturma anındaki kur
        // (SabitKur/TCMB). Kur çözülemezse FX kira REDDEDİLİR (ValidationException — fatura da aynı koşulda
        // reddederdi; sessiz 1:1 ciro yanlışlığı yerine erken temiz hata).
        var doviz = RentACar.Application.Kur.KurService.NormalizeKod(input.Doviz);
        var kurSnapshot = doviz == "TRY" ? 1m : await kurService.GetRateAsync(doviz, input.BasTar, ct: ct);

        var contract = new RentalContract
        {
            Durum = RentalStatus.Kirada,
            MusteriId = input.MusteriId,
            IkinciSurucuId = input.IkinciSurucuId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            CikisOfisi = input.CikisOfisi,
            DonusOfisi = input.DonusOfisi,
            Gun = pr.Gun,
            GunlukUcret = input.GunlukUcret,
            KmLimit = input.KmLimit,
            FazlaKmUcret = input.FazlaKmUcret,
            YakitBirimUcret = input.YakitBirimUcret,
            Tutar = pr.Tutar,
            GenelToplam = pr.Tutar,
            Tahsilat = 0m,
            Bakiye = pr.Tutar,
            // Tam teklif bileşenleri (bilgi/döküm; Tutar zaten net brütü — çift-sayım YOK, KURAL A).
            HediyeGun = pr.HediyeGun,
            IskontoTutar = pr.IskontoTutar,
            HaftaSonuFark = pr.HaftaSonuFark,
            FaturalananGun = pr.FaturalananGun,
            Provizyon = input.Provizyon,
            Depozito = input.Depozito,
            KomisyonOran = input.KomisyonOran,
            KomisyonTutar = input.KomisyonTutar,
            DropUcreti = input.DropUcreti,
            SonraOdeOran = input.SonraOdeOran,
            Aciklama = input.Aciklama,
            KiralamaTuru = input.KiralamaTuru,
            FaturalamaTipi = input.FaturalamaTipi,
            FiyatTuru = input.FiyatTuru,
            Doviz = input.Doviz,
            KurSnapshot = kurSnapshot
        };
        await _repository.CreateRentalAsync(contract, ct);
        return contract.Id;
    }

    /// <summary>Teslim: araç çıkışında KM/yakıt girişi. Araç odometresi (Vehicle.Km) AYNI transaction'da
    /// güncellenir (monoton: yalnız İLERİ; küçük girilirse araç km'si değişmez, kira yine kaydolur).</summary>
    public async Task<bool> DeliverAsync(Guid id, int cikisKm, int cikisYakit, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        return await _repository.UpdateRentalWithVehicleAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede teslim yapılır.");
            if (c.CikisKm is not null)
                throw new ValidationException("Araç zaten teslim edilmiş.");
            if (cikisKm < 0)
                throw new ValidationException("Çıkış KM negatif olamaz.");
            c.CikisKm = cikisKm;
            c.CikisYakit = cikisYakit;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => { v.Km = Math.Max(v.Km, cikisKm); v.Durum = VehicleStatus.Kirada; }, ct); // araç çıktı → Kirada
    }

    /// <summary>
    /// Dönüş: KM/yakıt/gerçek dönüş tarihi → fazla km, eksik yakıt, uzatma bedelleri;
    /// GenelToplam + Bakiye güncellenir; durum Tamamlandı (araç tekrar müsait olur).
    /// </summary>
    public async Task<bool> ReturnAsync(
        Guid id, int donusKm, int donusYakit, DateTimeOffset gercekDonus,
        int kmHediye = 0, string? bitisSebebi = null, Guid? teslimAlanPersonelId = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        if (kmHediye < 0)
            throw new ValidationException("KM hediye negatif olamaz.");
        // Üst sınır: int.MaxValue hediye taşma vektörüydü (adversarial BULGU 1); km-farkı guard'ıyla simetrik.
        if (kmHediye > 100_000)
            throw new ValidationException("KM hediye gerçekçi değil (100.000 üstü).");
        if (bitisSebebi is { } bs && bs.Trim().Length > 64)
            throw new ValidationException("Bitiş sebebi en fazla 64 karakter olabilir."); // varchar(64) — 500 yerine temiz red
        // Teslim alan personel: bu tenant'ta var olmalı (RLS zaten çapraz-tenant'ı keser; bu erken temiz hata).
        if (teslimAlanPersonelId is Guid pid &&
            await personelRepository.FindAsync(pid, ct) is null)
            throw new ValidationException("Teslim alan personel bulunamadı.");
        // Ek hizmet brütü dönüşte GenelToplam'da KORUNMALI (yoksa düşer).
        var ekHizmetToplam = (await _addOnRepository.ListForRentalAsync(id, ct)).Sum(a => a.Toplam);
        // Araç odometresi (Vehicle.Km) kira ile AYNI transaction'da güncellenir — km-bazlı bakım panosunu besler.
        return await _repository.UpdateRentalWithVehicleAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede dönüş yapılır.");
            if (c.CikisKm is null)
                throw new ValidationException("Önce teslim (çıkış KM) girilmelidir.");
            if (donusKm < c.CikisKm)
                throw new ValidationException("Dönüş KM, çıkış KM'den küçük olamaz.");
            // Sağduyu üst-sınırı: dönüş Tamamlandı'ya geçince geri alınamaz; parmak hatası (500000) aracın
            // odometresini kalıcı şişirir + bakım panosunu yanlış alarma sokar (adversarial inceleme 3c).
            // Düzeltme yolu: araç kartındaki Km alanı (VehicleService.UpdateAsync).
            if (donusKm - c.CikisKm.Value > 100_000)
                throw new ValidationException("KM farkı gerçekçi değil (tek kirada 100.000 km üstü). Dönüş KM'yi kontrol edin.");
            if (gercekDonus < c.BasTar)
                throw new ValidationException("Dönüş tarihi başlangıçtan önce olamaz.");

            var r = ReturnMath.Compute(c, donusKm, donusYakit, gercekDonus, kmHediye);
            c.DonusKm = donusKm;
            c.DonusYakit = donusYakit;
            c.GercekDonusTar = gercekDonus;
            c.KmHediye = kmHediye > 0 ? kmHediye : null;
            c.BitisSebebi = string.IsNullOrWhiteSpace(bitisSebebi) ? null : bitisSebebi.Trim();
            c.TeslimAlanPersonelId = teslimAlanPersonelId;
            c.FazlaKm = r.FazlaKm;
            c.FazlaKmBedeli = r.FazlaKmBedeli;
            c.EksikYakit = r.EksikYakit;
            c.YakitBedeli = r.YakitBedeli;
            c.UzatmaGun = r.UzatmaGun;
            c.UzatmaBedeli = r.UzatmaBedeli;
            // r.GenelToplam = baz brüt (ek hizmet hariç); ek hizmet brütünü ekle (RentalTotals ile tutarlı).
            c.GenelToplam = r.GenelToplam + ekHizmetToplam;
            c.Bakiye = c.GenelToplam - c.Tahsilat;
            c.Durum = RentalStatus.Tamamlandi;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => { v.Km = Math.Max(v.Km, donusKm); v.Durum = VehicleStatus.Musait; }, ct); // odometre monoton ileri; araç döndü → Musait (boşta)
    }

    /// <summary>
    /// Kira uzatma (roadmap I1): aktif (Kirada) sözleşmenin bitiş tarihini ileri iter. PLANLI uzatma baz kiranın
    /// parçasıdır → Gun + Tutar + GenelToplam + Bakiye artar; UzatmaGun/UzatmaBedeli'ne YAZILMAZ — o alanlar yalnız
    /// GEÇ DÖNÜŞ bedelidir (ReturnMath hesaplar). İkisine birden yazmak BaseGross'ta (Tutar + UzatmaBedeli) ÇİFT
    /// SAYIMDI (denetim K1: dönüş-öncesi fatura + ek-hizmet Recompute şişiyordu). DEFTER POSTLAMAZ; tahsilat/fatura
    /// ayrı. Uzatılan aralıkta başka aktif kira çakışması red.
    /// </summary>
    public async Task<bool> ExtendAsync(Guid id, DateTimeOffset yeniBitTar, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var c = await _repository.FindRentalAsync(id, ct);
        if (c is null) return false;
        BranchScope.RequireInScope(_currentUser, c.CikisOfisi); // adversarial M3
        if (c.Durum != RentalStatus.Kirada)
            throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
        if (yeniBitTar <= c.BitTar)
            throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");

        // Uzatılan aralıkta (kendisi hariç) başka aktif kira çakışması olmamalı.
        if (await _repository.HasOverlappingActiveRentalAsync(c.VehicleId, c.BasTar, yeniBitTar, id, ct))
            throw new AvailabilityConflictException();

        return await _repository.UpdateRentalAsync(id, x =>
        {
            if (x.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
            if (yeniBitTar <= x.BitTar)
                throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");

            var yeniGun = BookingMath.ComputeGun(x.BasTar, yeniBitTar);
            var ekGun = yeniGun - x.Gun;
            if (ekGun <= 0) throw new ValidationException("Uzatma en az 1 gün olmalıdır.");
            var ekBedel = ekGun * x.GunlukUcret;

            x.BitTar = yeniBitTar;
            x.Gun = yeniGun;
            x.Tutar += ekBedel;              // baz kira büyür (Tutar = Gun × GunlukUcret tutarlı; K1 fix)
            x.GenelToplam += ekBedel;
            x.Bakiye = x.GenelToplam - x.Tahsilat;
            // Tam teklif dökümü (hediye/iskonto/hafta-sonu) uzatma sonrası BAYAT kalır (Gun/Tutar değişti,
            // döküm create-anı değeri) → sözleşmede yanıltıcı olmasın diye TEMİZLENİR (adversarial L1; para yok).
            x.HediyeGun = null; x.FaturalananGun = null; x.IskontoTutar = null; x.HaftaSonuFark = null;
            x.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        return await _repository.UpdateRentalWithVehicleAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException($"Kira '{c.Durum}' durumundayken iptal edilemez.");
            c.Durum = RentalStatus.Iptal;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => v.Durum = VehicleStatus.Musait, ct); // iptal → araç serbest (boşta)
    }
}
