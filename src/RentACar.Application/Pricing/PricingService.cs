using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;

namespace RentACar.Application.Pricing;

/// <summary>
/// Fiyat çözüm adaptörü (booking akışı için ince facade): rezervasyon/teklif/kira oluştururken EFEKTİF
/// günlük ücret + tutar çözer. TEK fiyat motoru = <see cref="RentalQuoteEngine"/> (tarife matrisi).
/// Kural: manuel ücret (>0) DAİMA kazanır (geriye-uyumlu) — İSTİSNA: FiyatTuru=="Otomatik" ise
/// manuel ücret sunucu tarafında yok sayılır ve tarife çözülemezse temiz red (ValidationException);
/// aksi halde aracın grubuna göre tarife
/// matrisinden (onaylı) günlük ücret çözülür. Eşleşme yoksa **geriye-uyum fallback**: eski
/// <see cref="RateCardService"/> (DEPRECATED — yeni tarifeler RateMatrix'e). Hiçbiri yoksa 0 (manuel girilir).
/// Defter/bakiye YAZMAZ — yalnız tutar hesaplar.
///
/// Yan etki: <see cref="PriceAsync"/> auto-fiyat bulduğunda input.GunlukUcret'i günceller (çağıran servis
/// efektif ücreti sözleşmeye yazsın diye). (roadmap A1: RentalQuoteEngine birincil; RateCard deprecate.)
///
/// KAPSAM (önemli): FiyatTuru=="Otomatik" ise motorun TAM teklifi (baz + hediye-gün + iskonto + hafta-sonu →
/// Tutar; KM aşım/sigorta MATRAHA GİRMEZ, booking QuoteRequest'inde yok — KURAL A) sözleşmeye yansır + döküm
/// alanları dolar (HediyeGun/IskontoTutar/HaftaSonuFark/FaturalananGun). Otomatik DEĞİLKEN (legacy blank-rate)
/// bu facade YALNIZ günlük baz ücreti çözer → Tutar = gün × baz (iskontosuz, döküm null; eski davranış).
/// KM aşım dönüşte (ReturnMath), ek hizmet RentalAddOn'da. Çok-döviz: matris TRY değilse auto UYGULANMAZ
/// (booking tek-döviz) → 0 (Otomatik ise temiz red).
/// </summary>
public sealed class PricingService(
    IVehicleRepository vehicles, RentalQuoteEngine quoteEngine, RateCardService rateCards,
    Customers.ICustomerRepository customers, ReservationSources.IReservationSourceRepository kaynaklar)
{
    private readonly IVehicleRepository _vehicles = vehicles;
    private readonly RentalQuoteEngine _quoteEngine = quoteEngine;
    private readonly RateCardService _rateCards = rateCards;
    private readonly Customers.ICustomerRepository _customers = customers;
    private readonly ReservationSources.IReservationSourceRepository _kaynaklar = kaynaklar;

    /// <summary>
    /// Gün + tutar döner; gerekiyorsa input.GunlukUcret'i tarife matrisinden gelen efektif ücretle
    /// günceller. Manuel ücret verilmişse (&gt;0) motora/tarifeye bakılmaz — TEK İSTİSNA:
    /// FiyatTuru=="Otomatik" ise manuel ücret SUNUCU tarafında yok sayılır (tarife tek gerçek
    /// kaynak) ve tarife çözülemezse (matris yok / TRY-dışı matris) temiz redle
    /// <see cref="ValidationException"/> atılır — sessiz 0-TL sözleşme oluşmaz. Tetikleyici bu
    /// ortak facade'da olduğundan üç create yolu (kira/rezervasyon/teklif) + rezervasyon update
    /// otomatik kapsanır.
    /// </summary>
    /// <summary>Fiyatlanmış kira: gün + TUTAR (baz kira brütü) + bilgi amaçlı bileşenler (hediye gün / iskonto /
    /// hafta sonu farkı / faturalanan gün). Bileşenler yalnız tarife matrisi (Otomatik) çözdüğünde dolu; manuel
    /// veya RateCard fallback'te null. Tutar = motorun GenelToplam'ı — KURAL A: booking yolunda km-tahmini +
    /// sigorta MATRAHA GİRMEZ (req'te TahminiKm/SigortaUrunKodlari yok) → Tutar = bazTutar + haftaSonuFark −
    /// iskonto = TEMİZ baz brüt. Bileşenler Tutar'a AYRICA katılmaz (çift-sayım yok); RentalTotals.BaseGross +
    /// ReturnMath zaten yalnız Tutar'ı okur → tutarlı.</summary>
    public sealed record PricedRental(
        int Gun, decimal Tutar, int? HediyeGun, decimal? IskontoTutar, decimal? HaftaSonuFark, int? FaturalananGun);

    public async Task<PricedRental> PriceAsync(BookingInput input, CancellationToken ct = default)
    {
        var gun = BookingMath.ComputeGun(input.BasTar, input.BitTar);

        // "Otomatik" fiyat türü: manuel ücret yok sayılır → daima tarife çözümü.
        var otomatik = string.Equals(input.FiyatTuru?.Trim(), "Otomatik", StringComparison.OrdinalIgnoreCase);
        if (otomatik) input.GunlukUcret = 0m;

        // FAZ 3.A5: kampanya kodu yalnız motor (Otomatik) yolunda uygulanabilir — manuel/legacy fiyat
        // yolunda SESSİZCE yutulması para kaçağı sınıfıdır → gürültülü red (Otomatik seçilir ya da alan
        // temizlenir; kod her fiyatlamada motorca yeniden doğrulanır).
        if (!string.IsNullOrWhiteSpace(input.KampanyaKodu) && !otomatik)
            throw new ValidationException("Kampanya kodu yalnız 'Otomatik' fiyat türünde uygulanır; manuel fiyatla birlikte kullanılamaz.");

        if (input.GunlukUcret <= 0)
        {
            var vehicle = await _vehicles.FindAsync(input.VehicleId, ct);
            var grup = vehicle?.Grup?.Trim();
            if (!string.IsNullOrWhiteSpace(grup))
            {
                // TEK motor çağrısı. CikisOfisi (şube) → şube-özel matris (HIGH-2).
                // FAZ 3.A2: segment cariden ÇÖZÜLÜR (Customer.Sinif) — tetikleyici bu ortak facade'da
                // olduğundan 3 create yolu + rezervasyon-update otomatik kapsanır; reprice CARİNİN
                // GÜNCEL sınıfını kullanır (sınıf sonradan değişirse yeni fiyat yeni segmentten).
                var segment = input.MusteriId != Guid.Empty
                    ? (await _customers.FindAsync(input.MusteriId, ct))?.Sinif : null;
                // FAZ 3.A4: Kaynak → Kanal. YALNIZ aktif ReservationSource (Kod/Ad, case-insensitive)
                // eşleşirse geçer; eşleşmezse null (yazım hatası/spoof serbest-metin kanal-özel tarife
                // SEÇTİREMEZ). Kanal setliyken yabancı-kanal matrisleri elenir; kanalsız istekte
                // kanal-agnostik (base) matris tercih edilir (SelectMatrix sıralaması — mevcut davranış).
                var kanal = await KanalCozAsync(input.Kaynak, ct);
                var q = input.BitTar > input.BasTar
                    ? await _quoteEngine.QuoteAsync(new QuoteRequest
                        {
                            AracGrupKod = grup, Kanal = kanal, Sube = input.CikisOfisi,
                            BasTar = input.BasTar, BitTar = input.BitTar, MusteriSegment = segment,
                            KampanyaKodu = input.KampanyaKodu
                        }, ct)
                    : null;
                if (q?.TarifeKodu is not null)
                {
                    // Matris EŞLEŞTİ. TRY ise efektif günlük ücreti çöz; TRY-dışı → booking tek-döviz → 0 kalır
                    // (RateCard'a DÜŞME — MEDIUM-1 kararı); Otomatik ise aşağıda temiz red.
                    if (string.Equals(q.ParaBirimi, "TRY", StringComparison.OrdinalIgnoreCase) && q.GunlukUcret > 0)
                    {
                        input.GunlukUcret = q.GunlukUcret; // efektif günlük ücret sözleşmeye
                        // TAM teklif (iskonto/hediye/hafta-sonu → Tutar + döküm) YALNIZ "Otomatik" seçildiğinde
                        // (adversarial M1: aksi halde her boş-ücretli booking sessizce promosyon uygulardı; legacy
                        // blank-rate yolu yalnız günlük ücreti çözer → gün × baz, iskontosuz, döküm null).
                        if (otomatik)
                            return new PricedRental(q.Gun, q.GenelToplam,
                                q.HediyeGun > 0 ? q.HediyeGun : null,
                                q.IskontoTutar > 0 ? q.IskontoTutar : null,
                                q.HaftaSonuFark > 0 ? q.HaftaSonuFark : null,
                                q.HediyeGun > 0 ? q.FaturalananGun : null);
                    }
                }
                else
                {
                    // FAZ 3.A5 adversarial B1 (High): kodlu fiyat YALNIZ tarife matrisiyle çözülür —
                    // RateCard fallback'i kuralları bilmez; kod sessizce etkisiz kalır + iz yazılırdı
                    // (indirimsiz tutar, sözleşmede "kod uygulandı" yanılsaması) → gürültülü red.
                    if (!string.IsNullOrWhiteSpace(input.KampanyaKodu))
                        throw new ValidationException(
                            "Kampanya kodu yalnız tarife matrisiyle fiyatlanan kirada uygulanır; tarife tanımlayın veya kodu temizleyin.");
                    // Matris YOK → geriye-uyum fallback: eski RateCard (DEPRECATED; bileşen yok).
#pragma warning disable CS0618
                    var card = await _rateCards.GetRateAsync(grup, gun, input.BasTar, ct);
#pragma warning restore CS0618
                    if (card?.GunlukUcret is { } r && r > 0) input.GunlukUcret = r;
                }
            }
        }

        // Otomatik seçildi ama tarife çözülemedi → temiz red. Otomatik DEĞİLKEN 0 kalması mevcut davranıştır.
        if (otomatik && input.GunlukUcret <= 0)
            throw new ValidationException("Otomatik tarife bulunamadı; manuel fiyat girin veya tarife tanımlayın.");

        // KDV MODU (yalnız Otomatik DEĞİLKEN — Otomatik motor/RateCard brütü zaten çözdü). GunlukUcret DAİMA
        // brüte (KDV-dahil) normalize edilir → ExtendAsync (gün × GunlukUcret) tutarlı; Tutar hep brüt (fatura
        // BaseGross→FromGross ile net'i ayrıştırır → mod niyeti korunur). KURAL B: 3 create yolu bu facade'dan.
        var tutar = otomatik ? KdvMath.RoundGross(gun * input.GunlukUcret) : KdvModuUygula(input, gun);
        return new PricedRental(gun, tutar, null, null, null, null);
    }

    /// <summary>Kaynak metnini doğrulanmış kanala çevirir (FAZ 3.A4): boş → null; aktif
    /// ReservationSource'larda Kod VEYA Ad ile (Trim + case-insensitive) eşleşirse Trim'li metin
    /// döner (matris Kanal alanı aynı metinle eşleşir), eşleşmezse null — tanımsız kaynak kanal-özel
    /// tarife seçtiremez (çit; sessiz yanlış-tarife yerine base matris).</summary>
    private async Task<string?> KanalCozAsync(string? kaynak, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kaynak)) return null;
        var k = kaynak.Trim();
        var aktifler = await _kaynaklar.ListActiveAsync(ct);
        return aktifler.Any(s =>
            string.Equals(s.Kod, k, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Ad, k, StringComparison.OrdinalIgnoreCase)) ? k : null;
    }

    /// <summary>FiyatTuru moduna göre brüt Tutar; GunlukUcret'i brüte normalize eder (yan etki). Modlar:
    /// "KDV Dahil Günlük"/varsayılan → günlük ücret zaten brüt (Tutar = gün×brüt); "Günlük" → girilen NET günlük
    /// → brüte çevir; "KDV Dahil Toplam" → girilen BRÜT toplam (gün-bağımsız), günlük türet; "Toplam" → girilen
    /// NET toplam → brüte çevir, günlük türet. Toplam modlarında Tutar=girilen toplam AUTORİTE; türetilen günlük
    /// yuvarlandığından gün×günlük Tutar'dan gün×0.005'e kadar sapabilir (adversarial Bulgu-4) — para ıraksaması
    /// DEFTERE girmez (fatura/cari Tutar'ı okur), yalnız uzatmada türetilen günlük + ekran. Yan etki: GunlukUcret
    /// mutasyonu net modlarda idempotent DEĞİL (aynı input'u iki kez fiyatlarsa çift grossup — adversarial Bulgu-2);
    /// mevcut çağıranlar tek kez fiyatlar (rez/teklif update formu FiyatTuru göndermez → default brüt dalı).</summary>
    private static decimal KdvModuUygula(BookingInput input, int gun)
    {
        var mod = (input.FiyatTuru ?? string.Empty).Trim();
        var oran = KdvMath.VarsayilanOran;
        bool Es(string x) => string.Equals(mod, x, StringComparison.OrdinalIgnoreCase);

        if (Es("Günlük")) // NET günlük ücret → brüt
        {
            input.GunlukUcret = KdvMath.RoundGross(input.GunlukUcret * (1 + oran));
            return KdvMath.RoundGross(gun * input.GunlukUcret);
        }
        if (Es("KDV Dahil Toplam") || Es("Toplam")) // girilen değer TOPLAM (gün-bağımsız)
        {
            var tutar = Es("Toplam")
                ? KdvMath.RoundGross(input.GunlukUcret * (1 + oran)) // NET toplam → brüt
                : KdvMath.RoundGross(input.GunlukUcret);             // zaten brüt toplam
            input.GunlukUcret = gun > 0 ? KdvMath.RoundGross(tutar / gun) : tutar; // uzatma için günlük türet
            return tutar;
        }
        // "KDV Dahil Günlük" / null / bilinmeyen → günlük ücret zaten brüt (mevcut davranış).
        return KdvMath.RoundGross(gun * input.GunlukUcret);
    }

    /// <summary>
    /// Aracın grubu + tarih aralığı için efektif günlük ücret. BİRİNCİL: <see cref="RentalQuoteEngine"/>
    /// (onaylı tarife matrisi gün-kademesi). Eşleşme yoksa geriye-uyum: eski RateCard (deprecated).
    /// Grup yoksa ya da hiçbir tarife yoksa 0.
    /// </summary>
    public async Task<decimal> ResolveDailyRateAsync(
        Guid vehicleId, DateTimeOffset basTar, DateTimeOffset bitTar, string? sube = null, CancellationToken ct = default)
    {
        var vehicle = await _vehicles.FindAsync(vehicleId, ct);
        if (vehicle?.Grup is not { } grup || string.IsNullOrWhiteSpace(grup)) return 0m;

        // Birincil: tarife matrisi motoru. Kanal=null (booking'de kanal yok) → engine "her kanalı eşle".
        if (bitTar > basTar)
        {
            var q = await _quoteEngine.QuoteAsync(
                new QuoteRequest { AracGrupKod = grup, Sube = sube, BasTar = basTar, BitTar = bitTar }, ct);
            // Matris EŞLEŞTİYSE (TarifeKodu dolu) onun sonucu kesin → RateCard fallback'e DÜŞME (MEDIUM-1).
            if (q.TarifeKodu is not null)
            {
                // Çok-döviz booking'de desteklenmiyor (RentalContract tek-döviz) → yalnız TRY auto-uygula (HIGH-1).
                return string.Equals(q.ParaBirimi, "TRY", StringComparison.OrdinalIgnoreCase) ? q.GunlukUcret : 0m;
            }
        }

        // Matris YOK → geriye-uyum fallback: eski RateCard (DEPRECATED).
        var gun = BookingMath.ComputeGun(basTar, bitTar);
#pragma warning disable CS0618 // RateCard fiyat çözümü deprecated; bilinçli geriye-uyum fallback'i.
        var card = await _rateCards.GetRateAsync(grup, gun, basTar, ct);
#pragma warning restore CS0618
        return card?.GunlukUcret ?? 0m;
    }
}
