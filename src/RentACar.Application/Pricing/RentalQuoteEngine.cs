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
/// KALİBRASYON NOTU: Gün sayma (<see cref="BookingMath.ComputeGun"/>: 24h tam blok + ~3sa kısmi eşiği — referans sistem kalibre) ve
/// kuruş yuvarlama "makul varsayılan"dır — canlı referans sistem Gun_Hesapla/kuruş örneğiyle henüz doğrulanmadı
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
    CoverageProductService coverageProducts,
    DolulukFiyat.IDolulukFiyatKuralRepository dolulukKurallari,
    DolulukFiyat.IOccupancyProvider doluluk)
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
        var matris = SelectMatrix(await _rateMatrices.ListActiveAsync(ct), grupKod, kanal, sube, req.BasTar, gun, notlar);
        decimal gunlukUcret = 0m;
        if (matris is null)
            notlar.Add("Eşleşen tarife matrisi bulunamadı; günlük ücret 0 (manuel girilebilir).");
        else
            gunlukUcret = ResolveTierRate(matris, gun, notlar);

        // FAZ 3.A7: DOLULUK ÇARPANI — ResolveTierRate SONRASI, hediye/iskonto ÖNCESİ bağımsız aşama
        // (kural-seçimi en-avantajlıyı seçtiğinden surge RentalRule'a konamaz — her indirime yenilirdi).
        // Yalnız MOTOR yolu (manuel fiyat asla); kural yoksa KISA DEVRE (doluluk sorgusu atılmaz);
        // rezervasyon-UPDATE reprice'ında DolulukUygula=false (müşteriye verilen fiyat sıçramaz).
        // İskonto matrahı surge'lü baz olur (sonraki tüm hesaplar bu günlük ücretten). TOCTOU bilinçli
        // kabul: doluluk create anında okunur, fiyat sözleşmede kilitlenir.
        if (req.DolulukUygula && gunlukUcret > 0)
        {
            var dolulukAdaylari = (await dolulukKurallari.ListActiveAsync(ct))
                .Where(k => (k.AracGrupKod == null || k.AracGrupKod == grupKod)
                    && (k.GecerlilikBas == null || k.GecerlilikBas <= req.BasTar)
                    && (k.GecerlilikBit == null || k.GecerlilikBit >= req.BasTar)).ToList();
            if (dolulukAdaylari.Count > 0
                && await doluluk.GetGrupDolulukYuzdeAsync(grupKod, req.BasTar, req.BitTar, ct) is { } dolulukYuzde)
            {
                var surge = dolulukAdaylari.Where(k => dolulukYuzde >= k.EsikYuzde)
                    .OrderByDescending(k => k.EsikYuzde).ThenByDescending(k => k.CarpanYuzde)
                    .ThenBy(k => k.Kod, StringComparer.Ordinal).FirstOrDefault();
                if (surge is not null)
                {
                    var carpan = Math.Min(surge.CarpanYuzde, 50m); // uygulama KEMERİ (DB CHECK pantolon askısı)
                    gunlukUcret = R(gunlukUcret * (1m + carpan / 100m));
                    notlar.Add($"Doluluk %{dolulukYuzde:0.##} ≥ %{surge.EsikYuzde} → +%{carpan:0.##} ({surge.Kod}).");
                }
            }
        }

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
        var aktifKurallar = await _rentalRules.ListActiveAsync(ct);
        // FAZ 3.A5: kod girildiyse kural seçimi KODLU kuralla DEĞİŞTİRİLİR (REPLACE — stacking yok);
        // kod geçersiz/kapsam-dışıysa gürültülü red (sessiz otomatiğe düşme yok).
        var kural = string.IsNullOrWhiteSpace(req.KampanyaKodu)
            ? SelectRule(aktifKurallar, grupKod, kanal, sube, req.BasTar,
                gun, gunlukUcret, kmAsim + sigortaToplam, req.MusteriSegment)
            : KodluKuralSec(aktifKurallar, req.KampanyaKodu, grupKod, kanal, sube, req.BasTar,
                gun, req.MusteriSegment, gunlukUcret, kmAsim + sigortaToplam, notlar);
        var hediyeGun = Math.Min(kural?.HediyeGun ?? 0, gun);
        var iskontoOran = kural?.Iskonto ?? 0m;
        var faturalananGun = Math.Max(0, gun - hediyeGun);
        var bazTutar = R(gunlukUcret * faturalananGun);

        // 4b) Hafta sonu farkı (roadmap G3, opt-in): faturalanan dönemdeki Cmt/Pzr günlerine günlük ücret × oran.
        var hsOran = kural?.HaftaSonuFarkOran ?? 0m;
        var hsGun = hsOran > 0m ? WeekendDays(req.BasTar, faturalananGun) : 0;
        var haftaSonuFark = R(gunlukUcret * hsGun * hsOran / 100m);
        if (haftaSonuFark > 0m) notlar.Add($"Hafta sonu farkı: {hsGun} gün × %{hsOran} = {haftaSonuFark}.");

        // 5) Ara toplam → iskonto → genel toplam. İskonto matrahı = araToplam (baz + hafta sonu + km aşım + sigorta).
        var araToplam = R(bazTutar + haftaSonuFark + kmAsim + sigortaToplam);
        var iskontoTutar = R(araToplam * iskontoOran / 100m);
        var genelToplam = R(araToplam - iskontoTutar);

        return new QuoteResult
        {
            Gun = gun,
            HediyeGun = hediyeGun,
            FaturalananGun = faturalananGun,
            GunlukUcret = gunlukUcret,
            BazTutar = bazTutar,
            HaftaSonuFark = haftaSonuFark,
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
        IReadOnlyList<RateMatrix> all, string grupKod, string? kanal, string? sube, DateTimeOffset tarih,
        int gun, List<string> notlar)
    {
        // FAZ-70 — Max Kira Kapsamı: süresi satırın KiraSuresi'ni AŞAN kiralar için o satır ADAY
        // OLMAKTAN ÇIKAR. Eleme SelectMatrix'te yapılır ki motor sıradaki uygun satıra düşebilsin;
        // ResolveTierRate'ten sonra elenseydi geriye fiyatsız kalınırdı.
        //
        // DİKKAT: bu koşul RowMatches'a KONULMAZ. RowMatches'ı halka açık site yayın kapısı da
        // kullanıyor ve orada "kaç günlük kira" diye bir bilgi YOK — koşul oraya sızsaydı vitrin,
        // gün bilgisi olmadığı için tüm gün-sınırlı tarifeleri yanlışlıkla eler ya da geçirirdi.
        var adaylar = all.Where(m => RowMatches(m, grupKod, kanal, sube,
                x => (x.BasTar == null || x.BasTar <= tarih) && (x.BitTar == null || x.BitTar >= tarih)))
            .ToList();

        var elenen = adaylar.Where(m => m.KiraSuresi is { } max && gun > max).ToList();
        foreach (var m in elenen)
            notlar.Add($"Tarife '{m.Kod}' max kira kapsamı ({m.KiraSuresi} gün) aşıldı; satır elendi.");

        return adaylar.Except(elenen)
            .OrderByDescending(m => m.AracGrupKod == grupKod ? 1 : 0)
            .ThenByDescending(m => kanal != null && string.Equals(m.Kanal, kanal, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m.Kanal == null ? 1 : 0)
            .ThenByDescending(m => sube != null && string.Equals(m.Sube, sube, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m.Sube == null ? 1 : 0)
            .ThenBy(m => m.Kod, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Tarife satırının SATIR yüklemi — onay/grup-kod/wildcard/kanal/şube. Tarih koşulu PARAMETRE'dir:
    /// <see cref="SelectMatrix"/> "nokta tarih" (o gün geçerli) geçer, halka açık site kapısı
    /// (<see cref="FiyatlanabilirGruplarAsync"/>) "geçmişte kalmamış" penceresini geçer. Ayrım tek
    /// cümlededir ve bilinçlidir; geri kalan yüklem PAYLAŞILIR — kopyalansaydı iki yüzey zamanla
    /// ayrışırdı (ör. <c>AracGrupKod == null</c> wildcard'ı kapıda unutulur, o gruba özel tarifesi
    /// olmayan ama genel tarifeyle fiyatlanan grup vitrinden yanlışlıkla elenirdi).
    /// </summary>
    private static bool RowMatches(
        RateMatrix m, string grupKod, string? kanal, string? sube, Func<RateMatrix, bool> tarihKosulu)
        => m.OnayDurumu == TarifeOnayDurumu.Onayli &&
           (m.AracGrupKod == null || m.AracGrupKod == grupKod) &&
           tarihKosulu(m) &&
           (kanal == null || m.Kanal == null || string.Equals(m.Kanal, kanal, StringComparison.OrdinalIgnoreCase)) &&
           (sube == null || m.Sube == null || string.Equals(m.Sube, sube, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matriste fiyatlamaya yetecek EN AZ BİR pozitif kademe var mı. Salt "satır var mı"
    /// kontrolü yetmez: tüm kademeleri boş/0 olan bir satır vitrine girer ama aramada
    /// <c>GunlukUcret &lt;= 0</c> filtresine takılır → vitrinde görünen grubun detay linki 404 olurdu.</summary>
    private static bool PozitifKademeVar(RateMatrix m)
        => m.Gun1 > 0 || m.Gun2 > 0 || m.Gun3 > 0 || m.Gun4 > 0 || m.Gun5 > 0 || m.Gun6 > 0 || m.Gun7 > 0
           || m.GunHaftalik > 0 || m.GunAylik > 0;

    /// <summary>
    /// PR-11 — halka açık site YAYIN KAPISI: verilen grup kodlarından hangileri fiyatlanabilir.
    ///
    /// <para><b>Tek sorgu:</b> tarife listesi BİR KEZ yüklenir, yüklem bellekte grup başına
    /// değerlendirilir. Grup başına ayrı çağrı, rate-limit'siz en sıcak anonim sayfada N+1 üretirdi
    /// (<c>RateMatrixService.ListActiveAsync</c> cache'siz — her çağrı gerçek SQL).</para>
    ///
    /// <para><b>Tarih penceresi bilinçli olarak GENİŞ:</b> "bugün geçerli" değil, <b>"geçmişte
    /// kalmamış"</b> (<c>BitTar == null || BitTar &gt;= bugün</c>). Sebep KAPI ⊇ ARAMA kuralı: vitrin
    /// ve detay sayfasının tarihi yoktur, arama ziyaretçinin tarihiyle çalışır. Rent-a-car'da
    /// Haziran–Eylül tarifesi Mart'ta girilir; kapı "bugün"e bakarsa Mart'ta grup vitrinde olmaz ve
    /// <c>/araclar/{id}</c> 404 verir — ama Temmuz araması o grubu bulur ve kartın "Detay" linki
    /// 404'e gider. Geniş pencere bunu kapatır; gelecek sezonun grubunu vitrinde göstermek pazarlama
    /// olarak da doğrudur (kart fiyat basmaz). <c>BasTar</c> koşulu bu yüzden kapıda YOKTUR.</para>
    ///
    /// <para><b>Kanal/şube:</b> ikisi de <c>null</c> = en geniş görünüm. Public arama da
    /// <c>QuoteRequest.Kanal</c> set etmez (bkz. <c>FleetShowcaseService.SearchAvailabilityAsync</c>)
    /// — ikisi de aynı "belirtilmemiş" anlamına gelir ve <see cref="RowMatches"/>'te wildcard'a düşer.</para>
    /// </summary>
    public async Task<HashSet<string>> FiyatlanabilirGruplarAsync(
        IReadOnlyCollection<string> grupKodlari, DateTimeOffset bugun, string? kanal = null, CancellationToken ct = default)
    {
        var sonuc = new HashSet<string>(StringComparer.Ordinal);
        if (grupKodlari.Count == 0) return sonuc;

        var tumu = await _rateMatrices.ListActiveAsync(ct); // TEK sorgu — grup başına değil
        foreach (var ham in grupKodlari)
        {
            var kod = (ham ?? string.Empty).Trim().ToUpperInvariant(); // QuoteAsync ile aynı normalize
            if (kod.Length == 0) continue;
            // Any(...) — FirstOrDefault olsaydı kademesi boş bir satır, aynı gruba uyan dolu satırı
            // gölgeleyip grubu yanlışlıkla eleyebilirdi (liste sırası anlamlı değil).
            if (tumu.Any(m => RowMatches(m, kod, kanal, null, x => x.BitTar == null || x.BitTar >= bugun)
                    && PozitifKademeVar(m)))
                sonuc.Add(kod);
        }
        return sonuc;
    }

    /// <summary>Gün-kademesi fiyatı. UZUN DÖNEM (FAZ 3.A1): 30+ gün → GunAylik (tanımsızsa GunHaftalik'e
    /// düşer), 8-29 gün → GunHaftalik; uzun-dönem kademesi hiç tanımsızsa bugünkü Gun7-clamp davranışı
    /// + NOT (geriye uyum). 1-7 gün: Gün N (N=clamp); o kademe boşsa EN YAKIN dolu kademe (M1: önce
    /// aşağı, sonra yukarı) — yarım-dolu matriste sessiz sıfır baz oluşmaz.
    /// SEKTÖR GERÇEĞİ (A1 adversarial): kademe sınırının hemen altında TOPLAM ters dönebilir
    /// (29×haftalık &gt; 30×aylık) → "uzatmak daha ucuz" BİLGİ notu; otomatik düzeltme YOK (operatör kararı).</summary>
    private static decimal ResolveTierRate(RateMatrix m, int gun, List<string> notlar)
    {
        decimal secilen;
        if (gun >= 30 && (m.GunAylik ?? m.GunHaftalik) is { } uzun)
            secilen = uzun;
        else if (gun is >= 8 and < 30 && m.GunHaftalik is { } haftalik)
            secilen = haftalik;
        else
        {
            if (gun >= 8)
                notlar.Add($"Tarife '{m.Kod}' uzun-dönem kademesi (haftalık/aylık) tanımsız; Gün-7 kademesi uygulandı.");
            var tiers = new[] { m.Gun1, m.Gun2, m.Gun3, m.Gun4, m.Gun5, m.Gun6, m.Gun7 };
            var tier = Math.Clamp(gun, 1, 7);
            decimal? bulunan = null;
            for (var t = tier; t >= 1 && bulunan is null; t--) bulunan = tiers[t - 1];
            for (var t = tier + 1; t <= 7 && bulunan is null; t++) bulunan = tiers[t - 1];
            if (bulunan is null)
            {
                notlar.Add($"Tarife '{m.Kod}' için gün-kademesi fiyatı tanımlı değil; günlük ücret 0.");
                return 0m;
            }
            secilen = bulunan.Value;
        }

        // Ters-dönme bilgisi: bir üst kademe SINIRINA uzatmak toplamda ucuzluyorsa not düş.
        if (gun is >= 8 and < 30 && m.GunAylik is { } ay && 30m * ay < gun * secilen)
            notlar.Add($"Bilgi: 30 güne uzatmak toplamda daha ucuz olur (30×{ay:N2}={30m * ay:N2} < {gun}×{secilen:N2}={gun * secilen:N2}).");
        else if (gun < 8 && m.GunHaftalik is { } hf && 8m * hf < gun * secilen)
            notlar.Add($"Bilgi: 8 güne uzatmak toplamda daha ucuz olur (8×{hf:N2}={8m * hf:N2} < {gun}×{secilen:N2}={gun * secilen:N2}).");

        return secilen;
    }

    /// <summary>[bas, bas+gun) aralığındaki Cumartesi/Pazar gün sayısı (hafta sonu farkı için, roadmap G3).</summary>
    private static int WeekendDays(DateTimeOffset bas, int gun)
    {
        var count = 0;
        for (var i = 0; i < gun; i++)
        {
            var d = bas.Date.AddDays(i).DayOfWeek;
            if (d is DayOfWeek.Saturday or DayOfWeek.Sunday) count++;
        }
        return count;
    }

    /// <summary>Kapsam + tarih + min/max gün eşleşen tek kural seçilir (promosyonlar stack'lenmez).
    /// M3: eşit spesifiklikte MÜŞTERİ LEHİNE en yüksek faydalı kural (hediye-gün değeri + iskonto)
    /// kazanır — daha cömert hediye-gün kampanyası, düşük iskontolu kurala feda edilmez.
    /// KAMPANYA ÇİTİ (FAZ 3.A0 — canlı bug düzeltmesi): KampanyaKodu'lu kural KOD GİRİLMEDEN
    /// otomatik seçime GİRMEZ (kod-kapılı kural en-avantajlı seçimle sessizce uygulanıyordu);
    /// kodla uygulama A5'in (promosyon kodu) işi. Kodsuz kampanya (KampanyaMi, kodsuz) otomatik kalır.
    /// SEGMENT (FAZ 3.A2): MusteriSegment'li kural yalnız o segmentteki müşteriye (Trim+case-insensitive);
    /// null-scope herkese. SIRALAMA KRİTİK: segment-birebir eşleşme RuleBenefit'ten ÖNCE — yoksa
    /// "Problemli → %0" kuralı cömert genel kurala asla kazanamazdı.</summary>
    private static RentalRule? SelectRule(
        IReadOnlyList<RentalRule> all, string grupKod, string? kanal, string? sube,
        DateTimeOffset tarih, int gun, decimal gunlukUcret, decimal digerTutar, string? musteriSegment = null)
        => all.Where(r =>
                string.IsNullOrWhiteSpace(r.KampanyaKodu) &&
                (r.AracGrupKod == null || r.AracGrupKod == grupKod) &&
                (r.Kanal == null || string.Equals(r.Kanal, kanal, StringComparison.OrdinalIgnoreCase)) &&
                (r.Sube == null || string.Equals(r.Sube, sube, StringComparison.OrdinalIgnoreCase)) &&
                (r.MusteriSegment == null || string.Equals(r.MusteriSegment.Trim(),
                    musteriSegment?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                (r.GecerlilikBas == null || r.GecerlilikBas <= tarih) &&
                (r.GecerlilikBit == null || r.GecerlilikBit >= tarih) &&
                (r.MinGun == null || gun >= r.MinGun) &&
                (r.MaxGun == null || gun <= r.MaxGun))
            .OrderByDescending(r => r.AracGrupKod == grupKod ? 1 : 0)
            .ThenByDescending(r => r.MusteriSegment != null ? 1 : 0) // segment-özgü kural fayda kıyasından ÖNCE
            .ThenByDescending(r => RuleBenefit(r, gun, gunlukUcret, digerTutar))
            .ThenBy(r => r.Kod, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Promosyon kodu çözümü (FAZ 3.A5): birebir eşleşme REPLACE — otomatik seçim atlanır,
    /// stacking yok (tek-kural invaryantı korunur). Kapsam (grup/kanal/şube/segment/tarih/gün)
    /// sağlanmazsa GÜRÜLTÜLÜ RED — sessiz yutma yok (uygunsuz kod fiyatı sessizce otomatiğe
    /// düşüremez; operatör alanı düzeltir ya da temizler). Kod açık operatör talimatı olduğundan
    /// otomatik kuraldan DAHA AZ avantajlı olsa da uygulanır (karşılaştırma NOTU düşülür).</summary>
    private static RentalRule KodluKuralSec(
        IReadOnlyList<RentalRule> all, string kod, string grupKod, string? kanal, string? sube,
        DateTimeOffset tarih, int gun, string? musteriSegment, decimal gunlukUcret, decimal digerTutar,
        List<string> notlar)
    {
        var k = kod.Trim();
        var adaylar = all.Where(r => !string.IsNullOrWhiteSpace(r.KampanyaKodu) &&
            string.Equals(r.KampanyaKodu.Trim(), k, StringComparison.OrdinalIgnoreCase)).ToList();
        if (adaylar.Count == 0)
            throw new ValidationException($"Kampanya kodu geçersiz: '{k}'.");

        // Adversarial B4: aynı kod birden çok kuralda (servis artık engelliyor; eski veri kalabilir) —
        // kapsamı UYAN aday dururken diğerinin reddi fırlatılmaz: uyanlar arasından müşteri lehine en
        // faydalısı seçilir + not. Tek adayda aşağıdaki AYRINTILI kapsam redleri anlamlı mesaj verir.
        var kural = adaylar[0];
        if (adaylar.Count > 1)
        {
            kural = adaylar
                .Where(r => KapsamUyar(r, grupKod, kanal, sube, musteriSegment, tarih, gun))
                .OrderByDescending(r => RuleBenefit(r, gun, gunlukUcret, digerTutar))
                .ThenBy(r => r.Kod, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new ValidationException(
                    $"'{k}' kampanyasının hiçbir tanımı bu kiralamanın kapsamına uymuyor (grup/kanal/şube/segment/tarih/gün).");
            notlar.Add($"Uyarı: '{k}' kodu birden çok kuralda tanımlı; kapsamı uyan '{kural.Kod}' uygulandı.");
        }

        if (kural.AracGrupKod != null && kural.AracGrupKod != grupKod)
            throw new ValidationException($"'{k}' kampanyası bu araç grubunda geçerli değil (kapsam: {kural.AracGrupKod}).");
        if (kural.Kanal != null && !string.Equals(kural.Kanal, kanal, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"'{k}' kampanyası bu kanalda geçerli değil (kapsam: {kural.Kanal}).");
        if (kural.Sube != null && !string.Equals(kural.Sube, sube, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"'{k}' kampanyası bu şubede geçerli değil (kapsam: {kural.Sube}).");
        if (kural.MusteriSegment != null && !string.Equals(kural.MusteriSegment.Trim(),
                musteriSegment?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"'{k}' kampanyası bu müşteri segmentinde geçerli değil (kapsam: {kural.MusteriSegment}).");
        if (kural.GecerlilikBas is { } gb && gb > tarih)
            throw new ValidationException($"'{k}' kampanyası henüz başlamadı (başlangıç {gb:dd.MM.yyyy}).");
        if (kural.GecerlilikBit is { } gt && gt < tarih)
            throw new ValidationException($"'{k}' kampanyasının süresi doldu ({gt:dd.MM.yyyy}). Kod alanını temizleyin.");
        if (kural.MinGun is { } min && gun < min)
            throw new ValidationException($"'{k}' kampanyası en az {min} gün kiralamada geçerli (istenen {gun} gün).");
        if (kural.MaxGun is { } max && gun > max)
            throw new ValidationException($"'{k}' kampanyası en çok {max} gün kiralamada geçerli (istenen {gun} gün).");

        var otomatik = SelectRule(all, grupKod, kanal, sube, tarih, gun, gunlukUcret, digerTutar, musteriSegment);
        if (otomatik is not null &&
            RuleBenefit(otomatik, gun, gunlukUcret, digerTutar) > RuleBenefit(kural, gun, gunlukUcret, digerTutar))
            notlar.Add($"Bilgi: otomatik kural '{otomatik.Kod}' kodlu kampanyadan daha avantajlıydı; operatör talimatı (kod) uygulandı.");
        return kural;
    }

    /// <summary>Kodlu kural kapsam predicate'i (çoklu-aday yolu) — ayrıntılı red mesajlarıyla birebir aynı şartlar.</summary>
    private static bool KapsamUyar(RentalRule r, string grupKod, string? kanal, string? sube,
        string? musteriSegment, DateTimeOffset tarih, int gun)
        => (r.AracGrupKod == null || r.AracGrupKod == grupKod)
        && (r.Kanal == null || string.Equals(r.Kanal, kanal, StringComparison.OrdinalIgnoreCase))
        && (r.Sube == null || string.Equals(r.Sube, sube, StringComparison.OrdinalIgnoreCase))
        && (r.MusteriSegment == null || string.Equals(r.MusteriSegment.Trim(), musteriSegment?.Trim(), StringComparison.OrdinalIgnoreCase))
        && (r.GecerlilikBas == null || r.GecerlilikBas <= tarih)
        && (r.GecerlilikBit == null || r.GecerlilikBit >= tarih)
        && (r.MinGun == null || gun >= r.MinGun)
        && (r.MaxGun == null || gun <= r.MaxGun);

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
