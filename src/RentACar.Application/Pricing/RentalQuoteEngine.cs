using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.CoverageProducts;
using RentACar.Application.RateMatrices;
using RentACar.Application.RentalRules;
using RentACar.Application.VehicleGroups;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Pricing;

/// <summary>
/// Fiyat Motoru v1 (parite #7): tarife matrisi + araç grubu kuralları + kiralama kuralı + sigorta ürün
/// kataloğundan kalemli kira teklifi hesaplar. SALT-HESAP — deftere/bakiyeye YAZMAZ; rezervasyon/teklif
/// ekranı sonucu sözleşmeye yazabilir. Tüm tutarlar 2 ondalık (kuruş) yuvarlanır (AwayFromZero), KdvMath
/// ile aynı konvansiyon.
///
/// KALİBRASYON NOTU: Gün sayma (<see cref="BookingMath.ComputeGun"/>: 24-saat bloğu yukarı yuvarla) ve
/// kuruş yuvarlama "makul varsayılan"dır — canlı TürevRent Gun_Hesapla/kuruş örneğiyle henüz doğrulanmadı
/// (docs/parite README "Kalibrasyon boşlukları"). Parite örneği gelince burada kalibre edilecek.
///
/// Hesap akışı:
///   gün = ComputeGun(bas,bit)
///   günlükÜcret = tarife matrisi (kanal/şube/grup/tarih eşleşmesi) → gün-kademesi (1..7) fiyatı
///   hediyeGün/iskonto = kiralama kuralı (kapsam+tarih+min/max gün eşleşmesi)
///   faturalananGün = max(0, gün − hediyeGün);  bazTutar = günlükÜcret × faturalananGün
///   kmAşım = max(0, tahminiKm − (günlükKmLimiti × gün)) × aşımKmÜcreti   [araç grubu]
///   sigortaToplam = Σ (ürün.günlükÜcret × min(gün, ürün.maxGün))         [seçili teminat/paket]
///   araToplam = bazTutar + kmAşım + sigortaToplam
///   genelToplam = araToplam − round(araToplam × iskonto%/100)
/// </summary>
public sealed class RentalQuoteEngine(
    RateMatrixService rateMatrices,
    RentalRuleService rentalRules,
    VehicleGroupService vehicleGroups,
    CoverageProductService coverageProducts)
{
    private readonly RateMatrixService _rateMatrices = rateMatrices;
    private readonly RentalRuleService _rentalRules = rentalRules;
    private readonly VehicleGroupService _vehicleGroups = vehicleGroups;
    private readonly CoverageProductService _coverageProducts = coverageProducts;

    private static decimal R(decimal x) => Math.Round(x, 2, MidpointRounding.AwayFromZero);

    public async Task<QuoteResult> QuoteAsync(QuoteRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.AracGrupKod))
            throw new ValidationException("Araç grubu kodu zorunludur.");
        if (req.BitTar <= req.BasTar)
            throw new ValidationException("Bitiş tarihi başlangıçtan sonra olmalıdır.");
        if (req.SurucuYas is < 0) throw new ValidationException("Sürücü yaşı negatif olamaz.");
        if (req.TahminiKm is < 0) throw new ValidationException("Tahmini KM negatif olamaz.");

        var notlar = new List<string>();
        var grupKod = req.AracGrupKod.Trim().ToUpperInvariant();
        var kanal = req.Kanal?.Trim();
        var sube = req.Sube?.Trim();
        var gun = BookingMath.ComputeGun(req.BasTar, req.BitTar);

        // 1) Tarife matrisi → günlük ücret (gün-kademesi)
        var matris = SelectMatrix(await _rateMatrices.ListActiveAsync(ct), grupKod, kanal, sube, req.BasTar);
        decimal gunlukUcret = 0m;
        if (matris is null)
            notlar.Add("Eşleşen tarife matrisi bulunamadı; günlük ücret 0 (manuel girilebilir).");
        else
            gunlukUcret = ResolveTierRate(matris, gun, notlar);

        // Teklif dövizi = tarife matrisinin para birimi (yoksa TRY). KM aşım ücreti (araç grubunda
        // döviz alanı YOK) bu baz dövizde kabul edilir; sigorta ürünleri farklı döviz taşıyamaz (C1).
        var paraBirimi = string.IsNullOrWhiteSpace(matris?.ParaBirimi)
            ? "TRY" : matris!.ParaBirimi!.Trim().ToUpperInvariant();

        // 2) Araç grubu kuralları → KM aşım + provizyon/muafiyet + genç sürücü eşiği (kuraldan bağımsız)
        var grup = (await _vehicleGroups.ListActiveAsync(ct)).FirstOrDefault(g => g.Kod == grupKod);
        decimal kmAsim = 0m, provizyon = 0m, muafiyet = 0m;
        var gencSurucu = false;
        if (grup is null)
            notlar.Add($"'{grupKod}' araç grubu bulunamadı; grup kuralları (KM aşım/provizyon) uygulanmadı.");
        else
        {
            provizyon = grup.Provizyon ?? 0m;
            muafiyet = grup.MuafiyetTutari ?? 0m;
            if (req.TahminiKm is { } km && grup.GunlukKmLimiti is { } limit && limit > 0 && grup.AsimKmUcreti is { } asimUcret)
            {
                var dahilKm = (long)limit * gun;
                var asim = km - dahilKm;
                if (asim > 0) kmAsim = R(asim * asimUcret);
            }
            if (req.SurucuYas is { } yas && grup.GencSurucuYas is { } esik && yas < esik)
            {
                gencSurucu = true;
                notlar.Add($"Genç sürücü (yaş {yas} < {esik}); genç sürücü teminatı önerilir.");
            }
        }

        // 3) Sigorta/ek hizmet kalemleri (kuraldan bağımsız; faturalama gün tavanı = ürün.MaxGun)
        var kalemler = new List<QuoteLine>();
        if (req.SigortaUrunKodlari.Count > 0)
        {
            var urunler = (await _coverageProducts.ListActiveAsync(ct))
                .ToDictionary(u => u.Kod, u => u);
            foreach (var raw in req.SigortaUrunKodlari)
            {
                var k = (raw ?? string.Empty).Trim().ToUpperInvariant();
                if (k.Length == 0) continue;
                if (!urunler.TryGetValue(k, out var u))
                {
                    notlar.Add($"Sigorta ürünü '{k}' bulunamadı.");
                    continue;
                }
                // C1: farklı döviz taşıyan ürün tek teklifte toplanamaz (sessiz yanlış faturalama).
                if (!string.IsNullOrWhiteSpace(u.Doviz) &&
                    !string.Equals(u.Doviz, paraBirimi, StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException(
                        $"Çok-döviz teklif desteklenmiyor: tarife {paraBirimi}, '{u.Kod}' ürünü {u.Doviz}. Aynı döviz seçin.");
                var birim = u.GunlukUcret ?? 0m;
                // M2: MaxGun yalnız POZİTİF ise tavan uygular; 0/negatif → tavansız (tam gün), bedava değil.
                var urunGun = u.MaxGun is { } mg && mg > 0 && mg < gun ? mg : gun;
                kalemler.Add(new QuoteLine(u.Kod, u.Ad, u.Tur, birim, urunGun, R(birim * urunGun)));
            }
        }
        var sigortaToplam = R(kalemler.Sum(l => l.Tutar));

        // 4) Kiralama kuralı → hediye gün + iskonto. KM aşım + sigorta DAHİL gerçek iskonto matrahıyla
        // (araToplam) müşteri lehine en iyi kural seçilir (M-NEW: iskonto yalnız bazdan sayılmaz).
        var kural = SelectRule(await _rentalRules.ListActiveAsync(ct), grupKod, kanal, sube, req.BasTar,
            gun, gunlukUcret, kmAsim + sigortaToplam);
        var hediyeGun = Math.Min(kural?.HediyeGun ?? 0, gun);
        var iskontoOran = kural?.Iskonto ?? 0m;
        var faturalananGun = Math.Max(0, gun - hediyeGun);
        var bazTutar = R(gunlukUcret * faturalananGun);

        // 5) Ara toplam → iskonto → genel toplam. İskonto matrahı = araToplam (baz + km aşım + sigorta).
        var araToplam = R(bazTutar + kmAsim + sigortaToplam);
        var iskontoTutar = R(araToplam * iskontoOran / 100m);
        var genelToplam = R(araToplam - iskontoTutar);

        return new QuoteResult
        {
            Gun = gun,
            HediyeGun = hediyeGun,
            FaturalananGun = faturalananGun,
            GunlukUcret = gunlukUcret,
            BazTutar = bazTutar,
            KmAsimTutar = kmAsim,
            SigortaToplam = sigortaToplam,
            AraToplam = araToplam,
            IskontoOran = iskontoOran,
            IskontoTutar = iskontoTutar,
            GenelToplam = genelToplam,
            ParaBirimi = paraBirimi,
            TarifeKodu = matris?.Kod,
            Provizyon = provizyon,
            Muafiyet = muafiyet,
            GencSurucu = gencSurucu,
            SigortaKalemleri = kalemler,
            Notlar = notlar
        };
    }

    /// <summary>Kanal/şube/grup/tarih eşleşen ONAYLI tarife matrisi. Onaylanmamış (Bekliyor) kullanılmaz.
    /// İstek kanalı/şubesi NULL ise (ör. booking akışı, kanal bilinmiyor) o boyut "hepsini eşle" olur →
    /// kanal/şube-özel matrisler de aday olur (HIGH-2: aksi halde booking sessizce 0 yazardı). Sıralama:
    /// grup-özel &gt; tam-kanal-eşleşme &gt; kanal-agnostik(base) &gt; tam-şube &gt; şube-agnostik &gt; Kod.</summary>
    private static RateMatrix? SelectMatrix(
        IReadOnlyList<RateMatrix> all, string grupKod, string? kanal, string? sube, DateTimeOffset tarih)
        => all.Where(m =>
                m.OnayDurumu == TarifeOnayDurumu.Onayli &&
                (m.AracGrupKod == null || m.AracGrupKod == grupKod) &&
                (m.BasTar == null || m.BasTar <= tarih) &&
                (m.BitTar == null || m.BitTar >= tarih) &&
                (kanal == null || m.Kanal == null || string.Equals(m.Kanal, kanal, StringComparison.OrdinalIgnoreCase)) &&
                (sube == null || m.Sube == null || string.Equals(m.Sube, sube, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.AracGrupKod == grupKod ? 1 : 0)
            .ThenByDescending(m => kanal != null && string.Equals(m.Kanal, kanal, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m.Kanal == null ? 1 : 0)
            .ThenByDescending(m => sube != null && string.Equals(m.Sube, sube, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m.Sube == null ? 1 : 0)
            .ThenBy(m => m.Kod, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Gün-kademesi fiyatı: Gün N (N=clamp(gün,1,7)). O kademe boşsa EN YAKIN dolu kademe
    /// (M1: önce aşağı, sonra yukarı) — yarım-dolu matriste sessiz sıfır baz oluşmaz.</summary>
    private static decimal ResolveTierRate(RateMatrix m, int gun, List<string> notlar)
    {
        var tiers = new[] { m.Gun1, m.Gun2, m.Gun3, m.Gun4, m.Gun5, m.Gun6, m.Gun7 };
        var tier = Math.Clamp(gun, 1, 7);
        for (var t = tier; t >= 1; t--)
            if (tiers[t - 1] is { } v) return v;
        for (var t = tier + 1; t <= 7; t++)
            if (tiers[t - 1] is { } v) return v;
        notlar.Add($"Tarife '{m.Kod}' için gün-kademesi fiyatı tanımlı değil; günlük ücret 0.");
        return 0m;
    }

    /// <summary>Kapsam + tarih + min/max gün eşleşen tek kural seçilir (promosyonlar stack'lenmez).
    /// M3: eşit spesifiklikte MÜŞTERİ LEHİNE en yüksek faydalı kural (hediye-gün değeri + iskonto)
    /// kazanır — daha cömert hediye-gün kampanyası, düşük iskontolu kurala feda edilmez.</summary>
    private static RentalRule? SelectRule(
        IReadOnlyList<RentalRule> all, string grupKod, string? kanal, string? sube,
        DateTimeOffset tarih, int gun, decimal gunlukUcret, decimal digerTutar)
        => all.Where(r =>
                (r.AracGrupKod == null || r.AracGrupKod == grupKod) &&
                (r.Kanal == null || string.Equals(r.Kanal, kanal, StringComparison.OrdinalIgnoreCase)) &&
                (r.Sube == null || string.Equals(r.Sube, sube, StringComparison.OrdinalIgnoreCase)) &&
                (r.GecerlilikBas == null || r.GecerlilikBas <= tarih) &&
                (r.GecerlilikBit == null || r.GecerlilikBit >= tarih) &&
                (r.MinGun == null || gun >= r.MinGun) &&
                (r.MaxGun == null || gun <= r.MaxGun))
            .OrderByDescending(r => r.AracGrupKod == grupKod ? 1 : 0)
            .ThenByDescending(r => RuleBenefit(r, gun, gunlukUcret, digerTutar))
            .ThenBy(r => r.Kod, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Kuralın müşteriye sağladığı tahmini indirim değeri: hediye-gün × günlük ücret +
    /// iskonto% × GERÇEK matrah (faturalanan gün × günlük ücret + KM aşım + sigorta = araToplam).
    /// İskonto matrahı motorda araToplam olduğundan (satır 5), seçim metriği de onunla hizalıdır (M-NEW).</summary>
    private static decimal RuleBenefit(RentalRule r, int gun, decimal gunlukUcret, decimal digerTutar)
    {
        var hediye = Math.Min(r.HediyeGun ?? 0, gun);
        var faturalanan = Math.Max(0, gun - hediye);
        var hediyeDeger = hediye * gunlukUcret;
        var iskontoMatrah = faturalanan * gunlukUcret + digerTutar;
        var iskontoDeger = iskontoMatrah * (r.Iskonto ?? 0m) / 100m;
        return hediyeDeger + iskontoDeger;
    }
}
