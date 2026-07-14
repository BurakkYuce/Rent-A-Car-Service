using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Pricing;
using RentACar.Domain.Common;

namespace RentACar.Application.Bookings;

/// <summary>Canlı hesap isteği (kira formu önizleme paneli). SALT HESAP — hiçbir şey persist edilmez.</summary>
public sealed record KiraHesapIstek(
    Guid? VehicleId,
    DateTimeOffset BasTar,
    DateTimeOffset BitTar,
    decimal? GunlukUcret,
    string? FiyatTuru,
    string? Doviz,
    string? CikisOfisi,
    IReadOnlyList<KiraHesapEkHizmet> EkHizmetler,
    Guid? RentalId = null,
    Guid? MusteriId = null,
    string? KampanyaKodu = null,
    Guid? IkinciSurucuId = null,
    string? DonusOfisi = null,
    decimal? DropUcreti = null);

public sealed record KiraHesapEkHizmet(Guid TanimId, decimal Miktar);

public sealed record KiraHesapEkKalem(Guid TanimId, string Ad, decimal Miktar, decimal Net, decimal Kdv, decimal Toplam);

/// <summary>Canlı hesap sonucu. Ok=false → Hata mesajı panelde gösterilir (exception değil — kullanıcı
/// yazarken nazik geri bildirim). Tutar/Net/Kdv baz kira; GenelToplam = Tutar + EkHizmetToplam (kira dövizinde);
/// GenelToplamTl yalnız FX'te (kur çözülebildiyse). Kalan = GenelToplam − Tahsilat (RentalId verildiyse; yoksa
/// tahsilatsız → GenelToplam).</summary>
public sealed record KiraHesapSonuc(
    bool Ok,
    string? Hata,
    int Gun,
    decimal GunlukUcret,
    decimal Tutar,
    decimal Net,
    decimal Kdv,
    int? HediyeGun,
    decimal? IskontoTutar,
    decimal? HaftaSonuFark,
    int? FaturalananGun,
    IReadOnlyList<KiraHesapEkKalem> EkKalemler,
    decimal EkHizmetToplam,
    decimal GenelToplam,
    string Doviz,
    decimal? Kur,
    decimal? GenelToplamTl,
    decimal? Tahsilat,
    decimal Kalan,
    IReadOnlyList<string>? Notlar = null);

/// <summary>
/// Kira formu CANLI hesap servisi (JS fetch → GET /kiralar/hesapla → JSON). UI HİÇBİR formül taşımaz:
/// baz kira GERÇEK motordan (<see cref="PricingService.PriceAsync"/> → Otomatik'te RentalQuoteEngine),
/// ek kalem matematiği <see cref="RentACar.Application.RentalAddOns.RentalAddOnService"/> AddAsync ile
/// BİT-EŞ (net = round(birim×miktar,2); KdvMath.FromNet), KDV ayrışımı KdvMath.FromGross. Böylece
/// önizleme == kayıt her zaman tutar. PERSIST SIFIR; her çağrıda TAZE BookingInput kurulur
/// (PriceAsync input'u mutate eder ve net modlarda idempotent değildir — çift gross-up koruması).
/// </summary>
public sealed class KiraHesapService(
    PricingService pricing,
    IEkHizmetTanimRepository ekHizmetler,
    KurService kur,
    IBookingRepository bookings,
    ICurrentUser currentUser,
    FeeLineService feeLines,
    RentACar.Application.Customers.ICustomerRepository musteriler,
    KdvVarsayilan kdvVarsayilanServis)
{
    private const int MaxEkKalem = 50; // abuse guard: tek istekte gerçekçi üst sınır
    // Taşma guard'ları (adversarial PR-B Medium): decimal.MaxValue mertebesinde miktar/ücret,
    // round(birim×miktar) veya gün×ücret çarpımında OverflowException → 500 üretiyordu; "ok:false" sözleşmesi
    // delinmesin diye gerçekçi üst sınırlarda nazik red.
    private const decimal MaxMiktar = 100_000m;
    private const decimal MaxGunlukUcret = 100_000_000m;

    private readonly ICurrentUser _currentUser = currentUser;

    public async Task<KiraHesapSonuc> HesaplaAsync(KiraHesapIstek istek, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // çift savunma (grup zaten gate'li)

        var doviz = string.IsNullOrWhiteSpace(istek.Doviz) ? "TL" : istek.Doviz.Trim();
        if (istek.BitTar <= istek.BasTar)
            return Hatali("Bitiş tarihi başlangıçtan sonra olmalıdır.", doviz);
        if (istek.EkHizmetler.Count > MaxEkKalem)
            return Hatali($"En fazla {MaxEkKalem} ek hizmet kalemi hesaplanabilir.", doviz);
        if (istek.GunlukUcret is < 0m)
            return Hatali("Günlük ücret negatif olamaz.", doviz);
        if (istek.GunlukUcret is > MaxGunlukUcret)
            return Hatali("Günlük ücret gerçekçi değil.", doviz);
        if (istek.EkHizmetler.Any(e => e.Miktar > MaxMiktar))
            return Hatali($"Ek hizmet miktarı gerçekçi değil ({MaxMiktar:0} üstü).", doviz);

        // TAZE BookingInput — asla yeniden kullanılmaz/persist edilmez (PriceAsync mutasyon notu).
        var input = new BookingInput
        {
            VehicleId = istek.VehicleId ?? Guid.Empty,
            BasTar = istek.BasTar,
            BitTar = istek.BitTar,
            GunlukUcret = istek.GunlukUcret ?? 0m,
            FiyatTuru = istek.FiyatTuru,
            CikisOfisi = istek.CikisOfisi,
            Doviz = doviz,
            // Adversarial A5-B5: önizleme == kayıt — müşteri (A2 segment) ve kampanya kodu (A5)
            // canlı hesaba da girer; kod geçersizse Hatali(ok:false) nazik geri bildirimi.
            MusteriId = istek.MusteriId ?? Guid.Empty,
            KampanyaKodu = istek.KampanyaKodu
        };

        PricingService.PricedRental pr;
        try { pr = await pricing.PriceAsync(input, ct); }
        catch (ValidationException ex) { return Hatali(ex.Message, doviz); } // örn. Otomatik + tarife yok

        // FAZ 3.A6: Net/KDV göstergesi tenant varsayılanıyla ayrışır (net-mod gross-up'ı da PriceAsync
        // içinde aynı orandan) — önizleme == kayıt/fatura ayrıştırması.
        var (net, kdv) = KdvMath.FromGross(pr.Tutar, pr.KdvOranSnapshot ?? await kdvVarsayilanServis.OranAsync(ct));

        // Ek kalemler — AddAsync ile bit-eş matematik (tanımdan snapshot; override yok).
        var kalemler = new List<KiraHesapEkKalem>(istek.EkHizmetler.Count);
        foreach (var e in istek.EkHizmetler)
        {
            if (e.Miktar <= 0) continue; // işaretlenmemiş/sıfır satır — sessiz atla (önizleme)
            var tanim = await ekHizmetler.FindAsync(e.TanimId, ct);
            if (tanim is null) continue; // silinmiş tanım — önizlemede kalem düşer, kayıtta AddAsync reddeder
            var kalemNet = Math.Round(tanim.BirimUcret * e.Miktar, 2, MidpointRounding.AwayFromZero);
            var (kalemKdv, kalemGross) = KdvMath.FromNet(kalemNet, tanim.KdvOrani);
            kalemler.Add(new KiraHesapEkKalem(tanim.Id, tanim.Ad, e.Miktar, kalemNet, kalemKdv, kalemGross));
        }
        // FAZ 3.A3a: SİSTEM ücret satırları önizlemesi — kayıtla AYNI saf hesap (FeeLineService.HesaplaSaf)
        // + AYNI kalem matematiği (net = round(birim×gün,2); KdvMath.FromNet) → önizleme == kayıt.
        var feeNotlar = new List<string>();
        if (istek.VehicleId is Guid feeVid)
        {
            var grup = await feeLines.GrupCozAsync(feeVid, ct);
            var dogum = istek.MusteriId is Guid mid ? (await musteriler.FindAsync(mid, ct))?.DogumTarihi : null;
            var drop = await feeLines.DropUcretCozAsync(istek.CikisOfisi, istek.DonusOfisi, istek.DropUcreti, ct);
            foreach (var s in FeeLineService.HesaplaSaf(
                grup, pr.Gun, istek.BasTar, dogum, istek.IkinciSurucuId is not null, doviz, feeNotlar, drop))
            {
                var (tanimId, kdvOrani) = await feeLines.TanimBilgiAsync(s.TanimKod, ct);
                var fNet = Math.Round(s.BirimNet * s.Gun, 2, MidpointRounding.AwayFromZero);
                var (fKdv, fGross) = KdvMath.FromNet(fNet, kdvOrani);
                kalemler.Add(new KiraHesapEkKalem(tanimId ?? Guid.Empty, s.Ad + " (sistem)", s.Gun, fNet, fKdv, fGross));
            }
        }

        var ekToplam = kalemler.Sum(k => k.Toplam);
        var genelToplam = pr.Tutar + ekToplam;

        // Kur (yalnız gösterim; kayıt anında KurSnapshot ayrıca çözülür): SabitKur → TCMB (DB'den, dış HTTP yok).
        decimal? kurDeger = null;
        decimal? genelToplamTl = null;
        var kod = KurService.NormalizeKod(doviz);
        if (kod == "TRY") { kurDeger = 1m; genelToplamTl = genelToplam; }
        else
        {
            try
            {
                kurDeger = await kur.GetRateAsync(kod, istek.BasTar, ct: ct);
                genelToplamTl = Math.Round(genelToplam * kurDeger.Value, 2, MidpointRounding.AwayFromZero);
            }
            catch (ValidationException) { /* kur yok → TL karşılığı gösterilmez; kayıt anında temiz red zaten var */ }
        }

        // Kalan: mevcut kira bağlamında (edit) tahsilat düşülür; şube kapsamı zorlanır (sızıntı yok).
        decimal? tahsilat = null;
        if (istek.RentalId is Guid rid)
        {
            var c = await bookings.FindRentalAsync(rid, ct);
            if (c is not null)
            {
                BranchScope.RequireInScope(_currentUser, c.CikisOfisi);
                tahsilat = c.Tahsilat;
            }
        }
        var kalan = genelToplam - (tahsilat ?? 0m);

        return new KiraHesapSonuc(
            Ok: true, Hata: null,
            Gun: pr.Gun, GunlukUcret: input.GunlukUcret, Tutar: pr.Tutar, Net: net, Kdv: kdv,
            HediyeGun: pr.HediyeGun, IskontoTutar: pr.IskontoTutar, HaftaSonuFark: pr.HaftaSonuFark,
            FaturalananGun: pr.FaturalananGun,
            EkKalemler: kalemler, EkHizmetToplam: ekToplam,
            GenelToplam: genelToplam, Doviz: doviz, Kur: kurDeger, GenelToplamTl: genelToplamTl,
            Tahsilat: tahsilat, Kalan: kalan,
            Notlar: feeNotlar.Count > 0 ? feeNotlar : null);
    }

    private static KiraHesapSonuc Hatali(string mesaj, string doviz) => new(
        Ok: false, Hata: mesaj, Gun: 0, GunlukUcret: 0m, Tutar: 0m, Net: 0m, Kdv: 0m,
        HediyeGun: null, IskontoTutar: null, HaftaSonuFark: null, FaturalananGun: null,
        EkKalemler: [], EkHizmetToplam: 0m, GenelToplam: 0m, Doviz: doviz,
        Kur: null, GenelToplamTl: null, Tahsilat: null, Kalan: 0m);
}
