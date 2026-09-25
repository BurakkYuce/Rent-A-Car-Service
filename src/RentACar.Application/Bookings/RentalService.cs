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
    RentACar.Application.Kur.KurService kurService,
    RentACar.Application.Personnel.IPersonelRepository personelRepository,
    RentACar.Application.Customers.ICustomerRepository customerRepository,
    ITenantCache cache,
    FeeLineService feeLines,
    RentACar.Application.Finance.KdvVarsayilan kdvVarsayilan,
    RentACar.Application.FaturaDonemleri.FaturaDonemPlanService donemPlan,
    RentACar.Application.Finance.ICashRepository cashRepository,
    RentACar.Application.Locations.ILocationRepository locationRepository,
    RezKaynakKuralService kaynakKural,
    RentACar.Application.Vehicles.IVehicleRepository vehicleRepository)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    // FAZ-49: rezervasyon kaynağı kural matrisi (uzatma yasağı / provizyon yok / km sınırsız /
    // drop yasağı / max gün). Saf gövde RezKaynakKural'da; burada yalnız GİRİŞ NOKTASI çağrıları.
    private readonly RezKaynakKuralService _kaynakKural = kaynakKural;
    private readonly RentACar.Application.RentalAddOns.IRentalAddOnRepository _addOnRepository = addOnRepository;
    private readonly ITenantCache _cache = cache;

    /// <summary>
    /// Servis düzeyi yakıt çiti — TEK iç ölçek 0–12 (<see cref="YakitOlcegi"/>, Karar (3) 2026-09-25). Harici JWT
    /// API yüzde gönderir ama sınırda 0–12'ye çevirir; buraya yüzde ulaşamaz. Çit negatif değeri ve int taşmasını
    /// (int.MinValue → sahte milyarlık eksik-yakıt bedeli) keser; eksik yakıt en fazla 12 birim olur.
    /// </summary>
    public const int YakitEnFazla = YakitOlcegi.EnFazla;

    private static void YakitAraligi(int yakit, string etiket)
    {
        if (yakit is < 0 or > YakitEnFazla)
            throw new ValidationException($"{etiket} yakıt 0-{YakitEnFazla} aralığında olmalıdır.");
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
        TarihPolitikasi.KiraBaslangic(input.BasTar); // geçmişe açık (retroaktif); gelecek anti-typo ≤ +1yıl
        TarihPolitikasi.KiraBitis(input.BasTar, input.BitTar); // F4.1 L4: süre ≤ 5 yıl (9999 → taşma/bloke)

        // F4.1 adversarial M1: çıkış ofisi GİRİŞ NOKTASINDA şube kapsamından geçer (UpdateOpenAsync'teki
        // desen: ofis → Location → türetilmiş şube). Önceden operatör başka şubeye kira yazabiliyor (o
        // şubenin aracını bloke edip kaydı kendisi de göremiyordu) ya da ofissiz "yetim" kira açabiliyordu.
        var cikisOfisiGiris = Lim(input.CikisOfisi, 64, "Çıkış ofisi");
        if (cikisOfisiGiris is null && !BranchScope.EffectiveFilter(_currentUser).Unrestricted)
            throw new ValidationException("Çıkış ofisi zorunludur (şubeye bağlı kullanıcı kendi şubesinin ofisini seçmelidir).");
        var cikisSubeId = cikisOfisiGiris is null
            ? null : (await locationRepository.FindByAdAsync(cikisOfisiGiris, ct))?.SubeId;
        BranchScope.RequireInScope(_currentUser, cikisSubeId, cikisOfisiGiris);

        // 2. sürücü: aynı tenant'ta var olmalı (RLS çapraz-tenant'ı zaten keser; bu erken temiz hata) +
        // kendisiyle aynı olamaz.
        if (input.IkinciSurucuId is Guid ikinci)
        {
            if (ikinci == input.MusteriId)
                throw new ValidationException("2. sürücü müşteriyle aynı olamaz.");
            if (await customerRepository.FindAsync(ikinci, ct) is null)
                throw new ValidationException("2. sürücü (cari) bulunamadı.");
        }
        // FAZ-47: kayıtlı (FK) ve misafir (serbest metin) 2. sürücü BİRLİKTE olamaz.
        IkinciSurucuTekYolGuard(input.IkinciSurucuId, input.IkinciSurucuSerbestAd, input.IkinciSurucuSerbestSoyad,
            input.IkinciSurucuSerbestTel, input.IkinciSurucuSerbestEhliyetSinifi);
        // FAZ 4.4 RİSK GUARD'ı (giriş noktasında — tarih-politikası dersi): cari RiskLimiti tanımlıysa
        // (>0) ve mevcut borç bakiyesi limiti AŞIYORSA kira ancak Yönetici/Admin RiskOnay'ıyla açılır.
        // Onay kutusunu Operatör işaretleyemez (rol doğrulaması burada — UI'daki gizleme yeterli değil).
        // Varlık kontrolü (DEVIR §6 Low, harici RentalsApi): Rentals.MusteriId/VehicleId'de bileşik FK yok →
        // hiç olmayan ya da BAŞKA KİRACININ kimliğiyle kira yazılabiliyordu. Kontrol artık servis GİRİŞİNDE —
        // harici API, /api/ui ve Blazor formu aynı kuraldan geçer (uçlardaki kopyalar kaldırıldı). RLS + tenant
        // query filter kapsamlı FindAsync: yabancı kiracının kaydı "yok" görünür (varlık sızmaz) → 400.
        // Kalıcı çözüm (bileşik FK) ayrı iş. Kural tek yerde: BookingPartyCheck (rezervasyon/teklif de kullanır).
        var musteri = await BookingPartyCheck.RequireAsync(
            customerRepository, vehicleRepository, input.MusteriId, input.VehicleId, ct);
        if (musteri is { RiskLimiti: > 0m })
        {
            var bakiye = await cashRepository.GetCariBalanceAsync(input.MusteriId, ct);
            if (bakiye > musteri.RiskLimiti)
            {
                if (!input.RiskOnay)
                    throw new ValidationException(
                        $"Risk limiti aşıldı (bakiye {bakiye:N2} > limit {musteri.RiskLimiti:N2}) — Yönetici onayı gerekir.");
                if (_currentUser.Role is not (UserRole.Admin or UserRole.Yonetici))
                    throw new ValidationException("Risk onayı yalnız Yönetici/Admin tarafından verilebilir.");
            }
        }

        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA (fiyatlamadan önce): kaynağın gün sınırı + drop
        // yasağı reddeder, km sınırsızlığı KmLimit'i 0'a sabitler.
        var kaynakKurali = await _kaynakKural.CozAsync(input.Kaynak, ct);
        RezKaynakKural.MaxGunGuard(kaynakKurali, BookingMath.ComputeGun(input.BasTar, input.BitTar));
        RezKaynakKural.DropGuard(kaynakKurali, input.CikisOfisi, input.DonusOfisi);
        var kaynakKmLimit = RezKaynakKural.KmLimitUygula(kaynakKurali, input.KmLimit);

        var pr = await _pricing.PriceAsync(input, ct: ct); // fiyat motoru: manuel >0 kazanır, yoksa tarife (tam teklif)
        var varsayilanKdv = await kdvVarsayilan.OranAsync(ct); // FAZ 3.A6 (net-mod çiti gross-up oranıyla karşılaştırır)

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
            // Trim+boş→null: UpdateOpenAsync'in Tamamlandi "ofis değişti mi" Ordinal karşılaştırmasıyla
            // tutarlı saklama (adversarial Low: boşluklu kayıt yanlış red üretiyordu).
            CikisOfisi = Lim(input.CikisOfisi, 64, "Çıkış ofisi"),
            DonusOfisi = Lim(input.DonusOfisi, 64, "Dönüş ofisi"),
            Gun = pr.Gun,
            GunlukUcret = input.GunlukUcret,
            KmLimit = kaynakKmLimit,          // FAZ-49: KmSinirsiz kaynakta 0'a (sınırsız) sabitlenir
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
            KurSnapshot = kurSnapshot,
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
            OzelKdvOran = GirisOzelKdv(input.FiyatTuru, input.OzelKdvOran, pr.KdvOranSnapshot ?? varsayilanKdv), // FAZ 1.4+A6 (net-mod çiti gross-up oranıyla)
            DamgaVergisi = VergiDamga(input.DamgaVergisi),   // FAZ 1.4
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
        await donemPlan.EnsurePlanAsync(contract.Id, ct);
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
        var mevcut = await _repository.FindRentalAsync(id, ct);
        if (mevcut is null) return false;
        BranchScope.RequireInScope(_currentUser, mevcut.CikisSubeId, mevcut.CikisOfisi);
        if (mevcut.Durum == RentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş kira güncellenemez.");

        var cikisOfisi = Lim(input.CikisOfisi, 64, "Çıkış ofisi");
        var donusOfisi = Lim(input.DonusOfisi, 64, "Dönüş ofisi");
        if (!string.Equals(cikisOfisi ?? "", mevcut.CikisOfisi ?? "", StringComparison.Ordinal))
        {
            // C4: HEDEF ofisin türetilmiş şubesiyle kontrol — operatör kirayı kendi ŞUBESİNİN başka
            // ofisine taşıyabilir (widening, testli); kapsam DIŞI şubenin ofisine taşıyamaz.
            var hedefSubeId = string.IsNullOrWhiteSpace(cikisOfisi)
                ? null : (await locationRepository.FindByAdAsync(cikisOfisi!, ct))?.SubeId;
            BranchScope.RequireInScope(_currentUser, hedefSubeId, cikisOfisi);
        }
        // FAZ-47: kayıtlı (FK) + misafir (serbest metin) 2. sürücü BİRLİKTE olamaz — guard KODDA
        // (form "iki bloktan birini doldur" diye yazdığı için değil).
        IkinciSurucuTekYolGuard(input.IkinciSurucuId, input.IkinciSurucuSerbestAd, input.IkinciSurucuSerbestSoyad,
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
        var kaynakMetni = Lim(input.Kaynak, 64, "Kaynak");
        var kaynakKurali = await _kaynakKural.CozAsync(kaynakMetni, ct);
        var kaynakDegisti = !string.Equals(kaynakMetni ?? "", mevcut.Kaynak ?? "", StringComparison.OrdinalIgnoreCase);
        var ofisDegisti =
            !string.Equals(cikisOfisi ?? "", mevcut.CikisOfisi ?? "", StringComparison.Ordinal)
            || !string.Equals(donusOfisi ?? "", mevcut.DonusOfisi ?? "", StringComparison.Ordinal);
        if (ofisDegisti || kaynakDegisti)
            RezKaynakKural.DropGuard(kaynakKurali, cikisOfisi, donusOfisi);
        // Km sabitlemesi yalnız AÇIK (Kirada) sözleşmenin yazma yolunda uygulanır. Tamamlanmış
        // kirada aşım parametreleri zaten DONMUŞ; sabitlemeyi oradaki "değişti mi" karşılaştırmasına
        // sokmak, kural sonradan konduğunda kaydı tümüyle düzenlenemez yapardı.
        var kmLimit = RezKaynakKural.KmLimitUygula(kaynakKurali, input.KmLimit);

        if (mevcut.Durum == RentalStatus.Tamamlandi)
        {
            if (input.KmLimit != mevcut.KmLimit || input.FazlaKmUcret != mevcut.FazlaKmUcret
                || input.YakitBirimUcret != mevcut.YakitBirimUcret)
                throw new ValidationException("Tamamlanmış kirada aşım parametreleri değiştirilemez (dönüş hesabı yapıldı).");
            if (input.IkinciSurucuId != mevcut.IkinciSurucuId)
                throw new ValidationException("Tamamlanmış kirada 2. sürücü değiştirilemez.");
            if (!string.Equals(cikisOfisi ?? "", mevcut.CikisOfisi ?? "", StringComparison.Ordinal)
                || !string.Equals(donusOfisi ?? "", mevcut.DonusOfisi ?? "", StringComparison.Ordinal))
                throw new ValidationException("Tamamlanmış kirada ofisler değiştirilemez.");
            // A3b-B3: DropUcreti artık PARA-ETKİLİ manuel override — tamamlanmış kirada değişirse
            // alan/satır ıraksar (satır senkronu yalnız Kirada çalışır) → dondur.
            if (input.DropUcreti != mevcut.DropUcreti)
                throw new ValidationException("Tamamlanmış kirada drop ücreti değiştirilemez.");
        }
        // A3b-B3: FATURALANMIŞ kirada da dondur — satır senkronu defter snapshot'ına dokunmaz;
        // alan değişip satır değişmezse sözleşme belgesi ile tahsilat sessizce ıraksardı.
        else if (input.DropUcreti != mevcut.DropUcreti && await _addOnRepository.IsRentalInvoicedAsync(id, ct))
            throw new ValidationException("Faturalanmış kirada drop ücreti değiştirilemez (ücret satırı defter snapshot'ında).");
        if (input.DropUcreti is < 0m)
            throw new ValidationException("Drop ücreti negatif olamaz."); // A3b-B4 (update yolu)
        else if (input.IkinciSurucuId is Guid ikinci && ikinci != mevcut.IkinciSurucuId)
        {
            if (ikinci == mevcut.MusteriId)
                throw new ValidationException("2. sürücü müşteriyle aynı olamaz.");
            if (await customerRepository.FindAsync(ikinci, ct) is null)
                throw new ValidationException("2. sürücü (cari) bulunamadı.");
        }

        // FAZ 3.A6 adversarial B1: net-mod çiti güncellemede SNAPSHOT'la karşılaştırır; snapshot NULL =
        // pre-A6 kira = gross-up KESİNLİKLE 0.20 idi → fallback FATURA ÇİTİYLE AYNI (0.20) — tenant-güncel
        // orana düşmek, çitin kabul ettiği tek değerin faturayı kilitlemesine yol açıyordu.
        var guncelKdvVarsayilan = mevcut.KdvOranSnapshot ?? RentACar.Application.Finance.KdvMath.VarsayilanOran;
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
                c.CikisOfisi = cikisOfisi;
                c.DonusOfisi = donusOfisi;
                c.IkinciSurucuId = input.IkinciSurucuId;
                c.KmLimit = kmLimit;          // FAZ-49: KmSinirsiz kaynakta 0'a (sınırsız) sabitlenir
                c.FazlaKmUcret = input.FazlaKmUcret;
                c.YakitBirimUcret = input.YakitBirimUcret;
            }
            // Bilgi alanları — her iki durumda da (Kirada/Tamamlandi) serbest.
            c.Aciklama = Lim(input.Aciklama, 1024, "Açıklama");
            c.Kaynak = kaynakMetni;           // FAZ-49: kural çözümü ile AYNI metin (ıraksama olmaz)
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
            c.OzelKdvOran = GirisOzelKdv(c.FiyatTuru, input.OzelKdvOran, guncelKdvVarsayilan); // FAZ 1.4+A6
            c.DamgaVergisi = VergiDamga(input.DamgaVergisi); // FAZ 1.4
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
    public async Task<bool> ProvizyonAlAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // FAZ-49 KURAL MATRİSİ — GİRİŞ NOKTASINDA: kaynağı "provizyon yok" diyen sözleşmede bloke
        // alınamaz. Kaynak çözümü async olduğundan repo lambda'sının İÇİNE konamaz; ön-okuma ile
        // burada yapılır (durum/şube doğrulaması lambda içinde aynen sürüyor).
        var mevcut = await _repository.FindRentalAsync(id, ct);
        if (mevcut is null) return false;
        RezKaynakKural.ProvizyonGuard(await _kaynakKural.CozAsync(mevcut.Kaynak, ct));
        return await _repository.UpdateRentalAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi);
            if (c.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kirada provizyon işlemi yapılamaz.");
            if (c.ProvizyonDurum != ProvizyonDurum.Yok)
                throw new ValidationException($"Provizyon zaten '{c.ProvizyonDurum}' durumunda (yalnız Yok → Alındı).");
            if (c.Provizyon is not > 0m)
                throw new ValidationException("Önce Fiyat sekmesinde provizyon (bloke) tutarı girilmelidir.");
            c.ProvizyonDurum = ProvizyonDurum.Alindi;
            c.ProvizyonTarih ??= DateTimeOffset.UtcNow;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>FAZ 4.1 — provizyon kapama (Alindi→Kapandi) veya serbest bırakma (iade=true →
    /// IadeEdildi, kapama tutarı 0). Kapama tutarı verilmezse bloke tutarın tamamı bilgi olarak yazılır.</summary>
    public async Task<bool> ProvizyonKapatAsync(
        Guid id, decimal? kapamaTutar = null, bool iade = false, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (kapamaTutar is < 0m)
            throw new ValidationException("Kapama tutarı negatif olamaz.");
        return await _repository.UpdateRentalAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi);
            if (c.ProvizyonDurum != ProvizyonDurum.Alindi)
                throw new ValidationException($"Yalnız 'Alındı' durumundaki provizyon kapatılabilir (mevcut: {c.ProvizyonDurum}).");
            c.ProvizyonDurum = iade ? ProvizyonDurum.IadeEdildi : ProvizyonDurum.Kapandi;
            c.ProvizyonKapamaTarih = DateTimeOffset.UtcNow;
            c.ProvizyonKapamaTutar = iade ? 0m : (kapamaTutar ?? c.Provizyon);
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// Dönüş CANLI ÖNİZLEMESİ (mega-form Dönüş sekmesi; GET /kiralar/donus-hesapla). GERÇEK motor
    /// (ReturnMath.Compute) + ek hizmet brütü — PERSIST ETMEZ, durum değiştirmez. ReturnAsync ile aynı
    /// guard'lar NAZİK hataya çevrilir (Ok=false — kullanıcı yazarken 500/exception yok). Şube kapsamı zorlanır.
    /// </summary>
    public async Task<KiraDonusOnizleme> PreviewReturnAsync(
        Guid id, int donusKm, int donusYakit, DateTimeOffset gercekDonus, int kmHediye = 0,
        CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var c = await _repository.FindRentalAsync(id, ct);
        if (c is null) return KiraDonusOnizleme.Hatali("Kira bulunamadı.");
        BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // kapsam: sızıntı yok (GetAsync ile aynı)

        if (c.Durum != RentalStatus.Kirada) return KiraDonusOnizleme.Hatali("Yalnız aktif (Kirada) sözleşmede dönüş hesaplanır.");
        if (c.CikisKm is null) return KiraDonusOnizleme.Hatali("Önce teslim (çıkış KM) girilmelidir.");
        if (donusKm < c.CikisKm) return KiraDonusOnizleme.Hatali("Dönüş KM, çıkış KM'den küçük olamaz.");
        if (donusKm - c.CikisKm.Value > 100_000) return KiraDonusOnizleme.Hatali("KM farkı gerçekçi değil (100.000 üstü).");
        if (gercekDonus < c.BasTar) return KiraDonusOnizleme.Hatali("Dönüş tarihi başlangıçtan önce olamaz.");
        if (kmHediye is < 0 or > 100_000) return KiraDonusOnizleme.Hatali("KM hediye 0-100.000 aralığında olmalıdır.");
        if (donusYakit is < 0 or > YakitEnFazla) return KiraDonusOnizleme.Hatali($"Dönüş yakıt 0-{YakitEnFazla} aralığında olmalıdır."); // F4.1 L6
        if (gercekDonus > DateTimeOffset.UtcNow.AddYears(1)) return KiraDonusOnizleme.Hatali("Gerçek dönüş tarihi en fazla 1 yıl ileri olabilir."); // F4.1 L4

        var r = ReturnMath.Compute(c, donusKm, donusYakit, gercekDonus, kmHediye);
        var ekHizmetToplam = (await _addOnRepository.ListForRentalAsync(id, ct)).Sum(a => a.Toplam);
        var yeniGenelToplam = r.GenelToplam + ekHizmetToplam; // ReturnAsync ile birebir aynı formül
        return new KiraDonusOnizleme(
            Ok: true, Hata: null,
            KullanilanKm: r.KullanilanKm, FazlaKm: r.FazlaKm, FazlaKmBedeli: r.FazlaKmBedeli,
            EksikYakit: r.EksikYakit, YakitBedeli: r.YakitBedeli,
            UzatmaGun: r.UzatmaGun, UzatmaBedeli: r.UzatmaBedeli,
            EkHizmetToplam: ekHizmetToplam, YeniGenelToplam: yeniGenelToplam,
            Kalan: yeniGenelToplam - c.Tahsilat);
    }

    /// <summary>Serbest metin alanı: trim + boş→null + aşımda temiz red (DB varchar taşması 500 yerine).</summary>
    /// <summary>FAZ 1.4: özel KDV oranı kesir 0..1 (aksi red).</summary>
    private static decimal? VergiOran(decimal? oran)
    {
        if (oran is { } o && (o < 0m || o > 1m))
            throw new ValidationException("Özel KDV oranı 0 ile 1 arasında olmalıdır (kesir, ör. 0.10).");
        return oran;
    }

    /// <summary>FAZ 1.4 (adversarial D — guard'ı GİRİŞ noktasına koy dersi): NET fiyat modlu kirada
    /// varsayılan-dışı özel KDV çelişkisi girişte reddedilir — aksi halde dönüş fark-faturası kesilene
    /// dek kilitlenir (fatura-anı guard'ı savunma-derinliği olarak kalır).</summary>
    private static decimal? GirisOzelKdv(string? fiyatTuru, decimal? ozelKdvOran, decimal gecerliVarsayilan)
    {
        var oran = VergiOran(ozelKdvOran);
        OzelKdvNetModCiti(fiyatTuru, oran, gecerliVarsayilan);
        return oran;
    }

    /// <summary>FAZ 3.A6: karşılaştırma SABİT 0.20 yerine GEÇERLİ varsayılanla (create'te gross-up
    /// oranı / update'te snapshot ?? tenant varsayılanı) — tenant oranı %10 iken %10'luk özel oran
    /// net-modda çelişki DEĞİLDİR.</summary>
    private static void OzelKdvNetModCiti(string? fiyatTuru, decimal? ozelKdvOran, decimal gecerliVarsayilan)
    {
        var netMod = string.Equals(fiyatTuru?.Trim(), "Günlük", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(fiyatTuru?.Trim(), "Toplam", StringComparison.OrdinalIgnoreCase);
        if (netMod && ozelKdvOran is { } o && o != gecerliVarsayilan)
            throw new ValidationException(
                $"Net fiyat modlu kirada özel KDV oranı kullanılamaz (fiyat %{gecerliVarsayilan * 100:0.##} net üstünden hesaplanır).");
    }

    /// <summary>FAZ 1.4: damga vergisi negatif olamaz.</summary>
    private static decimal? VergiDamga(decimal? damga)
    {
        if (damga is < 0m)
            throw new ValidationException("Damga vergisi negatif olamaz.");
        return damga;
    }

    // FAZ-48: kural BookingMath.Kirp'e taşındı (rezervasyon aynı alanları taşıyor); davranış AYNI.
    private static string? Lim(string? s, int max, string alan) => BookingMath.Kirp(s, max, alan);

    private static int? ValidFindex(int? puan)
        => puan is < 0 ? throw new ValidationException("Findeks puanı negatif olamaz.") : puan;

    /// <summary>
    /// FAZ-47 — 2. sürücü TEK YOL kuralı: ya kayıtlı cari (<c>IkinciSurucuId</c>) ya misafir serbest metni.
    /// İkisi birden dolu gelirse GÜRÜLTÜLÜ RED — sessizce birini seçmek "sözleşmedeki 2. sürücü kim"
    /// sorusunu belirsiz bırakırdı (ve FK'lı sürücü ek-sürücü ÜCRETİ üretirken serbest metin üretmez;
    /// iki kayıt bir arada, ücret alınmayan görünmez bir sürücü demek olurdu).
    /// </summary>
    private static void IkinciSurucuTekYolGuard(Guid? fk, params string?[] serbestAlanlar)
    {
        if (fk is null) return;
        if (serbestAlanlar.Any(s => !string.IsNullOrWhiteSpace(s)))
            throw new ValidationException(
                "2. sürücü ya kayıtlı cariden seçilir ya da misafir bilgileri girilir — ikisi birden doldurulamaz.");
    }

    /// <summary>Teslim: araç çıkışında KM/yakıt girişi. Araç odometresi (Vehicle.Km) AYNI transaction'da
    /// güncellenir (monoton: yalnız İLERİ; küçük girilirse araç km'si değişmez, kira yine kaydolur).</summary>
    public async Task<bool> DeliverAsync(Guid id, int cikisKm, int cikisYakit, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        YakitAraligi(cikisYakit, "Çıkış"); // F4.1 L6
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException("Yalnız aktif (Kirada) sözleşmede teslim yapılır.");
            if (c.CikisKm is not null)
                throw new ValidationException("Araç zaten teslim edilmiş.");
            if (cikisKm < 0)
                throw new ValidationException("Çıkış KM negatif olamaz.");
            c.CikisKm = cikisKm;
            c.CikisYakit = cikisYakit;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => { v.Km = Math.Max(v.Km, cikisKm); v.Durum = VehicleStatus.Kirada; }, ct: ct)); // araç çıktı → Kirada
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
        YakitAraligi(donusYakit, "Dönüş");          // F4.1 L6
        TarihPolitikasi.GercekDonus(gercekDonus);   // F4.1 L4
        if (bitisSebebi is { } bs && bs.Trim().Length > 64)
            throw new ValidationException("Bitiş sebebi en fazla 64 karakter olabilir."); // varchar(64) — 500 yerine temiz red
        // Teslim alan personel: bu tenant'ta var olmalı (RLS zaten çapraz-tenant'ı keser; bu erken temiz hata).
        if (teslimAlanPersonelId is Guid pid &&
            await personelRepository.FindAsync(pid, ct) is null)
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
            c.GenelToplam = r.GenelToplam + k.EkHizmetToplam;
            c.Bakiye = c.GenelToplam - c.Tahsilat;
            c.Durum = RentalStatus.Tamamlandi;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => { v.Km = Math.Max(v.Km, donusKm); v.Durum = VehicleStatus.Musait; },
        // FAZ 2.5: dönüş odometresi km zaman-serisine AYNI transaction'da düşer (karnede "dönem km").
        kmLog: c => new VehicleKmLog { VehicleId = c.VehicleId, Tarih = gercekDonus, Km = donusKm, Kaynak = KmLogKaynak.Donus },
        ct: ct)); // odometre monoton ileri; araç döndü → Musait (boşta)
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
        BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi); // adversarial M3
        if (c.Durum != RentalStatus.Kirada)
            throw new ValidationException("Yalnız aktif (Kirada) sözleşme uzatılabilir.");
        if (yeniBitTar <= c.BitTar)
            throw new ValidationException("Yeni bitiş tarihi mevcut bitişten sonra olmalıdır.");
        TarihPolitikasi.KiraBitis(c.BasTar, yeniBitTar); // F4.1 L4: oluşturmayla AYNI süre sınırı

        // FAZ-49 KURAL MATRİSİ — uzatmanın GERÇEK giriş noktası burasıdır (UpdateOpenAsync'te tarih
        // alanı YOKTUR; RentalUpdateInput whitelist'i tip düzeyinde para/tarih taşımaz). Kaynak
        // "uzatılamaz" diyorsa red; MaxGun varsa uzatma sonrası TOPLAM gün sınırı aşamaz.
        var kaynakKurali = await _kaynakKural.CozAsync(c.Kaynak, ct);
        RezKaynakKural.UzatmaGuard(kaynakKurali);
        RezKaynakKural.MaxGunGuard(kaynakKurali, BookingMath.ComputeGun(c.BasTar, yeniBitTar));

        // Uzatılan aralıkta (kendisi hariç) başka aktif kira çakışması olmamalı.
        if (await _repository.HasOverlappingActiveRentalAsync(c.VehicleId, c.BasTar, yeniBitTar, id, ct))
            throw new AvailabilityConflictException();

        var uzatildi = await _repository.UpdateRentalAsync(id, x =>
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
        // FAZ 3.A3a adversarial B3: ücretler NET/GÜN tanımlı — uzatmada sistem satırları yeni güne
        // yeniden ölçeklenir (yalnız faturalanmamışken; faturalanmışsa dokunulmaz — defter snapshot'ı).
        if (uzatildi) await feeLines.SyncContractFeesAsync(id, ct);
        // FAZ 4.2-B1: uzatma dönem planını büyütür (Kesildi/Atlandi korunur; yalnız Planlandi yenilenir).
        if (uzatildi) await donemPlan.EnsurePlanAsync(id, ct);
        return uzatildi;
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
