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
public sealed class RentalCalculationService(
    PricingService pricing,
    IAddOnDefinitionRepository addOns,
    ExchangeRateService exchangeRate,
    IBookingRepository bookings,
    ICurrentUser currentUser,
    FeeLineService feeLines,
    RentACar.Application.Customers.ICustomerRepository customers,
    VatDefault vatDefaultService)
{
    private const int MaxExtraItems = 50; // abuse guard: tek istekte gerçekçi üst sınır
    // Taşma guard'ları (adversarial PR-B Medium): decimal.MaxValue mertebesinde miktar/ücret,
    // round(birim×miktar) veya gün×ücret çarpımında OverflowException → 500 üretiyordu; "ok:false" sözleşmesi
    // delinmesin diye gerçekçi üst sınırlarda nazik red.
    private const decimal MaxQuantity = 100_000m;
    private const decimal MaxDailyFee = 100_000_000m;

    private readonly ICurrentUser _currentUser = currentUser;

    public async Task<KiraHesapSonuc> CalculateAsync(KiraHesapIstek request, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // çift savunma (grup zaten gate'li)

        var currency = string.IsNullOrWhiteSpace(request.Doviz) ? "TL" : request.Doviz.Trim();
        if (request.BitTar <= request.BasTar)
            return Invalid("Bitiş tarihi başlangıçtan sonra olmalıdır.", currency);
        if (request.EkHizmetler.Count > MaxExtraItems)
            return Invalid($"En fazla {MaxExtraItems} ek hizmet kalemi hesaplanabilir.", currency);
        if (request.GunlukUcret is < 0m)
            return Invalid("Günlük ücret negatif olamaz.", currency);
        if (request.GunlukUcret is > MaxDailyFee)
            return Invalid("Günlük ücret gerçekçi değil.", currency);
        if (request.EkHizmetler.Any(e => e.Miktar > MaxQuantity))
            return Invalid($"Ek hizmet miktarı gerçekçi değil ({MaxQuantity:0} üstü).", currency);

        // TAZE BookingInput — asla yeniden kullanılmaz/persist edilmez (PriceAsync mutasyon notu).
        var input = new BookingInput
        {
            VehicleId = request.VehicleId ?? Guid.Empty,
            BasTar = request.BasTar,
            BitTar = request.BitTar,
            GunlukUcret = request.GunlukUcret ?? 0m,
            FiyatTuru = request.FiyatTuru,
            CikisOfisi = request.CikisOfisi,
            Doviz = currency,
            // Adversarial A5-B5: önizleme == kayıt — müşteri (A2 segment) ve kampanya kodu (A5)
            // canlı hesaba da girer; kod geçersizse Hatali(ok:false) nazik geri bildirimi.
            MusteriId = request.MusteriId ?? Guid.Empty,
            KampanyaKodu = request.KampanyaKodu
        };

        PricingService.PricedRental pr;
        try { pr = await pricing.PriceAsync(input, ct: ct); }
        catch (ValidationException ex) { return Invalid(ex.Message, currency); } // örn. Otomatik + tarife yok

        // FAZ 3.A6: Net/KDV göstergesi tenant varsayılanıyla ayrışır (net-mod gross-up'ı da PriceAsync
        // içinde aynı orandan) — önizleme == kayıt/fatura ayrıştırması.
        var (net, vat) = VatMath.FromGross(pr.Tutar, pr.KdvOranSnapshot ?? await vatDefaultService.RateAsync(ct));

        // Ek kalemler — AddAsync ile bit-eş matematik (tanımdan snapshot; override yok).
        var items = new List<KiraHesapEkKalem>(request.EkHizmetler.Count);
        foreach (var e in request.EkHizmetler)
        {
            if (e.Miktar <= 0) continue; // işaretlenmemiş/sıfır satır — sessiz atla (önizleme)
            var definition = await addOns.FindAsync(e.TanimId, ct);
            if (definition is null) continue; // silinmiş tanım — önizlemede kalem düşer, kayıtta AddAsync reddeder
            var itemNet = Math.Round(definition.BirimUcret * e.Miktar, 2, MidpointRounding.AwayFromZero);
            var (itemVat, itemGross) = VatMath.FromNet(itemNet, definition.KdvOrani);
            items.Add(new KiraHesapEkKalem(definition.Id, definition.Ad, e.Miktar, itemNet, itemVat, itemGross));
        }
        // FAZ 3.A3a: SİSTEM ücret satırları önizlemesi — kayıtla AYNI saf hesap (FeeLineService.HesaplaSaf)
        // + AYNI kalem matematiği (net = round(birim×gün,2); KdvMath.FromNet) → önizleme == kayıt.
        var feeNotes = new List<string>();
        if (request.VehicleId is Guid feeVid)
        {
            var group = await feeLines.ResolveGroupAsync(feeVid, ct);
            var birth = request.MusteriId is Guid mid ? (await customers.FindAsync(mid, ct))?.DogumTarihi : null;
            // FAZ-22: gün, MinGun koşulu için geçiyor. Önizleme ve kayıt AYNI gün sayısını
            // (pr.Gun / c.Gun) kullanır — aksi hâlde önizleme==kayıt sözleşmesi bozulurdu.
            var drop = await feeLines.ResolveDropFeeAsync(request.CikisOfisi, request.DonusOfisi, request.DropUcreti, pr.Gun, ct);
            foreach (var s in FeeLineService.CalculatePure(
                group, pr.Gun, request.BasTar, birth, request.IkinciSurucuId is not null, currency, feeNotes, drop))
            {
                var (definitionId, vatRate) = await feeLines.DefinitionInfoAsync(s.TanimKod, ct);
                var fNet = Math.Round(s.BirimNet * s.Gun, 2, MidpointRounding.AwayFromZero);
                var (fVat, fGross) = VatMath.FromNet(fNet, vatRate);
                items.Add(new KiraHesapEkKalem(definitionId ?? Guid.Empty, s.Ad + " (sistem)", s.Gun, fNet, fVat, fGross));
            }
        }

        var extraTotal = items.Sum(k => k.Toplam);
        var grandTotal = pr.Tutar + extraTotal;

        // Kur (yalnız gösterim; kayıt anında KurSnapshot ayrıca çözülür): SabitKur → TCMB (DB'den, dış HTTP yok).
        decimal? exchangeRateValue = null;
        decimal? grandTotalTry = null;
        var code = ExchangeRateService.NormalizeCode(currency);
        if (code == "TRY") { exchangeRateValue = 1m; grandTotalTry = grandTotal; }
        else
        {
            try
            {
                exchangeRateValue = await exchangeRate.GetRateAsync(code, request.BasTar, ct: ct);
                grandTotalTry = Math.Round(grandTotal * exchangeRateValue.Value, 2, MidpointRounding.AwayFromZero);
            }
            catch (ValidationException) { /* kur yok → TL karşılığı gösterilmez; kayıt anında temiz red zaten var */ }
        }

        // Kalan: mevcut kira bağlamında (edit) tahsilat düşülür; şube kapsamı zorlanır (sızıntı yok).
        decimal? collection = null;
        if (request.RentalId is Guid rid)
        {
            var c = await bookings.FindRentalAsync(rid, ct);
            if (c is not null)
            {
                BranchScope.RequireInScope(_currentUser, c.CikisSubeId, c.CikisOfisi);
                collection = c.Tahsilat;
            }
        }
        var remaining = grandTotal - (collection ?? 0m);

        return new KiraHesapSonuc(
            Ok: true, Hata: null,
            Gun: pr.Gun, GunlukUcret: input.GunlukUcret, Tutar: pr.Tutar, Net: net, Kdv: vat,
            HediyeGun: pr.HediyeGun, IskontoTutar: pr.IskontoTutar, HaftaSonuFark: pr.HaftaSonuFark,
            FaturalananGun: pr.FaturalananGun,
            EkKalemler: items, EkHizmetToplam: extraTotal,
            GenelToplam: grandTotal, Doviz: currency, Kur: exchangeRateValue, GenelToplamTl: grandTotalTry,
            Tahsilat: collection, Kalan: remaining,
            Notlar: feeNotes.Count > 0 ? feeNotes : null);
    }

    private static KiraHesapSonuc Invalid(string message, string currency) => new(
        Ok: false, Hata: message, Gun: 0, GunlukUcret: 0m, Tutar: 0m, Net: 0m, Kdv: 0m,
        HediyeGun: null, IskontoTutar: null, HaftaSonuFark: null, FaturalananGun: null,
        EkKalemler: [], EkHizmetToplam: 0m, GenelToplam: 0m, Doviz: currency,
        Kur: null, GenelToplamTl: null, Tahsilat: null, Kalan: 0m);
}
