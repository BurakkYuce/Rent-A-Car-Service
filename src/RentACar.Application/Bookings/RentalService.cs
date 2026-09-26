using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.ReservationSources;
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
    RentACar.Application.Kur.ExchangeRateService exchangeRateService,
    RentACar.Application.Personnel.IPersonnelRepository personnelRepository,
    RentACar.Application.Customers.ICustomerRepository customerRepository,
    ITenantCache cache,
    FeeLineService feeLines,
    RentACar.Application.Finance.VatDefault vatDefault,
    RentACar.Application.FaturaDonemleri.InvoicePeriodPlanService periodPlan,
    RentACar.Application.Finance.ICashRepository cashRepository,
    RentACar.Application.Locations.ILocationRepository locationRepository,
    ReservationSourceRuleService sourceRule,
    RentACar.Application.Vehicles.IVehicleRepository vehicleRepository)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    // FAZ-49: rezervasyon kaynağı kural matrisi (uzatma yasağı / provizyon yok / km sınırsız /
    // drop yasağı / max gün). Saf gövde RezKaynakKural'da; burada yalnız GİRİŞ NOKTASI çağrıları.
    private readonly ReservationSourceRuleService _sourceRule = sourceRule;
    private readonly RentACar.Application.RentalAddOns.IRentalAddOnRepository _addOnRepository = addOnRepository;
    private readonly ITenantCache _cache = cache;

    /// <summary>
    /// Servis düzeyi yakıt çiti — TEK iç ölçek 0–12 (<see cref="FuelScale"/>, Karar (3) 2026-09-25). Harici JWT
    /// API yüzde gönderir ama sınırda 0–12'ye çevirir; buraya yüzde ulaşamaz. Çit negatif değeri ve int taşmasını
    /// (int.MinValue → sahte milyarlık eksik-yakıt bedeli) keser; eksik yakıt en fazla 12 birim olur.
    /// </summary>
    public const int MaxFuel = FuelScale.Max;

    private static void FuelRange(int fuel, string label)
    {
        if (fuel is < 0 or > MaxFuel)
            throw new ValidationException($"{label} yakıt 0-{MaxFuel} aralığında olmalıdır.");
    }

    // Kira, araç Durum'unu değiştirdiğinde (Teslim→Kirada / Dönüş→Musait / İptal→Musait) VehicleService'in
    // "vehicles" cache'ini invalidate et → boş-araç dropdown/liste bayat kalmasın (latent cache tutarsızlığı fix).
    private async Task<bool> Inv(Task<bool> op)
    {
        var ok = await op;
        _cache.Invalidate(RentACar.Application.Vehicles.VehicleService.CacheKey);
        return ok;
    }

    public Task<IReadOnlyList<RentalContract>> ListAsync(CancellationToken ct = default)
        => _repository.ListRentalsAsync(BranchScope.EffectiveFilter(_currentUser), ct); // C4

    /// <summary>Kira listesi: filtre + müşteri/araç/fatura-durumu. Rol bazlı şube kapsamı zorlanır.</summary>
    public Task<IReadOnlyList<RentalRow>> SearchAsync(RentalFilter filter, CancellationToken ct = default)
    {
        filter.Kapsam = BranchScope.EffectiveFilter(_currentUser); // C4: FK-farkındalı (UI Sube ayrı)
        return _repository.SearchRentalRowsAsync(filter, ct);
    }

    public async Task<RentalContract?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _repository.FindRentalAsync(id, ct);
        if (r is not null) BranchScope.RequireInScope(_currentUser, r.CikisSubeId, r.CikisOfisi); // adversarial M3
        return r;
    }

    public async Task<Guid> CreateDirectAsync(BookingInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1: çift savunma (web + servis)
        BookingMath.Validate(input);
        DatePolicy.RentalStart(input.BasTar); // geçmişe açık (retroaktif); gelecek anti-typo ≤ +1yıl
        DatePolicy.RentalEnd(input.BasTar, input.BitTar); // F4.1 L4: süre ≤ 5 yıl (9999 → taşma/bloke)

        // F4.1 adversarial M1: çıkış ofisi GİRİŞ NOKTASINDA şube kapsamından geçer (UpdateOpenAsync'teki
        // desen: ofis → Location → türetilmiş şube). Önceden operatör başka şubeye kira yazabiliyor (o
        // şubenin aracını bloke edip kaydı kendisi de göremiyordu) ya da ofissiz "yetim" kira açabiliyordu.
        var pickupOfficeInput = Lim(input.CikisOfisi, 64, "Çıkış ofisi");
        if (pickupOfficeInput is null && !BranchScope.EffectiveFilter(_currentUser).Unrestricted)
            throw new ValidationException("Çıkış ofisi zorunludur (şubeye bağlı kullanıcı kendi şubesinin ofisini seçmelidir).");
        var pickupBranchId = pickupOfficeInput is null
            ? null : (await locationRepository.FindByNameAsync(pickupOfficeInput, ct))?.SubeId;
        BranchScope.RequireInScope(_currentUser, pickupBranchId, pickupOfficeInput);

        // 2. sürücü: aynı tenant'ta var olmalı (RLS çapraz-tenant'ı zaten keser; bu erken temiz hata) +
        // kendisiyle aynı olamaz.
        if (input.IkinciSurucuId is Guid second)
        {
            if (second == input.MusteriId)
                throw new ValidationException("2. sürücü müşteriyle aynı olamaz.");
            if (await customerRepository.FindAsync(second, ct) is null)
                throw new ValidationException("2. sürücü (cari) bulunamadı.");
        }
        // FAZ-47: kayıtlı (FK) ve misafir (serbest metin) 2. sürücü BİRLİKTE olamaz.
        SecondDriverSinglePathGuard(input.IkinciSurucuId, input.IkinciSurucuSerbestAd, input.IkinciSurucuSerbestSoyad,
            input.IkinciSurucuSerbestTel, input.IkinciSurucuSerbestEhliyetSinifi);
        // FAZ 4.4 RİSK GUARD'ı (giriş noktasında — tarih-politikası dersi): cari RiskLimiti tanımlıysa
        // (>0) ve mevcut borç bakiyesi limiti AŞIYORSA kira ancak Yönetici/Admin RiskOnay'ıyla açılır.
        // Onay kutusunu Operatör işaretleyemez (rol doğrulaması burada — UI'daki gizleme yeterli değil).
        // Varlık kontrolü (DEVIR §6 Low, harici RentalsApi): Rentals.MusteriId/VehicleId'de bileşik FK yok →
        // hiç olmayan ya da BAŞKA KİRACININ kimliğiyle kira yazılabiliyordu. Kontrol artık servis GİRİŞİNDE —
        // harici API, /api/ui ve Blazor formu aynı kuraldan geçer (uçlardaki kopyalar kaldırıldı). RLS + tenant
        // query filter kapsamlı FindAsync: yabancı kiracının kaydı "yok" görünür (varlık sızmaz) → 400.
        // Kalıcı çözüm (bileşik FK) ayrı iş. Kural tek yerde: BookingPartyCheck (rezervasyon/teklif de kullanır).
        var customer = await BookingPartyCheck.RequireAsync(
            customerRepository, vehicleRepository, input.MusteriId, input.VehicleId, ct);
        if (customer is { RiskLimiti: > 0m })
        {
            var balance = await cashRepository.GetAccountBalanceAsync(input.MusteriId, ct);
            if (balance > customer.RiskLimiti)
            {
                if (!input.RiskOnay)
                    throw new ValidationException(
                        $"Risk limiti aşıldı (bakiye {balance:N2} > limit {customer.RiskLimiti:N2}) — Yönetici onayı gerekir.");
                if (_currentUser.Role is not (UserRole.Admin or UserRole.Yonetici))
                    throw new ValidationException("Risk onayı yalnız Yönetici/Admin tarafından verilebilir.");
            }
        }

        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA (fiyatlamadan önce): kaynağın gün sınırı + drop
        // yasağı reddeder, km sınırsızlığı KmLimit'i 0'a sabitler.
        var resolvedSourceRule = await _sourceRule.ResolveAsync(input.Kaynak, ct);
        ReservationSourceRule.MaxDaysGuard(resolvedSourceRule, BookingMath.ComputeDays(input.BasTar, input.BitTar));
        ReservationSourceRule.DropGuard(resolvedSourceRule, input.CikisOfisi, input.DonusOfisi);
        var sourceKmLimit = ReservationSourceRule.ApplyKmLimit(resolvedSourceRule, input.KmLimit);

        var pr = await _pricing.PriceAsync(input, ct: ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife (tam teklif)
        var defaultVat = await vatDefault.RateAsync(ct); // FAZ 3.A6 (net-mod çiti gross-up oranıyla karşılaştırır)

        // Yumuşak ön-kontrol (kullanıcı dostu hata); kesin garanti exclusion constraint.
        if (await _repository.HasOverlappingActiveRentalAsync(input.VehicleId, input.BasTar, input.BitTar, null, ct))
            throw new AvailabilityConflictException();

        // Kur snapshot (denetim O5 — yalnız RAPORLAMA: CRM ciro TL-baz): TRY→1; FX→oluşturma anındaki kur
        // (SabitKur/TCMB). Kur çözülemezse FX kira REDDEDİLİR (ValidationException — fatura da aynı koşulda
        // reddederdi; sessiz 1:1 ciro yanlışlığı yerine erken temiz hata).
        var currency = RentACar.Application.Kur.ExchangeRateService.NormalizeCode(input.Doviz);
        var exchangeRateSnapshot = currency == "TRY" ? 1m : await exchangeRateService.GetRateAsync(currency, input.BasTar, ct: ct);

        var contract = new RentalContract
        {
            Durum = RentalStatus.Kirada,
            MusteriId = input.MusteriId,
            IkinciSurucuId = input.IkinciSurucuId,
            VehicleId = input.VehicleId,
            BasTar = input.BasTar,
            BitTar = input.BitTar,
            // Trim+boş→null: UpdateOpenAsync'in Tamamlandi "ofis değişti mi" Ordinal karşılaştırmasıyla
            // tutarlı saklama (adversarial Low: boşluklu kayıt yanlış red üretiyordu).
            CikisOfisi = Lim(input.CikisOfisi, 64, "Çıkış ofisi"),
            DonusOfisi = Lim(input.DonusOfisi, 64, "Dönüş ofisi"),
            Gun = pr.Gun,
            GunlukUcret = input.GunlukUcret,
            KmLimit = sourceKmLimit,          // FAZ-49: KmSinirsiz kaynakta 0'a (sınırsız) sabitlenir
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
            Aciklama = Lim(input.Aciklama, 1024, "Açıklama"),
            KiralamaTuru = input.KiralamaTuru,
            FaturalamaTipi = input.FaturalamaTipi,
            FiyatTuru = input.FiyatTuru,
            Doviz = input.Doviz,
            KurSnapshot = exchangeRateSnapshot,
            KdvOranSnapshot = pr.KdvOranSnapshot, // FAZ 3.A6: net-mod gross-up oranı (fatura aynı orandan ayrıştırır)
            // Kira formu detay alanları (bilgi amaçlı; Kaynak daha önce input'ta olup MAP EDİLMİYORDU — parite fix)
            Kaynak = Lim(input.Kaynak, 64, "Kaynak"),
            KampanyaKodu = Lim(input.KampanyaKodu, 64, "Kampanya kodu"),
            DonemselFaturalama = input.DonemselFaturalama, // FAZ 4.2-B4 (job kapısı; kira-başına opt-in)
            UyariAciklama = Lim(input.UyariAciklama, 512, "Uyarı açıklama"),
            OzelFaturaAciklama = Lim(input.OzelFaturaAciklama, 512, "Özel fatura açıklaması"),
            FaturaListesindeGizle = input.FaturaListesindeGizle,
            UcusNo = Lim(input.UcusNo, 32, "Uçuş no"),
            ProvizyonNo = Lim(input.ProvizyonNo, 64, "Provizyon no"),
            ProvizyonTarih = input.ProvizyonTarih,
            OnayKodu = Lim(input.OnayKodu, 64, "Onay kodu"),
            FirmaKodu = Lim(input.FirmaKodu, 64, "Firma kodu"),
            ProjeAdi = Lim(input.ProjeAdi, 128, "Proje adı"),
            OzelKod = Lim(input.OzelKod, 64, "Özel kod"),
            OzelKdvOran = InputCustomVat(input.FiyatTuru, input.OzelKdvOran, pr.KdvOranSnapshot ?? defaultVat), // FAZ 1.4+A6 (net-mod çiti gross-up oranıyla)
            DamgaVergisi = TaxStamp(input.DamgaVergisi),   // FAZ 1.4
            TalepTuru = Lim(input.TalepTuru, 64, "Talep türü"),
            GeldigiBirim = Lim(input.GeldigiBirim, 64, "Geldiği birim"),
            KefilBilgisi = Lim(input.KefilBilgisi, 512, "Kefil bilgisi"),
            AssistFirma = Lim(input.AssistFirma, 128, "Assist firma"),
            OzelSoforBilgisi = Lim(input.OzelSoforBilgisi, 512, "Özel şoför bilgisi"),
            EkKosullar = Lim(input.EkKosullar, 2048, "Ek koşullar"),
            BelgeSablonId = input.BelgeSablonId,
            OpsiyonNet = input.OpsiyonNet,
            OpsiyonGun = input.OpsiyonGun,
            RiskOnay = input.RiskOnay,
            ManuelFindexPuan = ValidFindex(input.ManuelFindexPuan),
            KabisCikis = input.KabisCikis,
            KabisDonus = input.KabisDonus,
            OtomatikUzat = input.OtomatikUzat,
            AksYedekAnahtarCikis = input.AksYedekAnahtarCikis,
            AksStepneCikis = input.AksStepneCikis,
            AksZincirCikis = input.AksZincirCikis,
            AksIlkYardimCikis = input.AksIlkYardimCikis,
            AksLastikCikis = Lim(input.AksLastikCikis, 64, "Lastik durumu (çıkış)"),
            // FAZ-47 — bilgi alanları (deftere/bakiyeye/fiyata GİRMEZ; kırılgan regresyon testiyle kilitli)
            OdemeSekli = Lim(input.OdemeSekli, 64, "Ödeme şekli"),
            IkinciSurucuSerbestAd = Lim(input.IkinciSurucuSerbestAd, 64, "2. sürücü adı"),
            IkinciSurucuSerbestSoyad = Lim(input.IkinciSurucuSerbestSoyad, 64, "2. sürücü soyadı"),
            IkinciSurucuSerbestTel = Lim(input.IkinciSurucuSerbestTel, 32, "2. sürücü telefonu"),
            IkinciSurucuSerbestEhliyetSinifi = Lim(input.IkinciSurucuSerbestEhliyetSinifi, 16, "2. sürücü ehliyet sınıfı")
        };
        await _repository.CreateRentalAsync(contract, ct);
        // FAZ 3.A3a: sistem ücret satırları (genç/ek sürücü) — RentalAddOn olarak (KURAL A: BaseGross'a
        // dokunmaz; GenelToplam add-on mekanizmasıyla güncellenir). FX kirada sessizce atlanır.
        await feeLines.ApplyContractFeesAsync(contract.Id, ct);
        // FAZ 4.2-B1: uygun (uzun/aylık) kirada fatura dönem planı kurulur (parasız; idempotent).
        await periodPlan.EnsurePlanAsync(contract.Id, ct);
        return contract.Id;
    }

    /// <summary>
    /// Açık kira alan güncelleme (mega-form "Kaydet" — edit modu). Whitelist <see cref="RentalUpdateInput"/>
    /// TİPİYLE zorlanır: para/tarih/durum alanları input'ta yoktur, form ne gönderirse göndersin değişemez
    /// (tarih = ExtendAsync, fiyat farkı = fark faturası). Kirada → operasyonel + bilgi alanları;
    /// Tamamlandi → yalnız bilgi alanları (aşım parametreleri/2. sürücü/ofisler DONMUŞ — ReturnMath koştu);
    /// Iptal → red. Ofis değişiminde HEM mevcut HEM yeni ofis şube kapsamında olmalı.
    /// </summary>
    public async Task<bool> UpdateOpenAsync(Guid id, RentalUpdateInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var existing = await _repository.FindRentalAsync(id, ct);
        if (existing is null) return false;
        BranchScope.RequireInScope(_currentUser, existing.CikisSubeId, existing.CikisOfisi);
        if (existing.Durum == RentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş kira güncellenemez.");

        var pickupOffice = Lim(input.CikisOfisi, 64, "Çıkış ofisi");
        var returnOffice = Lim(input.DonusOfisi, 64, "Dönüş ofisi");
        if (!string.Equals(pickupOffice ?? "", existing.CikisOfisi ?? "", StringComparison.Ordinal))
        {
            // C4: HEDEF ofisin türetilmiş şubesiyle kontrol — operatör kirayı kendi ŞUBESİNİN başka
            // ofisine taşıyabilir (widening, testli); kapsam DIŞI şubenin ofisine taşıyamaz.
            var targetBranchId = string.IsNullOrWhiteSpace(pickupOffice)
                ? null : (await locationRepository.FindByNameAsync(pickupOffice!, ct))?.SubeId;
            BranchScope.RequireInScope(_currentUser, targetBranchId, pickupOffice);
        }
        // FAZ-47: kayıtlı (FK) + misafir (serbest metin) 2. sürücü BİRLİKTE olamaz — guard KODDA
        // (form "iki bloktan birini doldur" diye yazdığı için değil).
        SecondDriverSinglePathGuard(input.IkinciSurucuId, input.IkinciSurucuSerbestAd, input.IkinciSurucuSerbestSoyad,
            input.IkinciSurucuSerbestTel, input.IkinciSurucuSerbestEhliyetSinifi);
        if (input.KmLimit < 0)
            throw new ValidationException("KM limit negatif olamaz.");
        if (input.FazlaKmUcret < 0m || input.YakitBirimUcret < 0m)
            throw new ValidationException("Aşım ücretleri negatif olamaz.");

        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA. Kaynak bu formda değişebildiğinden sonucu
        // tanımlayan kurallar YENİ kaynağa göre uygulanır. Drop yasağı YALNIZ ofis ya da kaynak
        // GERÇEKTEN değiştiyse denetlenir: kural kaynağa sonradan konabilir ve mevcut drop'lu
        // sözleşmenin not düzenlemesini de reddetseydik kayıt hiç güncellenemezdi (tarih-politikası
        // dersi — guard yeni ihlali keser, yaşlanmış kaydı kilitlemez).
        var sourceText = Lim(input.Kaynak, 64, "Kaynak");
        var resolvedSourceRule = await _sourceRule.ResolveAsync(sourceText, ct);
        var sourceChanged = !string.Equals(sourceText ?? "", existing.Kaynak ?? "", StringComparison.OrdinalIgnoreCase);
        var officeChanged =
            !string.Equals(pickupOffice ?? "", existing.CikisOfisi ?? "", StringComparison.Ordinal)
            || !string.Equals(returnOffice ?? "", existing.DonusOfisi ?? "", StringComparison.Ordinal);
        if (officeChanged || sourceChanged)
            ReservationSourceRule.DropGuard(resolvedSourceRule, pickupOffice, returnOffice);
        // Km sabitlemesi yalnız AÇIK (Kirada) sözleşmenin yazma yolunda uygulanır. Tamamlanmış
        // kirada aşım parametreleri zaten DONMUŞ; sabitlemeyi oradaki "değişti mi" karşılaştırmasına
        // sokmak, kural sonradan konduğunda kaydı tümüyle düzenlenemez yapardı.
        var kmLimit = ReservationSourceRule.ApplyKmLimit(resolvedSourceRule, input.KmLimit);

        if (existing.Durum == RentalStatus.Tamamlandi)
        {
            if (input.KmLimit != existing.KmLimit || input.FazlaKmUcret != existing.FazlaKmUcret
                || input.YakitBirimUcret != existing.YakitBirimUcret)
                throw new ValidationException("Tamamlanmış kirada aşım parametreleri değiştirilemez (dönüş hesabı yapıldı).");
            if (input.IkinciSurucuId != existing.IkinciSurucuId)
                throw new ValidationException("Tamamlanmış kirada 2. sürücü değiştirilemez.");
            if (!string.Equals(pickupOffice ?? "", existing.CikisOfisi ?? "", StringComparison.Ordinal)
                || !string.Equals(returnOffice ?? "", existing.DonusOfisi ?? "", StringComparison.Ordinal))
                throw new ValidationException("Tamamlanmış kirada ofisler değiştirilemez.");
            // A3b-B3: DropUcreti artık PARA-ETKİLİ manuel override — tamamlanmış kirada değişirse
            // alan/satır ıraksar (satır senkronu yalnız Kirada çalışır) → dondur.
            if (input.DropUcreti != existing.DropUcreti)
                throw new ValidationException("Tamamlanmış kirada drop ücreti değiştirilemez.");
        }
        // A3b-B3: FATURALANMIŞ kirada da dondur — satır senkronu defter snapshot'ına dokunmaz;
        // alan değişip satır değişmezse sözleşme belgesi ile tahsilat sessizce ıraksardı.
        else if (input.DropUcreti != existing.DropUcreti && await _addOnRepository.IsRentalInvoicedAsync(id, ct))
            throw new ValidationException("Faturalanmış kirada drop ücreti değiştirilemez (ücret satırı defter snapshot'ında).");
        if (input.DropUcreti is < 0m)
            throw new ValidationException("Drop ücreti negatif olamaz."); // A3b-B4 (update yolu)
        else if (input.IkinciSurucuId is Guid second && second != existing.IkinciSurucuId)
        {
            if (second == existing.MusteriId)
                throw new ValidationException("2. sürücü müşteriyle aynı olamaz.");
            if (await customerRepository.FindAsync(second, ct) is null)
                throw new ValidationException("2. sürücü (cari) bulunamadı.");
        }

        // FAZ 3.A6 adversarial B1: net-mod çiti güncellemede SNAPSHOT'la karşılaştırır; snapshot NULL =
        // pre-A6 kira = gross-up KESİNLİKLE 0.20 idi → fallback FATURA ÇİTİYLE AYNI (0.20) — tenant-güncel
        // orana düşmek, çitin kabul ettiği tek değerin faturayı kilitlemesine yol açıyordu.
        var currentVatDefault = existing.KdvOranSnapshot ?? RentACar.Application.Finance.VatMath.DefaultRate;
        // F4.3 adversarial F2: BeklenenSurum doluysa (yeni arayüz) sürüm kilit ALTINDA denetlenir — bayat tam
        // değiştirme başka oturumun değişikliğini geri alamaz. Blazor null gönderir (davranış aynı).
        var ok = await _repository.UpdateRentalAsync(id, input.BeklenenSurum, c =>
        {
            // TX içinde yeniden doğrula (ön-kontrol ile arasında durum değişmiş olabilir).
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi);
            if (c.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kira güncellenemez.");
            if (c.Durum == RentalStatus.Kirada)
            {
                c.CikisOfisi = pickupOffice;
                c.DonusOfisi = returnOffice;
                c.IkinciSurucuId = input.IkinciSurucuId;
                c.KmLimit = kmLimit;          // FAZ-49: KmSinirsiz kaynakta 0'a (sınırsız) sabitlenir
                c.FazlaKmUcret = input.FazlaKmUcret;
                c.YakitBirimUcret = input.YakitBirimUcret;
            }
            // Bilgi alanları — her iki durumda da (Kirada/Tamamlandi) serbest.
            c.Aciklama = Lim(input.Aciklama, 1024, "Açıklama");
            c.Kaynak = sourceText;           // FAZ-49: kural çözümü ile AYNI metin (ıraksama olmaz)
            c.KiralamaTuru = Lim(input.KiralamaTuru, 64, "Kiralama türü");
            c.DonemselFaturalama = input.DonemselFaturalama; // FAZ 4.2-B4
            c.FaturalamaTipi = Lim(input.FaturalamaTipi, 64, "Faturalama tipi");
            c.Provizyon = input.Provizyon;
            c.Depozito = input.Depozito;
            c.KomisyonOran = input.KomisyonOran;
            c.KomisyonTutar = input.KomisyonTutar;
            c.DropUcreti = input.DropUcreti;
            c.SonraOdeOran = input.SonraOdeOran;
            c.UyariAciklama = Lim(input.UyariAciklama, 512, "Uyarı açıklama");
            c.OzelFaturaAciklama = Lim(input.OzelFaturaAciklama, 512, "Özel fatura açıklaması");
            c.FaturaListesindeGizle = input.FaturaListesindeGizle;
            c.UcusNo = Lim(input.UcusNo, 32, "Uçuş no");
            c.ProvizyonNo = Lim(input.ProvizyonNo, 64, "Provizyon no");
            c.ProvizyonTarih = input.ProvizyonTarih;
            c.OnayKodu = Lim(input.OnayKodu, 64, "Onay kodu");
            c.FirmaKodu = Lim(input.FirmaKodu, 64, "Firma kodu");
            c.ProjeAdi = Lim(input.ProjeAdi, 128, "Proje adı");
            c.OzelKod = Lim(input.OzelKod, 64, "Özel kod");
            c.OzelKdvOran = InputCustomVat(c.FiyatTuru, input.OzelKdvOran, currentVatDefault); // FAZ 1.4+A6
            c.DamgaVergisi = TaxStamp(input.DamgaVergisi); // FAZ 1.4
            c.TalepTuru = Lim(input.TalepTuru, 64, "Talep türü");
            c.GeldigiBirim = Lim(input.GeldigiBirim, 64, "Geldiği birim");
            c.KefilBilgisi = Lim(input.KefilBilgisi, 512, "Kefil bilgisi");
            c.AssistFirma = Lim(input.AssistFirma, 128, "Assist firma");
            c.OzelSoforBilgisi = Lim(input.OzelSoforBilgisi, 512, "Özel şoför bilgisi");
            c.EkKosullar = Lim(input.EkKosullar, 2048, "Ek koşullar");
            c.BelgeSablonId = input.BelgeSablonId; // bilgi/sunum alanı (Kirada/Tamamlandi serbest)
            c.OpsiyonNet = input.OpsiyonNet;
            c.OpsiyonGun = input.OpsiyonGun;
            c.ManuelFindexPuan = ValidFindex(input.ManuelFindexPuan);
            c.KabisCikis = input.KabisCikis;
            c.KabisDonus = input.KabisDonus;
            c.OtomatikUzat = input.OtomatikUzat;
            c.AksYedekAnahtarCikis = input.AksYedekAnahtarCikis;
            c.AksYedekAnahtarDonus = input.AksYedekAnahtarDonus;
            c.AksStepneCikis = input.AksStepneCikis;
            c.AksStepneDonus = input.AksStepneDonus;
            c.AksZincirCikis = input.AksZincirCikis;
            c.AksZincirDonus = input.AksZincirDonus;
            c.AksIlkYardimCikis = input.AksIlkYardimCikis;
            c.AksIlkYardimDonus = input.AksIlkYardimDonus;
            c.AksLastikCikis = Lim(input.AksLastikCikis, 64, "Lastik durumu (çıkış)");
            c.AksLastikDonus = Lim(input.AksLastikDonus, 64, "Lastik durumu (dönüş)");
            // ---- FAZ-47 bilgi alanları (Kirada/Tamamlandi serbest; para/defter hesabına GİRMEZ) ----
            // Misafir 2. sürücü Tamamlandi'da da düzeltilebilir: FK'lı 2. sürücünün aksine ek-sürücü
            // ÜCRET satırını (FeeLineService) tetiklemez — yani donduracak bir para etkisi yoktur.
            c.TeslimEdenPersonelId = input.TeslimEdenPersonelId;
            c.OdemeSekli = Lim(input.OdemeSekli, 64, "Ödeme şekli");
            c.IkinciSurucuSerbestAd = Lim(input.IkinciSurucuSerbestAd, 64, "2. sürücü adı");
            c.IkinciSurucuSerbestSoyad = Lim(input.IkinciSurucuSerbestSoyad, 64, "2. sürücü soyadı");
            c.IkinciSurucuSerbestTel = Lim(input.IkinciSurucuSerbestTel, 32, "2. sürücü telefonu");
            c.IkinciSurucuSerbestEhliyetSinifi = Lim(input.IkinciSurucuSerbestEhliyetSinifi, 16, "2. sürücü ehliyet sınıfı");
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        // FAZ 3.A3a adversarial B2: 2. sürücü sonradan eklendi/kaldırıldıysa sistem ücret satırları
        // sözleşmenin GÜNCEL hâline eşitlenir (yalnız Kirada + faturalanmamışken; içeride kontrol).
        if (ok) await feeLines.SyncContractFeesAsync(id, ct);
        return ok;
    }

    /// <summary>FAZ 4.1 — MANUEL provizyon alma (Yok→Alindi). POS'suz kayıt: IPosService ÇAĞRILMAZ,
    /// kart verisi sisteme girmez (PCI). Deftere YAZMAZ — bilgi/iz; gerçek tahsilat ayrı akış.
    /// Fiyat sekmesindeki Provizyon tutarı girilmeden alınamaz (neyin bloke edildiği belli olsun).</summary>
    public async Task<bool> TakePreAuthAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA: kaynağı "provizyon yok" diyen sözleşmede bloke
        // alınamaz. Kaynak çözümü async olduğundan repo lambda'sının İÇİNE konamaz; ön-okuma ile
        // burada yapılır (durum/şube doğrulaması lambda içinde aynen sürüyor).
        var existing = await _repository.FindRentalAsync(id, ct);
        if (existing is null) return false;
        ReservationSourceRule.PreAuthGuard(await _sourceRule.ResolveAsync(existing.Kaynak, ct));
        return await _repository.UpdateRentalAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi);
            if (c.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kirada provizyon işlemi yapılamaz.");
            if (c.ProvizyonDurum != PreAuthStatus.Yok)
                throw new ValidationException($"Provizyon zaten '{c.ProvizyonDurum}' durumunda (yalnız Yok → Alındı).");
            if (c.Provizyon is not > 0m)
                throw new ValidationException("Önce Fiyat sekmesinde provizyon (bloke) tutarı girilmelidir.");
            c.ProvizyonDurum = PreAuthStatus.Alindi;
            c.ProvizyonTarih ??= DateTimeOffset.UtcNow;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>FAZ 4.1 — provizyon kapama (Alindi→Kapandi) veya serbest bırakma (iade=true →
    /// IadeEdildi, kapama tutarı 0). Kapama tutarı verilmezse bloke tutarın tamamı bilgi olarak yazılır.</summary>
    public async Task<bool> ClosePreAuthAsync(
        Guid id, decimal? closingAmount = null, bool refund = false, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (closingAmount is < 0m)
            throw new ValidationException("Kapama tutarı negatif olamaz.");
        return await _repository.UpdateRentalAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi);
            if (c.ProvizyonDurum != PreAuthStatus.Alindi)
                throw new ValidationException($"Yalnız 'Alındı' durumundaki provizyon kapatılabilir (mevcut: {c.ProvizyonDurum}).");
            c.ProvizyonDurum = refund ? PreAuthStatus.IadeEdildi : PreAuthStatus.Kapandi;
            c.ProvizyonKapamaTarih = DateTimeOffset.UtcNow;
            c.ProvizyonKapamaTutar = refund ? 0m : (closingAmount ?? c.Provizyon);
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// Dönüş CANLI ÖNİZLEMESİ (mega-form Dönüş sekmesi; GET /kiralar/donus-hesapla). GERÇEK motor
    /// (ReturnMath.Compute) + ek hizmet brütü — PERSIST ETMEZ, durum değiştirmez. ReturnAsync ile aynı
    /// guard'lar NAZİK hataya çevrilir (Ok=false — kullanıcı yazarken 500/exception yok). Şube kapsamı zorlanır.
    /// </summary>
    public async Task<KiraDonusOnizleme> PreviewReturnAsync(
        Guid id, int returnKm, int returnFuel, DateTimeOffset actualReturn, int freeKm = 0,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var c = await _repository.FindRentalAsync(id, ct);
        if (c is null) return KiraDonusOnizleme.Invalid("Kira bulunamadı.");
        BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // kapsam: sızıntı yok (GetAsync ile aynı)

        if (c.Durum != RentalStatus.Kirada) return KiraDonusOnizleme.Invalid("Yalnız aktif (Kirada) sözleşmede dönüş hesaplanır.");
        if (c.CikisKm is null) return KiraDonusOnizleme.Invalid("Önce teslim (çıkış KM) girilmelidir.");
        if (returnKm < c.CikisKm) return KiraDonusOnizleme.Invalid("Dönüş KM, çıkış KM'den küçük olamaz.");
        if (returnKm - c.CikisKm.Value > 100_000) return KiraDonusOnizleme.Invalid("KM farkı gerçekçi değil (100.000 üstü).");
        if (actualReturn < c.BasTar) return KiraDonusOnizleme.Invalid("Dönüş tarihi başlangıçtan önce olamaz.");
        if (freeKm is < 0 or > 100_000) return KiraDonusOnizleme.Invalid("KM hediye 0-100.000 aralığında olmalıdır.");
        if (returnFuel is < 0 or > MaxFuel) return KiraDonusOnizleme.Invalid($"Dönüş yakıt 0-{MaxFuel} aralığında olmalıdır."); // F4.1 L6
        if (actualReturn > DateTimeOffset.UtcNow.AddYears(1)) return KiraDonusOnizleme.Invalid("Gerçek dönüş tarihi en fazla 1 yıl ileri olabilir."); // F4.1 L4

        var r = ReturnMath.Compute(c, returnKm, returnFuel, actualReturn, freeKm);
        var addOnTotal = (await _addOnRepository.ListForRentalAsync(id, ct)).Sum(a => a.Toplam);
        var newGrandTotal = r.GenelToplam + addOnTotal; // ReturnAsync ile birebir aynı formül
        return new KiraDonusOnizleme(
            Ok: true, Hata: null,
            KullanilanKm: r.KullanilanKm, FazlaKm: r.FazlaKm, FazlaKmBedeli: r.FazlaKmBedeli,
            EksikYakit: r.EksikYakit, YakitBedeli: r.YakitBedeli,
            UzatmaGun: r.UzatmaGun, UzatmaBedeli: r.UzatmaBedeli,
            EkHizmetToplam: addOnTotal, YeniGenelToplam: newGrandTotal,
            Kalan: newGrandTotal - c.Tahsilat);
    }

    /// <summary>Serbest metin alanı: trim + boş→null + aşımda temiz red (DB varchar taşması 500 yerine).</summary>
    /// <summary>FAZ 1.4: özel KDV oranı kesir 0..1 (aksi red).</summary>
    private static decimal? TaxRate(decimal? rate)
    {
        if (rate is { } o && (o < 0m || o > 1m))
            throw new ValidationException("Özel KDV oranı 0 ile 1 arasında olmalıdır (kesir, ör. 0.10).");
        return rate;
    }

    /// <summary>FAZ 1.4 (adversarial D — guard'ı GİRİŞ noktasına koy dersi): NET fiyat modlu kirada
    /// varsayılan-dışı özel KDV çelişkisi girişte reddedilir — aksi halde dönüş fark-faturası kesilene
    /// dek kilitlenir (fatura-anı guard'ı savunma-derinliği olarak kalır).</summary>
    private static decimal? InputCustomVat(string? priceType, decimal? customVatRate, decimal validDefault)
    {
        var rate = TaxRate(customVatRate);
        CustomVatNetModeFence(priceType, rate, validDefault);
        return rate;
    }

    /// <summary>FAZ 3.A6: karşılaştırma SABİT 0.20 yerine GEÇERLİ varsayılanla (create'te gross-up
    /// oranı / update'te snapshot ?? tenant varsayılanı) — tenant oranı %10 iken %10'luk özel oran
    /// net-modda çelişki DEĞİLDİR.</summary>
    private static void CustomVatNetModeFence(string? priceType, decimal? customVatRate, decimal validDefault)
    {
        var netMod = string.Equals(priceType?.Trim(), "Günlük", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(priceType?.Trim(), "Toplam", StringComparison.OrdinalIgnoreCase);
        if (netMod && customVatRate is { } o && o != validDefault)
            throw new ValidationException(
                $"Net fiyat modlu kirada özel KDV oranı kullanılamaz (fiyat %{validDefault * 100:0.##} net üstünden hesaplanır).");
    }

    /// <summary>FAZ 1.4: damga vergisi negatif olamaz.</summary>
    private static decimal? TaxStamp(decimal? stamp)
    {
        if (stamp is < 0m)
            throw new ValidationException("Damga vergisi negatif olamaz.");
        return stamp;
    }

    // FAZ-48: kural BookingMath.Kirp'e taşındı (rezervasyon aynı alanları taşıyor); davranış AYNI.
    private static string? Lim(string? s, int max, string alan) => BookingMath.Clamp(s, max, alan);

    private static int? ValidFindex(int? score)
        => score is < 0 ? throw new ValidationException("Findeks puanı negatif olamaz.") : score;

    /// <summary>
    /// FAZ-47 — 2. sürücü TEK YOL kuralı: ya kayıtlı cari (<c>IkinciSurucuId</c>) ya misafir serbest metni.
    /// İkisi birden dolu gelirse GÜRÜLTÜLÜ RED — sessizce birini seçmek "sözleşmedeki 2. sürücü kim"
    /// sorusunu belirsiz bırakırdı (ve FK'lı sürücü ek-sürücü ÜCRETİ üretirken serbest metin üretmez;
    /// iki kayıt bir arada, ücret alınmayan görünmez bir sürücü demek olurdu).
    /// </summary>
    private static void SecondDriverSinglePathGuard(Guid? fk, params string?[] freeFields)
    {
        if (fk is null) return;
        if (freeFields.Any(s => !string.IsNullOrWhiteSpace(s)))
            throw new ValidationException(
                "2. sürücü ya kayıtlı cariden seçilir ya da misafir bilgileri girilir — ikisi birden doldurulamaz.");
    }

    /// <summary>Teslim: araç çıkışında KM/yakıt girişi. Araç odometresi (Vehicle.Km) AYNI transaction'da
    /// güncellenir (monoton: yalnız İLERİ; küçük girilirse araç km'si değişmez, kira yine kaydolur).</summary>
    public async Task<bool> DeliverAsync(Guid id, int pickupKm, int pickupFuel, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        FuelRange(pickupFuel, "Çıkış"); // F4.1 L6
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede teslim yapılır.");
            if (c.CikisKm is not null)
                throw new ValidationException("Araç zaten teslim edilmiş.");
            if (pickupKm < 0)
                throw new ValidationException("Çıkış KM negatif olamaz.");
            c.CikisKm = pickupKm;
            c.CikisYakit = pickupFuel;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => { v.Km = Math.Max(v.Km, pickupKm); v.Durum = VehicleStatus.Kirada; }, ct: ct)); // araç çıktı → Kirada
    }

    /// <summary>
    /// Dönüş: KM/yakıt/gerçek dönüş tarihi → fazla km, eksik yakıt, uzatma bedelleri;
    /// GenelToplam + Bakiye güncellenir; durum Tamamlandı (araç tekrar müsait olur).
    /// </summary>
    public async Task<bool> ReturnAsync(
        Guid id, int returnKm, int returnFuel, DateTimeOffset actualReturn,
        int freeKm = 0, string? endReason = null, Guid? receivingStaffId = null,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        if (freeKm < 0)
            throw new ValidationException("KM hediye negatif olamaz.");
        // Üst sınır: int.MaxValue hediye taşma vektörüydü (adversarial BULGU 1); km-farkı guard'ıyla simetrik.
        if (freeKm > 100_000)
            throw new ValidationException("KM hediye gerçekçi değil (100.000 üstü).");
        FuelRange(returnFuel, "Dönüş");          // F4.1 L6
        DatePolicy.ActualReturn(actualReturn);   // F4.1 L4
        if (endReason is { } bs && bs.Trim().Length > 64)
            throw new ValidationException("Bitiş sebebi en fazla 64 karakter olabilir."); // varchar(64) — 500 yerine temiz red
        // Teslim alan personel: bu tenant'ta var olmalı (RLS zaten çapraz-tenant'ı keser; bu erken temiz hata).
        if (receivingStaffId is Guid pid &&
            await personnelRepository.FindAsync(pid, ct) is null)
            throw new ValidationException("Teslim alan personel bulunamadı.");
        // Ek hizmet brütü dönüşte GenelToplam'da KORUNMALI (yoksa düşer). F4.1 adversarial M2: toplam artık
        // kira satır kilidi ALTINDA okunur (k.EkHizmetToplam) — önceden TX DIŞINDA okunuyordu ve eşzamanlı ek
        // hizmet eklemesi GenelToplam'dan sessizce düşüyordu (24 yarışın 23'ünde tutarsızlık).
        // Araç odometresi (Vehicle.Km) kira ile AYNI transaction'da güncellenir — km-bazlı bakım panosunu besler.
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, (c, k) =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede dönüş yapılır.");
            if (c.CikisKm is null)
                throw new ValidationException("Önce teslim (çıkış KM) girilmelidir.");
            if (returnKm < c.CikisKm)
                throw new ValidationException("Dönüş KM, çıkış KM'den küçük olamaz.");
            // Sağduyu üst-sınırı: dönüş Tamamlandı'ya geçince geri alınamaz; parmak hatası (500000) aracın
            // odometresini kalıcı şişirir + bakım panosunu yanlış alarma sokar (adversarial inceleme 3c).
            // Düzeltme yolu: araç kartındaki Km alanı (VehicleService.UpdateAsync).
            if (returnKm - c.CikisKm.Value > 100_000)
                throw new ValidationException("KM farkı gerçekçi değil (tek kirada 100.000 km üstü). Dönüş KM'yi kontrol edin.");
            if (actualReturn < c.BasTar)
                throw new ValidationException("Dönüş tarihi başlangıçtan önce olamaz.");

            var r = ReturnMath.Compute(c, returnKm, returnFuel, actualReturn, freeKm);
            c.DonusKm = returnKm;
            c.DonusYakit = returnFuel;
            c.GercekDonusTar = actualReturn;
            c.KmHediye = freeKm > 0 ? freeKm : null;
            c.BitisSebebi = string.IsNullOrWhiteSpace(endReason) ? null : endReason.Trim();
            c.TeslimAlanPersonelId = receivingStaffId;
            c.FazlaKm = r.FazlaKm;
            c.FazlaKmBedeli = r.FazlaKmBedeli;
            c.EksikYakit = r.EksikYakit;
            c.YakitBedeli = r.YakitBedeli;
            c.UzatmaGun = r.UzatmaGun;
            c.UzatmaBedeli = r.UzatmaBedeli;
            // r.GenelToplam = baz brüt (ek hizmet hariç); ek hizmet brütünü ekle (RentalTotals ile tutarlı).
            c.GenelToplam = r.GenelToplam + k.EkHizmetToplam;
            c.Bakiye = c.GenelToplam - c.Tahsilat;
            c.Durum = RentalStatus.Tamamlandi;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => { v.Km = Math.Max(v.Km, returnKm); v.Durum = VehicleStatus.Musait; },
        // FAZ 2.5: dönüş odometresi km zaman-serisine AYNI transaction'da düşer (karnede "dönem km").
        kmLog: c => new VehicleKmLog { VehicleId = c.VehicleId, Tarih = actualReturn, Km = returnKm, Kaynak = KmLogSource.Donus },
        ct: ct)); // odometre monoton ileri; araç döndü → Musait (boşta)
    }

    /// <summary>
    /// Kira uzatma (roadmap I1): aktif (Kirada) sözleşmenin bitiş tarihini ileri iter. PLANLI uzatma baz kiranın
    /// parçasıdır → Gun + Tutar + GenelToplam + Bakiye artar; UzatmaGun/UzatmaBedeli'ne YAZILMAZ — o alanlar yalnız
    /// GEÇ DÖNÜŞ bedelidir (ReturnMath hesaplar). İkisine birden yazmak BaseGross'ta (Tutar + UzatmaBedeli) ÇİFT
    /// SAYIMDI (denetim K1: dönüş-öncesi fatura + ek-hizmet Recompute şişiyordu). DEFTER POSTLAMAZ; tahsilat/fatura
    /// ayrı. Uzatılan aralıkta başka aktif kira çakışması red.
    /// </summary>
    public async Task<bool> ExtendAsync(Guid id, DateTimeOffset newEndDate, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var c = await _repository.FindRentalAsync(id, ct);
        if (c is null) return false;
        BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // adversarial M3
        if (c.Durum != RentalStatus.Kirada)
            throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
        if (newEndDate <= c.BitTar)
            throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");
        DatePolicy.RentalEnd(c.BasTar, newEndDate); // F4.1 L4: oluşturmayla AYNI süre sınırı

        // FAZ-49 KURAL MATRİSİ — uzatmanın GERÇEK giriş noktası burasıdır (UpdateOpenAsync'te tarih
        // alanı YOKTUR; RentalUpdateInput whitelist'i tip düzeyinde para/tarih taşımaz). Kaynak
        // "uzatılamaz" diyorsa red; MaxGun varsa uzatma sonrası TOPLAM gün sınırı aşamaz.
        var resolvedSourceRule = await _sourceRule.ResolveAsync(c.Kaynak, ct);
        ReservationSourceRule.ExtensionGuard(resolvedSourceRule);
        ReservationSourceRule.MaxDaysGuard(resolvedSourceRule, BookingMath.ComputeDays(c.BasTar, newEndDate));

        // Uzatılan aralıkta (kendisi hariç) başka aktif kira çakışması olmamalı.
        if (await _repository.HasOverlappingActiveRentalAsync(c.VehicleId, c.BasTar, newEndDate, id, ct))
            throw new AvailabilityConflictException();

        var extended = await _repository.UpdateRentalAsync(id, x =>
        {
            if (x.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
            if (newEndDate <= x.BitTar)
                throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");

            var newDays = BookingMath.ComputeDays(x.BasTar, newEndDate);
            var extraDays = newDays - x.Gun;
            if (extraDays <= 0) throw new ValidationException("Uzatma en az 1 gün olmalıdır.");
            var extraCharge = extraDays * x.GunlukUcret;

            x.BitTar = newEndDate;
            x.Gun = newDays;
            x.Tutar += extraCharge;              // baz kira büyür (Tutar = Gun × GunlukUcret tutarlı; K1 fix)
            x.GenelToplam += extraCharge;
            x.Bakiye = x.GenelToplam - x.Tahsilat;
            // Tam teklif dökümü (hediye/iskonto/hafta-sonu) uzatma sonrası BAYAT kalır (Gun/Tutar değişti,
            // döküm create-anı değeri) → sözleşmede yanıltıcı olmasın diye TEMİZLENİR (adversarial L1; para yok).
            x.HediyeGun = null; x.FaturalananGun = null; x.IskontoTutar = null; x.HaftaSonuFark = null;
            x.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
        // FAZ 3.A3a adversarial B3: ücretler NET/GÜN tanımlı — uzatmada sistem satırları yeni güne
        // yeniden ölçeklenir (yalnız faturalanmamışken; faturalanmışsa dokunulmaz — defter snapshot'ı).
        if (extended) await feeLines.SyncContractFeesAsync(id, ct);
        // FAZ 4.2-B1: uzatma dönem planını büyütür (Kesildi/Atlandi korunur; yalnız Planlandi yenilenir).
        if (extended) await periodPlan.EnsurePlanAsync(id, ct);
        return extended;
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // inceltme: sözleşme iptali ayrı izin
        // F4.1 adversarial M3: fatura/tahsilat kontrolü kira-fatura kilidi + satır kilidi ALTINDA (fatura
        // kesimi aynı advisory kilidi alır ve kilit altında "kira iptal mi" diye bakar → yarış yok).
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, (c, k) =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException($"Kira '{c.Durum}' durumundayken iptal edilemez.");
            // Faturalanmış kira iptal edilirse fatura ve cari alacağı yerinde kalıyordu (defter ↔ sözleşme ıraksar).
            if (k.AcikFaturaVar)
                throw new ValidationException("Faturalanmış kira iptal edilemez — önce faturanın iadesini kesin.");
            if (c.Tahsilat != 0m)
                throw new ValidationException("Tahsilatı olan kira iptal edilemez — önce tahsilatı iade edin (ödeme ya da ters kayıt).");
            c.Durum = RentalStatus.Iptal;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => v.Durum = VehicleStatus.Musait, ct: ct)); // iptal → araç serbest (boşta)
    }
}
