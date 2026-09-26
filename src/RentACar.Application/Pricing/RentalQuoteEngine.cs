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
/// KALİBRASYON NOTU: Gün sayma (<see cref="BookingMath.ComputeDays"/>: 24h tam blok + ~3sa kısmi eşiği — referans sistem kalibre) ve
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
    DolulukFiyat.IOccupancyPriceRuleRepository occupancyRules,
    DolulukFiyat.IOccupancyProvider occupancy)
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

        var notes = new List<string>();
        var groupCode = req.AracGrupKod.Trim().ToUpperInvariant();
        var channel = req.Kanal?.Trim();
        var branch = req.Sube?.Trim();
        var day = BookingMath.ComputeDays(req.BasTar, req.BitTar);

        // 1) Tarife matrisi → günlük ücret (gün-kademesi)
        var matrix = SelectMatrix(await _rateMatrices.ListActiveAsync(ct), groupCode, channel, branch, req.BasTar, day, notes);
        decimal dailyFee = 0m;
        if (matrix is null)
            notes.Add("Eşleşen tarife matrisi bulunamadı; günlük ücret 0 (manuel girilebilir).");
        else
            dailyFee = ResolveTierRate(matrix, day, notes);

        // FAZ 3.A7: DOLULUK ÇARPANI — ResolveTierRate SONRASI, hediye/iskonto ÖNCESİ bağımsız aşama
        // (kural-seçimi en-avantajlıyı seçtiğinden surge RentalRule'a konamaz — her indirime yenilirdi).
        // Yalnız MOTOR yolu (manuel fiyat asla); kural yoksa KISA DEVRE (doluluk sorgusu atılmaz);
        // rezervasyon-UPDATE reprice'ında DolulukUygula=false (müşteriye verilen fiyat sıçramaz).
        // İskonto matrahı surge'lü baz olur (sonraki tüm hesaplar bu günlük ücretten). TOCTOU bilinçli
        // kabul: doluluk create anında okunur, fiyat sözleşmede kilitlenir.
        if (req.DolulukUygula && dailyFee > 0)
        {
            // FAZ-73 ŞUBE KAPSAMI: SadeceKendiSubeleri=false (varsayılan + tüm mevcut kayıtlar) →
            // aşağıdaki yüklem SABİT true, yani aday kümesi ve seçilen çarpan göç öncesiyle BİREBİR
            // aynı kalır. Bayrak yalnız kullanıcı açıkça işaretlerse kuralı tek şubeye kısıtlar.
            // %50 tavan formülü DEĞİŞMEDİ (KARARLAR.md FAZ-73) — yalnız ADAYLIK daralır.
            var occupancyCandidates = (await occupancyRules.ListActiveAsync(ct))
                .Where(k => (k.AracGrupKod == null || k.AracGrupKod == groupCode)
                    && (k.GecerlilikBas == null || k.GecerlilikBas <= req.BasTar)
                    && (k.GecerlilikBit == null || k.GecerlilikBit >= req.BasTar)
                    && WarnSurgeBranch(k, branch)).ToList();
            if (occupancyCandidates.Count > 0
                && await occupancy.GetGroupOccupancyPercentAsync(groupCode, req.BasTar, req.BitTar, ct) is { } occupancyPercent)
            {
                var surge = occupancyCandidates.Where(k => occupancyPercent >= k.EsikYuzde)
                    .OrderByDescending(k => k.EsikYuzde).ThenByDescending(k => k.CarpanYuzde)
                    .ThenBy(k => k.Kod, StringComparer.Ordinal).FirstOrDefault();
                if (surge is not null)
                {
                    var multiplier = Math.Min(surge.CarpanYuzde, 50m); // uygulama KEMERİ (DB CHECK pantolon askısı)
                    dailyFee = R(dailyFee * (1m + multiplier / 100m));
                    notes.Add($"Doluluk %{occupancyPercent:0.##} ≥ %{surge.EsikYuzde} → +%{multiplier:0.##} ({surge.Kod}).");
                }
            }
        }

        // Teklif dövizi = tarife matrisinin para birimi (yoksa TRY). KM aşım ücreti (araç grubunda
        // döviz alanı YOK) bu baz dövizde kabul edilir; sigorta ürünleri farklı döviz taşıyamaz (C1).
        var currency = string.IsNullOrWhiteSpace(matrix?.ParaBirimi)
            ? "TRY" : matrix!.ParaBirimi!.Trim().ToUpperInvariant();

        // 2) Araç grubu kuralları → KM aşım + provizyon/muafiyet + genç sürücü eşiği (kuraldan bağımsız)
        var group = (await _vehicleGroups.ListActiveAsync(ct)).FirstOrDefault(g => g.Kod == groupCode);
        decimal kmOverage = 0m, preAuth = 0m, exemption = 0m;
        var youngDriver = false;
        if (group is null)
            notes.Add($"'{groupCode}' araç grubu bulunamadı; grup kuralları (provizyon/muafiyet) uygulanmadı.");
        else
        {
            preAuth = group.Provizyon ?? 0m;
            exemption = group.MuafiyetTutari ?? 0m;
            if (req.SurucuYas is { } yas && group.GencSurucuYas is { } threshold && yas < threshold)
            {
                youngDriver = true;
                notes.Add($"Genç sürücü (yaş {yas} < {threshold}); genç sürücü teminatı önerilir.");
            }
        }

        // FAZ-71: KM limiti/aşım ücreti ÖNCE tarifenin GÜN-KADEMESİNDEN okunur; tarife bu alanları
        // taşımıyorsa (tenant henüz doldurmadıysa) araç grubunun global değerine DÜŞÜLÜR — bugünkü
        // davranış hiç bozulmaz (geriye uyum).
        // Blok BİLİNÇLİ olarak grup dalının DIŞINDA: tarifede km tanımlıyken araç grubu bulunamazsa
        // limit yine de uygulanmalı; grubun içinde kalsaydı tarifedeki değer sessizce yok sayılırdı.
        var (tierKmLimit, tierKmFee) = matrix is null
            ? ((int?)null, (decimal?)null)
            : ResolveTierKm(matrix, day, notes);
        var effectiveKmLimit = tierKmLimit ?? group?.GunlukKmLimiti;
        var effectiveKmFee = tierKmFee ?? group?.AsimKmUcreti;
        if (req.TahminiKm is { } km && effectiveKmLimit is { } limit && limit > 0 && effectiveKmFee is { } excessFee)
        {
            // Limit GÜNLÜKTÜR (kullanıcı kararı): 200 km/gün × 5 gün = 1000 km dahil.
            var includedKm = (long)limit * day;
            var excess = km - includedKm;
            if (excess > 0) kmOverage = R(excess * excessFee);
        }

        // 3) Sigorta/ek hizmet kalemleri (kuraldan bağımsız; faturalama gün tavanı = ürün.MaxGun)
        var items = new List<QuoteLine>();
        if (req.SigortaUrunKodlari.Count > 0)
        {
            var products = (await _coverageProducts.ListActiveAsync(ct))
                .ToDictionary(u => u.Kod, u => u);
            foreach (var raw in req.SigortaUrunKodlari)
            {
                var k = (raw ?? string.Empty).Trim().ToUpperInvariant();
                if (k.Length == 0) continue;
                if (!products.TryGetValue(k, out var u))
                {
                    notes.Add($"Sigorta ürünü '{k}' bulunamadı.");
                    continue;
                }
                // C1: farklı döviz taşıyan ürün tek teklifte toplanamaz (sessiz yanlış faturalama).
                if (!string.IsNullOrWhiteSpace(u.Doviz) &&
                    !string.Equals(u.Doviz, currency, StringComparison.OrdinalIgnoreCase))
                    throw new ValidationException(
                        $"Çok-döviz teklif desteklenmiyor: tarife {currency}, '{u.Kod}' ürünü {u.Doviz}. Aynı döviz seçin.");
                var unit = u.GunlukUcret ?? 0m;
                // M2: MaxGun yalnız POZİTİF ise tavan uygular; 0/negatif → tavansız (tam gün), bedava değil.
                var productDays = u.MaxGun is { } mg && mg > 0 && mg < day ? mg : day;
                items.Add(new QuoteLine(u.Kod, u.Ad, u.Tur, unit, productDays, R(unit * productDays)));
            }
        }
        var insuranceTotal = R(items.Sum(l => l.Tutar));

        // 4) Kiralama kuralı → hediye gün + iskonto. KM aşım + sigorta DAHİL gerçek iskonto matrahıyla
        // (araToplam) müşteri lehine en iyi kural seçilir (M-NEW: iskonto yalnız bazdan sayılmaz).
        var activeRules = await _rentalRules.ListActiveAsync(ct);
        // FAZ 3.A5: kod girildiyse kural seçimi KODLU kuralla DEĞİŞTİRİLİR (REPLACE — stacking yok);
        // kod geçersiz/kapsam-dışıysa gürültülü red (sessiz otomatiğe düşme yok).
        var rule = string.IsNullOrWhiteSpace(req.KampanyaKodu)
            ? SelectRule(activeRules, groupCode, channel, branch, req.BasTar,
                day, dailyFee, kmOverage + insuranceTotal, req.MusteriSegment)
            : SelectCodedRule(activeRules, req.KampanyaKodu, groupCode, channel, branch, req.BasTar,
                day, req.MusteriSegment, dailyFee, kmOverage + insuranceTotal, notes);
        var giftDays = Math.Min(rule?.HediyeGun ?? 0, day);
        var discountRate = rule?.Iskonto ?? 0m;
        var invoicedDays = Math.Max(0, day - giftDays);
        var baseAmount = R(dailyFee * invoicedDays);

        // 4b) Hafta sonu farkı (roadmap G3, opt-in): faturalanan dönemdeki Cmt/Pzr günlerine günlük ücret × oran.
        var weekendRate = rule?.HaftaSonuFarkOran ?? 0m;
        var weekendDays = weekendRate > 0m ? WeekendDays(req.BasTar, invoicedDays) : 0;
        var weekendDifference = R(dailyFee * weekendDays * weekendRate / 100m);
        if (weekendDifference > 0m) notes.Add($"Hafta sonu farkı: {weekendDays} gün × %{weekendRate} = {weekendDifference}.");

        // 5) Ara toplam → iskonto → genel toplam. İskonto matrahı = araToplam (baz + hafta sonu + km aşım + sigorta).
        var subtotal = R(baseAmount + weekendDifference + kmOverage + insuranceTotal);
        var discountAmount = R(subtotal * discountRate / 100m);
        var grandTotal = R(subtotal - discountAmount);

        return new QuoteResult
        {
            Gun = day,
            HediyeGun = giftDays,
            FaturalananGun = invoicedDays,
            GunlukUcret = dailyFee,
            BazTutar = baseAmount,
            HaftaSonuFark = weekendDifference,
            KmAsimTutar = kmOverage,
            SigortaToplam = insuranceTotal,
            AraToplam = subtotal,
            IskontoOran = discountRate,
            IskontoTutar = discountAmount,
            GenelToplam = grandTotal,
            ParaBirimi = currency,
            TarifeKodu = matrix?.Kod,
            Provizyon = preAuth,
            Muafiyet = exemption,
            GencSurucu = youngDriver,
            SigortaKalemleri = items,
            Notlar = notes
        };
    }

    /// <summary>Kanal/şube/grup/tarih eşleşen ONAYLI tarife matrisi. Onaylanmamış (Bekliyor) kullanılmaz.
    /// İstek kanalı/şubesi NULL ise (ör. booking akışı, kanal bilinmiyor) o boyut "hepsini eşle" olur →
    /// kanal/şube-özel matrisler de aday olur (HIGH-2: aksi halde booking sessizce 0 yazardı). Sıralama:
    /// grup-özel &gt; tam-kanal-eşleşme &gt; kanal-agnostik(base) &gt; tam-şube &gt; şube-agnostik &gt; Kod.</summary>
    private static RateMatrix? SelectMatrix(
        IReadOnlyList<RateMatrix> all, string groupCode, string? channel, string? branch, DateTimeOffset date,
        int day, List<string> notes)
    {
        // FAZ-70 — Max Kira Kapsamı: süresi satırın KiraSuresi'ni AŞAN kiralar için o satır ADAY
        // OLMAKTAN ÇIKAR. Eleme SelectMatrix'te yapılır ki motor sıradaki uygun satıra düşebilsin;
        // ResolveTierRate'ten sonra elenseydi geriye fiyatsız kalınırdı.
        //
        // DİKKAT: bu koşul RowMatches'a KONULMAZ. RowMatches'ı halka açık site yayın kapısı da
        // kullanıyor ve orada "kaç günlük kira" diye bir bilgi YOK — koşul oraya sızsaydı vitrin,
        // gün bilgisi olmadığı için tüm gün-sınırlı tarifeleri yanlışlıkla eler ya da geçirirdi.
        var candidates = all.Where(m => RowMatches(m, groupCode, channel, branch,
                x => (x.BasTar == null || x.BasTar <= date) && (x.BitTar == null || x.BitTar >= date)))
            .ToList();

        var excluded = candidates.Where(m => m.KiraSuresi is { } max && day > max).ToList();
        foreach (var m in excluded)
            notes.Add($"Tarife '{m.Kod}' max kira kapsamı ({m.KiraSuresi} gün) aşıldı; satır elendi.");

        return candidates.Except(excluded)
            .OrderByDescending(m => m.AracGrupKod == groupCode ? 1 : 0)
            .ThenByDescending(m => channel != null && string.Equals(m.Kanal, channel, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m.Kanal == null ? 1 : 0)
            .ThenByDescending(m => branch != null && string.Equals(m.Sube, branch, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(m => m.Sube == null ? 1 : 0)
            .ThenBy(m => m.Kod, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Tarife satırının SATIR yüklemi — onay/grup-kod/wildcard/kanal/şube. Tarih koşulu PARAMETRE'dir:
    /// <see cref="SelectMatrix"/> "nokta tarih" (o gün geçerli) geçer, halka açık site kapısı
    /// (<see cref="PriceableGroupsAsync"/>) "geçmişte kalmamış" penceresini geçer. Ayrım tek
    /// cümlededir ve bilinçlidir; geri kalan yüklem PAYLAŞILIR — kopyalansaydı iki yüzey zamanla
    /// ayrışırdı (ör. <c>AracGrupKod == null</c> wildcard'ı kapıda unutulur, o gruba özel tarifesi
    /// olmayan ama genel tarifeyle fiyatlanan grup vitrinden yanlışlıkla elenirdi).
    /// </summary>
    /// <summary>
    /// FAZ-73 — doluluk çarpanı kuralının ŞUBE adaylığı. SAF fonksiyon (test edilebilir).
    ///
    /// <para><c>SadeceKendiSubeleri == false</c> → DAİMA true: kuralın <c>Sube</c> alanı motorda hiç
    /// okunmaz, davranış göç öncesiyle bit-birebir. <c>true</c> → teklifin şubesi kuralın şubesiyle
    /// birebir eşleşmeli (RowMatches ile AYNI konvansiyon: OrdinalIgnoreCase). Teklifte şube yoksa
    /// (şube-agnostik sorgu) şube-kısıtlı kural aday DEĞİLDİR — "belirtilmemiş" bir şubeyi "kendi
    /// şubesi" saymak, kısıtı sessizce delerdi.</para>
    /// </summary>
    public static bool WarnSurgeBranch(DolulukFiyatKural k, string? branch)
        => !k.SadeceKendiSubeleri
           || (!string.IsNullOrWhiteSpace(k.Sube) && !string.IsNullOrWhiteSpace(branch)
               && string.Equals(k.Sube, branch, StringComparison.OrdinalIgnoreCase));

    private static bool RowMatches(
        RateMatrix m, string groupCode, string? channel, string? branch, Func<RateMatrix, bool> dateCondition)
        => m.OnayDurumu == TariffApprovalStatus.Onayli &&
           (m.AracGrupKod == null || m.AracGrupKod == groupCode) &&
           dateCondition(m) &&
           (channel == null || m.Kanal == null || string.Equals(m.Kanal, channel, StringComparison.OrdinalIgnoreCase)) &&
           (branch == null || m.Sube == null || string.Equals(m.Sube, branch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matriste fiyatlamaya yetecek EN AZ BİR pozitif kademe var mı. Salt "satır var mı"
    /// kontrolü yetmez: tüm kademeleri boş/0 olan bir satır vitrine girer ama aramada
    /// <c>GunlukUcret &lt;= 0</c> filtresine takılır → vitrinde görünen grubun detay linki 404 olurdu.</summary>
    private static bool HasPositiveTier(RateMatrix m)
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
    public async Task<HashSet<string>> PriceableGroupsAsync(
        IReadOnlyCollection<string> groupCodes, DateTimeOffset today, string? channel = null, CancellationToken ct = default)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (groupCodes.Count == 0) return result;

        var all = await _rateMatrices.ListActiveAsync(ct); // TEK sorgu — grup başına değil
        foreach (var raw in groupCodes)
        {
            var code = (raw ?? string.Empty).Trim().ToUpperInvariant(); // QuoteAsync ile aynı normalize
            if (code.Length == 0) continue;
            // Any(...) — FirstOrDefault olsaydı kademesi boş bir satır, aynı gruba uyan dolu satırı
            // gölgeleyip grubu yanlışlıkla eleyebilirdi (liste sırası anlamlı değil).
            if (all.Any(m => RowMatches(m, code, channel, null, x => x.BitTar == null || x.BitTar >= today)
                    && HasPositiveTier(m)))
                result.Add(code);
        }
        return result;
    }

    /// <summary>
    /// FAZ-71 — gün-kademesine karşılık gelen KM limiti (GÜNLÜK) + aşım ücreti.
    ///
    /// <para>Kademe seçimi <see cref="ResolveTierRate"/> ile AYNI mantığı izler: 30+ gün → aylık,
    /// 8-29 gün → haftalık, 1-7 gün → Kademe N (clamp) ve o kademe boşsa EN YAKIN dolu kademe
    /// (önce aşağı, sonra yukarı). İki resolver'ın ayrı kural konuşması, fiyatı bir kademeden
    /// km limitini başka kademeden almak demek olurdu.</para>
    ///
    /// <para><b>Uzun dönem AYRI alan (kullanıcı kararı):</b> haftalık/aylık kademeler Km6'ya
    /// DÜŞMEZ, kendi alanlarını kullanır; tanımsızsa sırayla haftalık → Km6 → (çağıranda) araç
    /// grubunun global değeri devreye girer. Böylece uzun kirada limit "son kademeden miras"
    /// kalmaz.</para>
    ///
    /// <para><b>Limit ve ücret AYRI çözülür:</b> tenant yalnız limiti doldurup ücreti boş
    /// bırakabilir (ya da tersi); birini diğerinin varlığına bağlamak, yarım doldurulmuş tarifede
    /// sessizce yanlış kademeye düşürürdü.</para>
    /// </summary>
    private static (int? Limit, decimal? Ucret) ResolveTierKm(RateMatrix m, int day, List<string> notes)
    {
        int? limit;
        decimal? fee;

        if (day >= 30)
        {
            limit = m.KmAylik ?? m.KmHaftalik;
            fee = m.KmAylikUcret ?? m.KmHaftalikUcret;
        }
        else if (day >= 8)
        {
            limit = m.KmHaftalik;
            fee = m.KmHaftalikUcret;
        }
        else
        {
            limit = null;
            fee = null;
        }

        if (limit is null || fee is null)
        {
            // 1-7 gün yolu VE uzun-dönem alanı boş kalan durum: en yakın dolu 1..6 kademesi.
            var limits = new[] { m.Km1, m.Km2, m.Km3, m.Km4, m.Km5, m.Km6 };
            var fees = new[] { m.Km1Ucret, m.Km2Ucret, m.Km3Ucret, m.Km4Ucret, m.Km5Ucret, m.Km6Ucret };
            var tier = Math.Clamp(day, 1, 6);

            for (var t = tier; t >= 1 && limit is null; t--) limit = limits[t - 1];
            for (var t = tier + 1; t <= 6 && limit is null; t++) limit = limits[t - 1];
            for (var t = tier; t >= 1 && fee is null; t--) fee = fees[t - 1];
            for (var t = tier + 1; t <= 6 && fee is null; t++) fee = fees[t - 1];

            if (day >= 8 && limit is not null)
                notes.Add($"Tarife '{m.Kod}' uzun-dönem km limiti tanımsız; kademe km limiti uygulandı.");
        }

        return (limit, fee);
    }

    /// <summary>Gün-kademesi fiyatı. UZUN DÖNEM (FAZ 3.A1): 30+ gün → GunAylik (tanımsızsa GunHaftalik'e
    /// düşer), 8-29 gün → GunHaftalik; uzun-dönem kademesi hiç tanımsızsa bugünkü Gun7-clamp davranışı
    /// + NOT (geriye uyum). 1-7 gün: Gün N (N=clamp); o kademe boşsa EN YAKIN dolu kademe (M1: önce
    /// aşağı, sonra yukarı) — yarım-dolu matriste sessiz sıfır baz oluşmaz.
    /// SEKTÖR GERÇEĞİ (A1 adversarial): kademe sınırının hemen altında TOPLAM ters dönebilir
    /// (29×haftalık &gt; 30×aylık) → "uzatmak daha ucuz" BİLGİ notu; otomatik düzeltme YOK (operatör kararı).</summary>
    private static decimal ResolveTierRate(RateMatrix m, int day, List<string> notes)
    {
        decimal selected;
        if (day >= 30 && (m.GunAylik ?? m.GunHaftalik) is { } longText)
            selected = longText;
        else if (day is >= 8 and < 30 && m.GunHaftalik is { } weekly)
            selected = weekly;
        else
        {
            if (day >= 8)
                notes.Add($"Tarife '{m.Kod}' uzun-dönem kademesi (haftalık/aylık) tanımsız; Gün-7 kademesi uygulandı.");
            var tiers = new[] { m.Gun1, m.Gun2, m.Gun3, m.Gun4, m.Gun5, m.Gun6, m.Gun7 };
            var tier = Math.Clamp(day, 1, 7);
            decimal? found = null;
            for (var t = tier; t >= 1 && found is null; t--) found = tiers[t - 1];
            for (var t = tier + 1; t <= 7 && found is null; t++) found = tiers[t - 1];
            if (found is null)
            {
                notes.Add($"Tarife '{m.Kod}' için gün-kademesi fiyatı tanımlı değil; günlük ücret 0.");
                return 0m;
            }
            selected = found.Value;
        }

        // Ters-dönme bilgisi: bir üst kademe SINIRINA uzatmak toplamda ucuzluyorsa not düş.
        if (day is >= 8 and < 30 && m.GunAylik is { } month && 30m * month < day * selected)
            notes.Add($"Bilgi: 30 güne uzatmak toplamda daha ucuz olur (30×{month:N2}={30m * month:N2} < {day}×{selected:N2}={day * selected:N2}).");
        else if (day < 8 && m.GunHaftalik is { } hf && 8m * hf < day * selected)
            notes.Add($"Bilgi: 8 güne uzatmak toplamda daha ucuz olur (8×{hf:N2}={8m * hf:N2} < {day}×{selected:N2}={day * selected:N2}).");

        return selected;
    }

    /// <summary>[bas, bas+gun) aralığındaki Cumartesi/Pazar gün sayısı (hafta sonu farkı için, roadmap G3).</summary>
    private static int WeekendDays(DateTimeOffset start, int day)
    {
        var count = 0;
        for (var i = 0; i < day; i++)
        {
            var d = start.Date.AddDays(i).DayOfWeek;
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
        IReadOnlyList<RentalRule> all, string groupCode, string? channel, string? branch,
        DateTimeOffset date, int day, decimal dailyFee, decimal otherAmount, string? customerSegment = null)
        => all.Where(r =>
                string.IsNullOrWhiteSpace(r.KampanyaKodu) &&
                (r.AracGrupKod == null || r.AracGrupKod == groupCode) &&
                (r.Kanal == null || string.Equals(r.Kanal, channel, StringComparison.OrdinalIgnoreCase)) &&
                (r.Sube == null || string.Equals(r.Sube, branch, StringComparison.OrdinalIgnoreCase)) &&
                (r.MusteriSegment == null || string.Equals(r.MusteriSegment.Trim(),
                    customerSegment?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                (r.GecerlilikBas == null || r.GecerlilikBas <= date) &&
                (r.GecerlilikBit == null || r.GecerlilikBit >= date) &&
                (r.MinGun == null || day >= r.MinGun) &&
                (r.MaxGun == null || day <= r.MaxGun))
            .OrderByDescending(r => r.AracGrupKod == groupCode ? 1 : 0)
            .ThenByDescending(r => r.MusteriSegment != null ? 1 : 0) // segment-özgü kural fayda kıyasından ÖNCE
            .ThenByDescending(r => RuleBenefit(r, day, dailyFee, otherAmount))
            .ThenBy(r => r.Kod, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>Promosyon kodu çözümü (FAZ 3.A5): birebir eşleşme REPLACE — otomatik seçim atlanır,
    /// stacking yok (tek-kural invaryantı korunur). Kapsam (grup/kanal/şube/segment/tarih/gün)
    /// sağlanmazsa GÜRÜLTÜLÜ RED — sessiz yutma yok (uygunsuz kod fiyatı sessizce otomatiğe
    /// düşüremez; operatör alanı düzeltir ya da temizler). Kod açık operatör talimatı olduğundan
    /// otomatik kuraldan DAHA AZ avantajlı olsa da uygulanır (karşılaştırma NOTU düşülür).</summary>
    private static RentalRule SelectCodedRule(
        IReadOnlyList<RentalRule> all, string code, string groupCode, string? channel, string? branch,
        DateTimeOffset date, int day, string? customerSegment, decimal dailyFee, decimal otherAmount,
        List<string> notes)
    {
        var k = code.Trim();
        var candidates = all.Where(r => !string.IsNullOrWhiteSpace(r.KampanyaKodu) &&
            string.Equals(r.KampanyaKodu.Trim(), k, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0)
            throw new ValidationException($"Kampanya kodu geçersiz: '{k}'.");

        // Adversarial B4: aynı kod birden çok kuralda (servis artık engelliyor; eski veri kalabilir) —
        // kapsamı UYAN aday dururken diğerinin reddi fırlatılmaz: uyanlar arasından müşteri lehine en
        // faydalısı seçilir + not. Tek adayda aşağıdaki AYRINTILI kapsam redleri anlamlı mesaj verir.
        var rule = candidates[0];
        if (candidates.Count > 1)
        {
            rule = candidates
                .Where(r => WarnScope(r, groupCode, channel, branch, customerSegment, date, day))
                .OrderByDescending(r => RuleBenefit(r, day, dailyFee, otherAmount))
                .ThenBy(r => r.Kod, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new ValidationException(
                    $"'{k}' kampanyasının hiçbir tanımı bu kiralamanın kapsamına uymuyor (grup/kanal/şube/segment/tarih/gün).");
            notes.Add($"Uyarı: '{k}' kodu birden çok kuralda tanımlı; kapsamı uyan '{rule.Kod}' uygulandı.");
        }

        if (rule.AracGrupKod != null && rule.AracGrupKod != groupCode)
            throw new ValidationException($"'{k}' kampanyası bu araç grubunda geçerli değil (kapsam: {rule.AracGrupKod}).");
        if (rule.Kanal != null && !string.Equals(rule.Kanal, channel, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"'{k}' kampanyası bu kanalda geçerli değil (kapsam: {rule.Kanal}).");
        if (rule.Sube != null && !string.Equals(rule.Sube, branch, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"'{k}' kampanyası bu şubede geçerli değil (kapsam: {rule.Sube}).");
        if (rule.MusteriSegment != null && !string.Equals(rule.MusteriSegment.Trim(),
                customerSegment?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ValidationException($"'{k}' kampanyası bu müşteri segmentinde geçerli değil (kapsam: {rule.MusteriSegment}).");
        if (rule.GecerlilikBas is { } gb && gb > date)
            throw new ValidationException($"'{k}' kampanyası henüz başlamadı (başlangıç {gb:dd.MM.yyyy}).");
        if (rule.GecerlilikBit is { } gt && gt < date)
            throw new ValidationException($"'{k}' kampanyasının süresi doldu ({gt:dd.MM.yyyy}). Kod alanını temizleyin.");
        if (rule.MinGun is { } min && day < min)
            throw new ValidationException($"'{k}' kampanyası en az {min} gün kiralamada geçerli (istenen {day} gün).");
        if (rule.MaxGun is { } max && day > max)
            throw new ValidationException($"'{k}' kampanyası en çok {max} gün kiralamada geçerli (istenen {day} gün).");

        var auto = SelectRule(all, groupCode, channel, branch, date, day, dailyFee, otherAmount, customerSegment);
        if (auto is not null &&
            RuleBenefit(auto, day, dailyFee, otherAmount) > RuleBenefit(rule, day, dailyFee, otherAmount))
            notes.Add($"Bilgi: otomatik kural '{auto.Kod}' kodlu kampanyadan daha avantajlıydı; operatör talimatı (kod) uygulandı.");
        return rule;
    }

    /// <summary>Kodlu kural kapsam predicate'i (çoklu-aday yolu) — ayrıntılı red mesajlarıyla birebir aynı şartlar.</summary>
    private static bool WarnScope(RentalRule r, string groupCode, string? channel, string? branch,
        string? customerSegment, DateTimeOffset date, int day)
        => (r.AracGrupKod == null || r.AracGrupKod == groupCode)
        && (r.Kanal == null || string.Equals(r.Kanal, channel, StringComparison.OrdinalIgnoreCase))
        && (r.Sube == null || string.Equals(r.Sube, branch, StringComparison.OrdinalIgnoreCase))
        && (r.MusteriSegment == null || string.Equals(r.MusteriSegment.Trim(), customerSegment?.Trim(), StringComparison.OrdinalIgnoreCase))
        && (r.GecerlilikBas == null || r.GecerlilikBas <= date)
        && (r.GecerlilikBit == null || r.GecerlilikBit >= date)
        && (r.MinGun == null || day >= r.MinGun)
        && (r.MaxGun == null || day <= r.MaxGun);

    /// <summary>Kuralın müşteriye sağladığı tahmini indirim değeri: hediye-gün × günlük ücret +
    /// iskonto% × GERÇEK matrah (faturalanan gün × günlük ücret + KM aşım + sigorta = araToplam).
    /// İskonto matrahı motorda araToplam olduğundan (satır 5), seçim metriği de onunla hizalıdır (M-NEW).</summary>
    private static decimal RuleBenefit(RentalRule r, int day, decimal dailyFee, decimal otherAmount)
    {
        var gift = Math.Min(r.HediyeGun ?? 0, day);
        var invoiced = Math.Max(0, day - gift);
        var giftValue = gift * dailyFee;
        var discountTaxBase = invoiced * dailyFee + otherAmount;
        var discountValue = discountTaxBase * (r.Iskonto ?? 0m) / 100m;
        return giftValue + discountValue;
    }
}
