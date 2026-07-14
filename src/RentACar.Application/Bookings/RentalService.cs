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
    RentACar.Application.Customers.ICustomerRepository customerRepository,
    ITenantCache cache)
{
    private readonly IBookingRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly PricingService _pricing = pricing;
    private readonly RentACar.Application.RentalAddOns.IRentalAddOnRepository _addOnRepository = addOnRepository;
    private readonly ITenantCache _cache = cache;

    // Kira, araç Durum'unu değiştirdiğinde (Teslim→Kirada / Dönüş→Musait / İptal→Musait) VehicleService'in
    // "vehicles" cache'ini invalidate et → boş-araç dropdown/liste bayat kalmasın (latent cache tutarsızlığı fix).
    private async Task<bool> Inv(Task<bool> op)
    {
        var ok = await op;
        _cache.Invalidate(RentACar.Application.Vehicles.VehicleService.CacheKey);
        return ok;
    }

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
            // Trim+boş→null: UpdateOpenAsync'in Tamamlandi "ofis değişti mi" Ordinal karşılaştırmasıyla
            // tutarlı saklama (adversarial Low: boşluklu kayıt yanlış red üretiyordu).
            CikisOfisi = Lim(input.CikisOfisi, 64, "Çıkış ofisi"),
            DonusOfisi = Lim(input.DonusOfisi, 64, "Dönüş ofisi"),
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
            Aciklama = Lim(input.Aciklama, 1024, "Açıklama"),
            KiralamaTuru = input.KiralamaTuru,
            FaturalamaTipi = input.FaturalamaTipi,
            FiyatTuru = input.FiyatTuru,
            Doviz = input.Doviz,
            KurSnapshot = kurSnapshot,
            // Kira formu detay alanları (bilgi amaçlı; Kaynak daha önce input'ta olup MAP EDİLMİYORDU — parite fix)
            Kaynak = Lim(input.Kaynak, 64, "Kaynak"),
            KampanyaKodu = Lim(input.KampanyaKodu, 64, "Kampanya kodu"),
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
            OzelKdvOran = GirisOzelKdv(input.FiyatTuru, input.OzelKdvOran), // FAZ 1.4 (net-mod çiti dahil)
            DamgaVergisi = VergiDamga(input.DamgaVergisi),   // FAZ 1.4
            TalepTuru = Lim(input.TalepTuru, 64, "Talep türü"),
            GeldigiBirim = Lim(input.GeldigiBirim, 64, "Geldiği birim"),
            KefilBilgisi = Lim(input.KefilBilgisi, 512, "Kefil bilgisi"),
            AssistFirma = Lim(input.AssistFirma, 128, "Assist firma"),
            OzelSoforBilgisi = Lim(input.OzelSoforBilgisi, 512, "Özel şoför bilgisi"),
            EkKosullar = Lim(input.EkKosullar, 2048, "Ek koşullar"),
            ManuelFindexPuan = ValidFindex(input.ManuelFindexPuan),
            KabisCikis = input.KabisCikis,
            KabisDonus = input.KabisDonus,
            OtomatikUzat = input.OtomatikUzat,
            AksYedekAnahtarCikis = input.AksYedekAnahtarCikis,
            AksStepneCikis = input.AksStepneCikis,
            AksZincirCikis = input.AksZincirCikis,
            AksIlkYardimCikis = input.AksIlkYardimCikis,
            AksLastikCikis = Lim(input.AksLastikCikis, 64, "Lastik durumu (çıkış)")
        };
        await _repository.CreateRentalAsync(contract, ct);
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
        BranchScope.RequireInScope(_currentUser, mevcut.CikisOfisi);
        if (mevcut.Durum == RentalStatus.Iptal)
            throw new ValidationException("İptal edilmiş kira güncellenemez.");

        var cikisOfisi = Lim(input.CikisOfisi, 64, "Çıkış ofisi");
        var donusOfisi = Lim(input.DonusOfisi, 64, "Dönüş ofisi");
        if (!string.Equals(cikisOfisi ?? "", mevcut.CikisOfisi ?? "", StringComparison.Ordinal))
            BranchScope.RequireInScope(_currentUser, cikisOfisi); // kira kapsam DIŞINA taşınamaz
        if (input.KmLimit < 0)
            throw new ValidationException("KM limit negatif olamaz.");
        if (input.FazlaKmUcret < 0m || input.YakitBirimUcret < 0m)
            throw new ValidationException("Aşım ücretleri negatif olamaz.");

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
        }
        else if (input.IkinciSurucuId is Guid ikinci && ikinci != mevcut.IkinciSurucuId)
        {
            if (ikinci == mevcut.MusteriId)
                throw new ValidationException("2. sürücü müşteriyle aynı olamaz.");
            if (await customerRepository.FindAsync(ikinci, ct) is null)
                throw new ValidationException("2. sürücü (cari) bulunamadı.");
        }

        return await _repository.UpdateRentalAsync(id, c =>
        {
            // TX içinde yeniden doğrula (ön-kontrol ile arasında durum değişmiş olabilir).
            BranchScope.RequireInScope(_currentUser, c.CikisOfisi);
            if (c.Durum == RentalStatus.Iptal)
                throw new ValidationException("İptal edilmiş kira güncellenemez.");
            if (c.Durum == RentalStatus.Kirada)
            {
                c.CikisOfisi = cikisOfisi;
                c.DonusOfisi = donusOfisi;
                c.IkinciSurucuId = input.IkinciSurucuId;
                c.KmLimit = input.KmLimit;
                c.FazlaKmUcret = input.FazlaKmUcret;
                c.YakitBirimUcret = input.YakitBirimUcret;
            }
            // Bilgi alanları — her iki durumda da (Kirada/Tamamlandi) serbest.
            c.Aciklama = Lim(input.Aciklama, 1024, "Açıklama");
            c.Kaynak = Lim(input.Kaynak, 64, "Kaynak");
            c.KiralamaTuru = Lim(input.KiralamaTuru, 64, "Kiralama türü");
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
            c.OzelKdvOran = GirisOzelKdv(c.FiyatTuru, input.OzelKdvOran); // FAZ 1.4 (net-mod çiti dahil)
            c.DamgaVergisi = VergiDamga(input.DamgaVergisi); // FAZ 1.4
            c.TalepTuru = Lim(input.TalepTuru, 64, "Talep türü");
            c.GeldigiBirim = Lim(input.GeldigiBirim, 64, "Geldiği birim");
            c.KefilBilgisi = Lim(input.KefilBilgisi, 512, "Kefil bilgisi");
            c.AssistFirma = Lim(input.AssistFirma, 128, "Assist firma");
            c.OzelSoforBilgisi = Lim(input.OzelSoforBilgisi, 512, "Özel şoför bilgisi");
            c.EkKosullar = Lim(input.EkKosullar, 2048, "Ek koşullar");
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
        BranchScope.RequireInScope(_currentUser, c.CikisOfisi); // kapsam: sızıntı yok (GetAsync ile aynı)

        if (c.Durum != RentalStatus.Kirada) return KiraDonusOnizleme.Hatali("Yalnız aktif (Kirada) sözleşmede dönüş hesaplanır.");
        if (c.CikisKm is null) return KiraDonusOnizleme.Hatali("Önce teslim (çıkış KM) girilmelidir.");
        if (donusKm < c.CikisKm) return KiraDonusOnizleme.Hatali("Dönüş KM, çıkış KM'den küçük olamaz.");
        if (donusKm - c.CikisKm.Value > 100_000) return KiraDonusOnizleme.Hatali("KM farkı gerçekçi değil (100.000 üstü).");
        if (gercekDonus < c.BasTar) return KiraDonusOnizleme.Hatali("Dönüş tarihi başlangıçtan önce olamaz.");
        if (kmHediye is < 0 or > 100_000) return KiraDonusOnizleme.Hatali("KM hediye 0-100.000 aralığında olmalıdır.");

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
    private static decimal? GirisOzelKdv(string? fiyatTuru, decimal? ozelKdvOran)
    {
        var oran = VergiOran(ozelKdvOran);
        OzelKdvNetModCiti(fiyatTuru, oran);
        return oran;
    }

    private static void OzelKdvNetModCiti(string? fiyatTuru, decimal? ozelKdvOran)
    {
        var netMod = string.Equals(fiyatTuru?.Trim(), "Günlük", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(fiyatTuru?.Trim(), "Toplam", StringComparison.OrdinalIgnoreCase);
        if (netMod && ozelKdvOran is { } o && o != RentACar.Application.Finance.KdvMath.VarsayilanOran)
            throw new ValidationException("Net fiyat modlu kirada özel KDV oranı kullanılamaz (fiyat %20 net üstünden hesaplanır).");
    }

    /// <summary>FAZ 1.4: damga vergisi negatif olamaz.</summary>
    private static decimal? VergiDamga(decimal? damga)
    {
        if (damga is < 0m)
            throw new ValidationException("Damga vergisi negatif olamaz.");
        return damga;
    }

    private static string? Lim(string? s, int max, string alan)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (t.Length > max)
            throw new ValidationException($"{alan} en fazla {max} karakter olabilir.");
        return t;
    }

    private static int? ValidFindex(int? puan)
        => puan is < 0 ? throw new ValidationException("Findeks puanı negatif olamaz.") : puan;

    /// <summary>Teslim: araç çıkışında KM/yakıt girişi. Araç odometresi (Vehicle.Km) AYNI transaction'da
    /// güncellenir (monoton: yalnız İLERİ; küçük girilirse araç km'si değişmez, kira yine kaydolur).</summary>
    public async Task<bool> DeliverAsync(Guid id, int cikisKm, int cikisYakit, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial H1
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, c =>
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
        if (bitisSebebi is { } bs && bs.Trim().Length > 64)
            throw new ValidationException("Bitiş sebebi en fazla 64 karakter olabilir."); // varchar(64) — 500 yerine temiz red
        // Teslim alan personel: bu tenant'ta var olmalı (RLS zaten çapraz-tenant'ı keser; bu erken temiz hata).
        if (teslimAlanPersonelId is Guid pid &&
            await personelRepository.FindAsync(pid, ct) is null)
            throw new ValidationException("Teslim alan personel bulunamadı.");
        // Ek hizmet brütü dönüşte GenelToplam'da KORUNMALI (yoksa düşer).
        var ekHizmetToplam = (await _addOnRepository.ListForRentalAsync(id, ct)).Sum(a => a.Toplam);
        // Araç odometresi (Vehicle.Km) kira ile AYNI transaction'da güncellenir — km-bazlı bakım panosunu besler.
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, c =>
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
        return await Inv(_repository.UpdateRentalWithVehicleAsync(id, c =>
        {
            BranchScope.RequireInScope(_currentUser, c.CikisOfisi); // adversarial M3
            if (c.Durum != RentalStatus.Kirada)
                throw new ValidationException($"Kira '{c.Durum}' durumundayken iptal edilemez.");
            c.Durum = RentalStatus.Iptal;
            c.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, v => v.Durum = VehicleStatus.Musait, ct: ct)); // iptal → araç serbest (boşta)
    }
}
