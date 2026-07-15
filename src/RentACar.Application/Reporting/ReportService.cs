using RentACar.Application.Pricing;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Reporting;

/// <summary>
/// Salt-okunur finansal raporlama — çift-taraflı defter (AccountLedgerEntry) ÜSTÜNDE toplama.
/// Yeni tablo/yazım YOK. DB erişimi <see cref="IReportRepository"/>'de; burası saf toplama
/// (yürüyen bakiye, özet, kırılım). Tutarlar base para (Amount×Rate).
///
/// Semantik: Kasa/Banka bakiye = Σ (Borç +base, Alacak −base). Gelir = Σ Alacak(Gelir),
/// Gider = Σ Borç(Gider), KDV tahsil = Σ Alacak(Kdv), KDV indirilecek = Σ Borç(Kdv).
/// </summary>
public sealed class ReportService(IReportRepository repository, TutSatEsikleri tutSatEsikleri)
{
    private readonly IReportRepository _repository = repository;
    private readonly TutSatEsikleri _tutSat = tutSatEsikleri;

    /// <summary>
    /// Araç-bazlı kârlılık raporu (roadmap B2): defterden türetilen Gelir/Gider satırları (repo'da
    /// SourceId→Fatura→Kira→Araç atfı). Opsiyonel şube/grup/plaka filtresi (filtre varsa "(Atanmamış)"
    /// satırı hariç). Filtresiz toplamlar defter Gelir/Gider toplamıyla MUTABIK (invariant). NetKar desc sıralı.
    /// </summary>
    public async Task<KarlilikDto> GetKarlilikAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        string? sube = null, string? grup = null, string? plaka = null, CancellationToken ct = default)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        var rows = await _repository.GetKarlilikRowsAsync(from, to, ct);

        var filtreVar = !string.IsNullOrWhiteSpace(sube) || !string.IsNullOrWhiteSpace(grup) || !string.IsNullOrWhiteSpace(plaka);
        IEnumerable<KarlilikSatirDto> q = rows;
        if (filtreVar)
            q = rows.Where(r => r.VehicleId != null
                && (string.IsNullOrWhiteSpace(sube) || string.Equals(r.Sube, sube.Trim(), OIC))
                && (string.IsNullOrWhiteSpace(grup) || string.Equals(r.Grup, grup.Trim(), OIC))
                && (string.IsNullOrWhiteSpace(plaka) || r.Plaka.Contains(plaka.Trim(), OIC)));

        var list = q.OrderByDescending(r => r.NetKar).ThenBy(r => r.Plaka, StringComparer.OrdinalIgnoreCase).ToList();
        return new KarlilikDto(list, list.Sum(r => r.Gelir), list.Sum(r => r.Gider), list.Sum(r => r.NetKar));
    }

    /// <summary>Çok-boyutlu kârlılık özeti (roadmap #2): araç-bazlı P&amp;L satırlarını bir boyuta göre toplar.
    /// boyut: "grup"|"sube"|"segment" (varsayılan grup). Yalnız araca atfedilmiş satırlar (VehicleId!=null) —
    /// "(Atanmamış)" gruba dahil edilmez (boyut değeri yok). Aggregation saf gruplama; para mantığı KarlilikDto'dan.</summary>
    public async Task<KarlilikOzetDto> GetKarlilikOzetAsync(
        string boyut, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = (await _repository.GetKarlilikRowsAsync(from, to, ct)).Where(r => r.VehicleId != null).ToList();
        var b = (boyut ?? "grup").Trim().ToLowerInvariant();
        string ad = b switch { "sube" => "Şube", "segment" => "Segment", _ => "Grup" };
        string Key(KarlilikSatirDto r) => b switch
        {
            "sube" => string.IsNullOrWhiteSpace(r.Sube) ? "(Şubesiz)" : r.Sube!.Trim(),
            "segment" => string.IsNullOrWhiteSpace(r.Segment) ? "(Segmentsiz)" : r.Segment!.Trim(),
            _ => string.IsNullOrWhiteSpace(r.Grup) ? "(Grupsuz)" : r.Grup!.Trim()
        };
        var satirlar = rows.GroupBy(Key)
            .Select(g => new KarlilikOzetSatirDto(g.Key, g.Count(), g.Sum(r => r.Gelir), g.Sum(r => r.Gider), g.Sum(r => r.NetKar)))
            .OrderByDescending(s => s.NetKar).ThenBy(s => s.Boyut, StringComparer.CurrentCulture).ToList();
        return new KarlilikOzetDto(ad, satirlar, satirlar.Sum(s => s.Gelir), satirlar.Sum(s => s.Gider), satirlar.Sum(s => s.NetKar));
    }

    /// <summary>
    /// Araç karnesi (araç ön muhasebe 360°): tek aracın defterden P&L'i (yıllık kırılım + gelir-kaynak +
    /// gider-kategori) + kaynak-varlık olay zaman çizelgesi. Toplamlar o aracın Karlilik satırıyla MUTABIK
    /// (atıf kuralları birebir; parite testi kilitler). Araç yoksa/başka tenant'sa null (sayfa 404).
    /// Saf toplama — DB erişimi repo'da; KPI/amortisman bloğu ayrı artışta eklenir.
    /// </summary>
    public async Task<AracKarneDto?> GetAracKarneAsync(
        Guid vehicleId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var raw = await _repository.GetAracKarneRawAsync(vehicleId, from, to, ct);
        if (raw.Vehicle is null) return null;
        var v = raw.Vehicle;

        var header = new AracKarneHeaderDto(
            v.Id, v.Plaka, v.Marka, v.Tip, v.Grup, v.Segment, v.Sube, v.AracSahibi, v.Durum, v.Km,
            v.AlimBedeli, v.AlimTarihi, v.IkinciElDeger, v.FiloGirisTarih, v.FiloCikisTarih,
            v.SonBakimTarih, v.SonBakimKm);

        var toplamGelir = raw.Gelirler.Sum(g => g.Tutar);
        var toplamGider = raw.Giderler.Sum(g => g.Tutar);

        // Yıllık P&L: gelir ∪ gider yıllarının birleşimi (yalnız gelirli/yalnız giderli yıl kaybolmaz).
        var yillik = raw.Gelirler.Select(g => g.Yil).Concat(raw.Giderler.Select(g => g.Yil))
            .Distinct().OrderBy(y => y)
            .Select(y =>
            {
                var ge = raw.Gelirler.Where(g => g.Yil == y).Sum(g => g.Tutar);
                var gi = raw.Giderler.Where(g => g.Yil == y).Sum(g => g.Tutar);
                return new AracYilPnlRow(y, ge, gi, ge - gi);
            }).ToList();

        // Kırılım yüzdesi: tutar ÷ toplam gelir (kurumsal "% of revenue"). Yalnız POZİTİF gelirde
        // anlamlı — dönem-net'i 0/negatifse (iade > gelir) yüzde yanıltır → null (adversarial F1).
        decimal? Pct(decimal t) => toplamGelir > 0m
            ? Math.Round(t * 100m / toplamGelir, 2, MidpointRounding.AwayFromZero) : null;
        var gelirKaynak = raw.Gelirler.GroupBy(g => g.Kaynak)
            .Select(g => new AracKirilimRow(g.Key, g.Sum(x => x.Tutar), Pct(g.Sum(x => x.Tutar))))
            .OrderByDescending(r => r.Tutar).ToList();
        var giderKategori = raw.Giderler.GroupBy(g => g.Kategori)
            .Select(g => new AracKirilimRow(g.Key, g.Sum(x => x.Tutar), Pct(g.Sum(x => x.Tutar))))
            .OrderByDescending(r => r.Tutar).ToList();

        var netKar = toplamGelir - toplamGider;
        // KPI para girdileri ÖMÜR-BOYU (raw.OmurGelir/OmurGider) — sayfanın dönem filtresi P&L'i daraltır
        // ama KPI paydaları/payları daralmaz (adversarial F2: karışık-payda sızıntısı kapatıldı).
        var kpi = AracKpiHesapla(v, raw, out var sureAy);

        // Amortisman/başabaş modeli: yalnız AlimBedeli>0 (MaliyetHesapService guard'ları ValidationException
        // atar — null bırakılır). Ömür-boyu holding varsayımı; FaizOran=0 (uydurma finansman yazılmaz),
        // Residual = IkinciElDeger/AlimBedeli (yoksa varsayılan 0.30), AylikGider = gerçekleşen ortalama.
        // TEK payda: model her yerde AYNI clamp'lenmiş ayı kullanır (>120 ayda karışık-payda çarpıklığı olmaz).
        MaliyetHesapSonuc? maliyetModel = null;
        if (v.AlimBedeli is > 0m)
        {
            var modelAy = Math.Clamp(sureAy, 1, 120);
            maliyetModel = MaliyetHesapService.Hesapla(new MaliyetHesapInput
            {
                AlisBedeli = v.AlimBedeli.Value,
                SureAy = modelAy,
                ResidualYuzde = v.IkinciElDeger is > 0m
                    ? Math.Clamp(v.IkinciElDeger.Value / v.AlimBedeli.Value, 0m, 1m) : 0.30m,
                AylikGider = decimal.Round(raw.OmurGider / modelAy, 2, MidpointRounding.AwayFromZero),
                FaizOran = 0m, DamgaOran = 0m
            });
        }

        var tutSat = TutSatHesap.Hesapla(raw.TutSatHam, v.IkinciElDeger, raw.GrupOrtDegerOrani, _tutSat);
        // FAZ 2.3: başabaş GÜNLÜK (model) = BasaBasAylik / 30.44 — Kpi.Adr ile kıyas satırı
        // (aynı 30.44 gün/ay paydası; model yoksa null → UI "—" gösterir, uydurma değer yok).
        decimal? basaBasGunluk = maliyetModel is null ? null
            : decimal.Round(maliyetModel.BasaBasAylik / 30.44m, 2, MidpointRounding.AwayFromZero);
        // FAZ 2.4: kalıntı projeksiyonu (azalan bakiye; salt-hesap, deftere yazmaz).
        var kalinti = KalintiProjeksiyon.Hesapla(v.AlimBedeli, v.IkinciElDeger, v.AlimTarihi, DateTimeOffset.UtcNow);

        // FAZ 2.5: dönemsel km — km-log serisinden pencere farkı. BİLGİ satırı: KPI km-maliyeti
        // ömür-boyu tanımını KORUR (karışık-payda yasak). Tanım: pencere-içi SON log −
        // (pencere-öncesi SON log ?? pencere-içi İLK log); maliyet = dönem P&L gideri ÷ dönem km.
        int? donemKm = null; decimal? donemKmMaliyet = null;
        if (raw.KmLoglari is { Count: > 0 } loglar)
        {
            var bitSiniri = to ?? DateTimeOffset.UtcNow;
            var pencereIci = loglar.Where(k => k.Tarih <= bitSiniri && (from is null || k.Tarih >= from)).ToList();
            if (pencereIci.Count > 0)
            {
                var taban = from is { } f
                    ? loglar.Where(k => k.Tarih < f).Select(k => (int?)k.Km).LastOrDefault() ?? pencereIci[0].Km
                    : pencereIci[0].Km;
                donemKm = Math.Max(0, pencereIci[^1].Km - taban);
                if (donemKm > 0 && toplamGider > 0m)
                    donemKmMaliyet = decimal.Round(toplamGider / donemKm.Value, 2, MidpointRounding.AwayFromZero);
            }
        }

        return new AracKarneDto(header, toplamGelir, toplamGider, netKar,
            yillik, gelirKaynak, giderKategori, raw.Olaylar, kpi, maliyetModel, tutSat, basaBasGunluk, kalinti,
            donemKm, donemKmMaliyet);
    }

    /// <summary>
    /// Kurumsal KPI bloğu — sahiplik penceresi (ömür boyu; sayfa dönem-filtresinden bağımsız, raw KPI hamı
    /// da öyle). Gün matematiği GetDolulukAsync ile aynı: .UtcDateTime.Date + kapsayıcı OverlapDays.
    /// Oranlar yalnız pozitif paydayla anlamlı; aksi null (negatif dönem-net'i yüzdesi dersi).
    /// sureAy = sahiplik günü / 30.44 yuvarlanmış (yaklaşık takvim ayı), min 1 — amortisman/başabaş paydası.
    /// </summary>
    private static AracKpiDto AracKpiHesapla(Vehicle v, AracKarneRawDto raw, out int sureAy)
    {
        // Para girdileri ömür-boyu — dönem filtresinden bağımsız (DTO sözleşmesi).
        var gelir = raw.OmurGelir;
        var gider = raw.OmurGider;
        var netKar = gelir - gider;
        DateTimeOffset? wBas = v.FiloGirisTarih ?? v.AlimTarihi;
        DateTimeOffset? wBit = v.FiloCikisTarih
            ?? (v.Durum == VehicleStatus.Satildi ? raw.SonSatisTarih : null)
            ?? DateTimeOffset.UtcNow;

        int sahiplik = 0, kiralanan = 0, servis = 0;
        if (wBas is { } wb && wBit is { } we)
        {
            var basD = wb.UtcDateTime.Date;
            var bitD = we.UtcDateTime.Date;
            sahiplik = bitD >= basD ? (bitD - basD).Days + 1 : 0;
            kiralanan = raw.KiraAraliklari.Sum(r =>
                OverlapDays(r.Bas.UtcDateTime.Date, r.Bit.UtcDateTime.Date, basD, bitD));
            var now = DateTimeOffset.UtcNow.UtcDateTime.Date;
            servis = raw.ServisAraliklari.Sum(a =>
                OverlapDays(a.Giris.UtcDateTime.Date, (a.Cikis?.UtcDateTime.Date ?? now), basD, bitD));
        }
        sureAy = Math.Max(1, (int)Math.Round(sahiplik / 30.44, MidpointRounding.AwayFromZero));

        decimal? R2(decimal? x) => x is { } d ? decimal.Round(d, 2, MidpointRounding.AwayFromZero) : null;
        // Gerçekleşen amortisman: SATILMIŞ araçta kalıntı realize edildi ve satış geliri netKar'ın İÇİNDE →
        // tam AlimBedeli düşülür (yoksa kalıntı çift sayılır — adversarial F3). Aktif araçta tahmin:
        // AlimBedeli − IkinciElDeger (yalnız >0; 0/negatif "veri yok" — modelin 0.30 varsayımıyla çelişmesin).
        var satilmis = v.Durum == VehicleStatus.Satildi && raw.SonSatisTarih is not null;
        var amortisman = v.AlimBedeli is > 0m
            ? satilmis ? v.AlimBedeli
                       : v.IkinciElDeger is > 0m ? v.AlimBedeli.Value - v.IkinciElDeger.Value : (decimal?)null
            : null;

        return new AracKpiDto(
            SahiplikGun: sahiplik,
            KiralananGun: kiralanan,
            ServisGun: servis,
            BosGun: Math.Max(0, sahiplik - kiralanan - servis),
            // Doluluk 100 ile sınırlanır: geç dönüş sonraki sözleşmeyle çakışabilir (veri gerçeği) —
            // kurumsal panoda >%100 doluluk güven zedeler (adversarial F4).
            DolulukYuzde: sahiplik > 0 ? R2(Math.Min(100m, kiralanan * 100m / sahiplik)) : null,
            RevPacd: sahiplik > 0 ? R2(gelir / sahiplik) : null,
            Adr: kiralanan > 0 ? R2(gelir / kiralanan) : null,
            KmBasinaMaliyet: raw.ToplamKatedilenKm > 0 ? R2(gider / raw.ToplamKatedilenKm) : null,
            NetMarjYuzde: gelir > 0m ? R2(netKar * 100m / gelir) : null,
            // ROI: satılmışta yaşam-döngüsü KAPANIŞ getirisi (satış netKar'da, alım düşülür — çift sayım yok);
            // aktifte defter ROI (amortisman EkonomikKar satırında ayrıca görünür).
            RoiYuzde: v.AlimBedeli is > 0m
                ? R2((satilmis ? netKar - v.AlimBedeli.Value : netKar) * 100m / v.AlimBedeli.Value) : null,
            GeriOdemeAy: GeriOdemeAyHesapla(v.AlimBedeli, netKar, sureAy, sahiplik),
            Tco: (v.AlimBedeli ?? 0m) + gider,
            GerceklesenAmortisman: amortisman,
            AylikAmortisman: amortisman is { } a2 ? R2(a2 / sureAy) : null,
            EkonomikKar: amortisman is { } a3 ? netKar - a3 : null,
            ToplamKatedilenKm: raw.ToplamKatedilenKm,
            KiraSayisi: raw.KiraSayisi);
    }

    /// <summary>
    /// Filo analiz panosu: dönem-pencereli araç P&L satırları (Karlilik ile mutabık) + ÖMÜR-BOYU KPI
    /// sütunları (Doluluk cap-100 / ROI [satılmışta kapanış] / km-maliyet — karne semantiğiyle birebir,
    /// karışık-payda yok) + yaş kohortu (alım tarihi kovası → ort. km-maliyet & doluluk).
    /// siralama: "net"(vars.) | "zarar" | "doluluk" | "roi". Toplamlar (satır+Atanmamış) defterle mutabık.
    /// </summary>
    public async Task<FiloAnalizDto> GetFiloAnalizAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? siralama = null,
        CancellationToken ct = default)
    {
        var raw = await _repository.GetFiloAnalizRawAsync(from, to, ct);
        var omurByVeh = raw.KarlilikOmur.Where(r => r.VehicleId != null).ToDictionary(r => r.VehicleId!.Value);
        var pencereByVeh = raw.KarlilikPencere.Where(r => r.VehicleId != null).ToDictionary(r => r.VehicleId!.Value);
        var kiraByVeh = raw.Kiralar.GroupBy(k => k.VehicleId).ToDictionary(g => g.Key, g => g.ToList());
        var aracById = raw.Araclar.ToDictionary(a => a.Id);
        var simdi = DateTimeOffset.UtcNow;

        // Satırlar TÜM filodan tohumlanır (adversarial F-D): dönemde hareketi olmayan araç 0 P&L ile
        // görünür — gizli zararlı / hiç kiralanmamış araç panodan kaçmaz; kohort filoyu sayar.
        var rows = new List<FiloAnalizRow>();
        foreach (var a in raw.Araclar)
        {
            var p = pencereByVeh.TryGetValue(a.Id, out var pr) ? pr : null;
            var om = omurByVeh.TryGetValue(a.Id, out var o) ? o : null;
            var omurNet = (om?.Gelir ?? 0m) - (om?.Gider ?? 0m);
            var kiralar = kiraByVeh.TryGetValue(a.Id, out var ks) ? ks : [];

            int sahiplik = 0, kiralanan = 0; int? yasAy = null;
            decimal? doluluk = null, roi = null, kmMaliyet = null;
            DateTimeOffset? wBas = a.FiloGirisTarih ?? a.AlimTarihi;
            var wBit = a.FiloCikisTarih
                ?? (a.Durum == VehicleStatus.Satildi ? a.SonSatisTarih : null) ?? simdi;
            if (wBas is { } wb)
            {
                var basD = wb.UtcDateTime.Date;
                var bitD = wBit.UtcDateTime.Date;
                sahiplik = bitD >= basD ? (bitD - basD).Days + 1 : 0;
                kiralanan = kiralar.Sum(k => OverlapDays(k.Bas.UtcDateTime.Date, k.Bit.UtcDateTime.Date, basD, bitD));
                if (sahiplik > 0)
                    doluluk = decimal.Round(Math.Min(100m, kiralanan * 100m / sahiplik), 2, MidpointRounding.AwayFromZero);
            }
            var satilmis = a.Durum == VehicleStatus.Satildi && a.SonSatisTarih is not null;
            if (a.AlimBedeli is > 0m)
                roi = decimal.Round((satilmis ? omurNet - a.AlimBedeli.Value : omurNet) * 100m / a.AlimBedeli.Value,
                    2, MidpointRounding.AwayFromZero);
            if (a.AlimTarihi is { } at)
            {
                var son = (a.Durum == VehicleStatus.Satildi ? a.SonSatisTarih : null) ?? simdi;
                // Gün-hassas ay farkı (adversarial F-E): gün-of-ay geçmemişse ay tamamlanmadı sayılır
                // (362 günlük araç "1-2 yıl" kovasına düşmesin).
                var ay = (son.Year - at.Year) * 12 + son.Month - at.Month - (son.Day < at.Day ? 1 : 0);
                yasAy = Math.Max(0, ay);
            }
            var km = kiralar.Where(k => k.CikisKm != null && k.DonusKm != null)
                .Sum(k => k.DonusKm!.Value - k.CikisKm!.Value);
            if (km > 0 && om is not null)
                kmMaliyet = decimal.Round(om.Gider / km, 2, MidpointRounding.AwayFromZero);

            // FAZ 2.2: tut/sat sinyali — grup ortalaması aşağıda TÜM satırlar kurulduktan sonra
            // hesaplanacağından burada ham+İkinciEl saklanır; sinyal ikinci geçişte eklenir.
            rows.Add(new FiloAnalizRow(a.Id, a.Plaka, a.Grup, a.Segment, a.Sube,
                p?.Gelir ?? 0m, p?.Gider ?? 0m, p?.NetKar ?? 0m,
                doluluk, roi, kmMaliyet, sahiplik, kiralanan, yasAy));
        }

        // FAZ 2.2/2.3 ikinci geçiş — grup ortalamaları ORTAK helper'dan (GrupOrtalama; iki kopya yasak):
        // (2.2) gider/değer oranı → tut/sat kural-b; (2.3) km-maliyet → SinifEndeks (1.00 = sınıf ort.).
        var tutSatByVeh = raw.TutSatHam.ToDictionary(x => x.VehicleId);
        var ikinciElByVeh = raw.Araclar.ToDictionary(a => a.Id, a => a.IkinciElDeger);
        var grupOrtDegerOrani = GrupOrtalama.Hesapla(raw.Araclar, a => a.Grup,
            a => a.IkinciElDeger is > 0m
                ? tutSatByVeh.GetValueOrDefault(a.Id, new FiloTutSatRow(a.Id, 0m, 0m, 0, 0)).Gider12 / a.IkinciElDeger.Value
                : null);
        var grupOrtKmMaliyet = GrupOrtalama.Hesapla(rows, r => r.Grup, r => r.KmBasinaMaliyet);
        rows = rows.Select(r =>
        {
            var ham = tutSatByVeh.GetValueOrDefault(r.VehicleId, new FiloTutSatRow(r.VehicleId, 0m, 0m, 0, 0));
            var sinyal = TutSatHesap.Hesapla(ham, ikinciElByVeh.GetValueOrDefault(r.VehicleId),
                GrupOrtalama.Deger(grupOrtDegerOrani, r.Grup), _tutSat);
            decimal? endeks = null;
            if (r.KmBasinaMaliyet is { } km && GrupOrtalama.Deger(grupOrtKmMaliyet, r.Grup) is { } go && go > 0m)
                endeks = decimal.Round(km / go, 2, MidpointRounding.AwayFromZero);
            return r with { TutSatSinyal = sinyal.Sinyal, SinifEndeks = endeks };
        }).ToList();

        // FAZ 2.3: filo-geneli HAVUZ KPI — Σ havuzlardan (satır KPI'larının ortalaması DEĞİL; karışık-payda
        // yasak). Ömür semantik: gelir = Σ araç ömür geliri (defter), günler = Σ sahiplik/kiralanan.
        // Silinmiş-araç kalıntısı eklenmeden ÖNCE hesaplanır (paydasız gelir RevPACD'yi şişirmesin).
        var havuzSahiplik = rows.Sum(r => r.SahiplikGun);
        var havuzKiralanan = rows.Sum(r => r.KiralananGun);
        var havuzGelir = rows.Sum(r => omurByVeh.TryGetValue(r.VehicleId, out var og) ? og.Gelir : 0m);
        var havuzKpi = new FiloHavuzKpiDto(havuzSahiplik, havuzKiralanan, havuzGelir,
            havuzSahiplik > 0 ? decimal.Round(Math.Min(100m, havuzKiralanan * 100m / havuzSahiplik), 2, MidpointRounding.AwayFromZero) : null,
            havuzSahiplik > 0 ? decimal.Round(havuzGelir / havuzSahiplik, 2, MidpointRounding.AwayFromZero) : null,
            havuzKiralanan > 0 ? decimal.Round(havuzGelir / havuzKiralanan, 2, MidpointRounding.AwayFromZero) : null);

        // FAZ 2.4: tut/sat-adayı özeti — sinyal ≥2 araçlar + 12-ay-sonu tahmini kalıntı toplamı
        // (KalintiProjeksiyon; İkinciEl'siz aday projeksiyona katılmaz — uydurma taban yok).
        var adaylar = rows.Where(r => r.TutSatSinyal >= 2)
            .Select(r => aracById.GetValueOrDefault(r.VehicleId)).Where(a => a is not null).ToList();
        var tutSatAday = new TutSatAdayOzetDto(adaylar.Count,
            adaylar.Sum(a => KalintiProjeksiyon.Hesapla(a!.AlimBedeli, a.IkinciElDeger, a.AlimTarihi, simdi)?.Deger12Ay ?? 0m));
        // Silinmiş aracın defter kalıntısı: satır olarak korunur (Σ satır + Atanmamış = defter mutabakatı),
        // KPI'sız; kohorta girmez, karne linki çizilmez ("(bilinmeyen araç)").
        foreach (var p in raw.KarlilikPencere.Where(x => x.VehicleId is Guid vid && !aracById.ContainsKey(vid)))
            rows.Add(new FiloAnalizRow(p.VehicleId!.Value, p.Plaka, p.Grup, p.Segment, p.Sube,
                p.Gelir, p.Gider, p.NetKar, null, null, null, 0, 0, null));

        rows = (siralama ?? "net").Trim().ToLowerInvariant() switch
        {
            "zarar" => [.. rows.OrderBy(x => x.NetKar).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            "doluluk" => [.. rows.OrderByDescending(x => x.DolulukYuzde ?? -1m).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            "roi" => [.. rows.OrderByDescending(x => x.RoiYuzde ?? decimal.MinValue).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            "tutsat" => [.. rows.OrderByDescending(x => x.TutSatSinyal).ThenBy(x => x.NetKar).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            _ => [.. rows.OrderByDescending(x => x.NetKar).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)]
        };

        var atanmamis = raw.KarlilikPencere.FirstOrDefault(x => x.VehicleId == null);

        static string Kova(int? yasAy) => yasAy switch
        {
            null => "Alım tarihi yok",
            < 12 => "0-1 yıl",
            < 24 => "1-2 yıl",
            < 36 => "2-3 yıl",
            _ => "3+ yıl"
        };
        static int KovaSira(string k) => k switch
        { "0-1 yıl" => 0, "1-2 yıl" => 1, "2-3 yıl" => 2, "3+ yıl" => 3, _ => 4 };
        static decimal? Ort(IEnumerable<decimal?> xs)
        {
            var v = xs.Where(x => x != null).Select(x => x!.Value).ToList();
            return v.Count > 0 ? decimal.Round(v.Average(), 2, MidpointRounding.AwayFromZero) : null;
        }
        var kohort = rows.Where(x => aracById.ContainsKey(x.VehicleId)).GroupBy(x => Kova(x.YasAy))
            .Select(g => new FiloKohortRow(g.Key, g.Count(),
                Ort(g.Select(x => x.KmBasinaMaliyet)), Ort(g.Select(x => x.DolulukYuzde))))
            .OrderBy(k => KovaSira(k.Kova)).ToList();

        // İnvaryant: satırlar + Atanmamış = dönem defter toplamı (Karlilik ile aynı).
        var toplamGelir = rows.Sum(x => x.Gelir) + (atanmamis?.Gelir ?? 0m);
        var toplamGider = rows.Sum(x => x.Gider) + (atanmamis?.Gider ?? 0m);
        return new FiloAnalizDto(rows, toplamGelir, toplamGider, toplamGelir - toplamGider,
            atanmamis?.Gelir ?? 0m, atanmamis?.Gider ?? 0m, kohort, havuzKpi, tutSatAday);
    }

    /// <summary>Geri-ödeme ayı: alım bedelinin aylık net kârla amortismanı. TAMAMEN decimal hesap —
    /// (int) cast taşması yok (adversarial F1: 1-kuruş net + milyonluk araç OverflowException veriyordu).
    /// 1200 aydan (100 yıl) uzun geri ödeme pratikte "geri ödemez" → null.</summary>
    private static int? GeriOdemeAyHesapla(decimal? alimBedeli, decimal netKar, int sureAy, int sahiplik)
    {
        if (netKar <= 0m || alimBedeli is not > 0m || sahiplik <= 0) return null;
        var aylikNet = netKar / sureAy;
        if (aylikNet <= 0m) return null;
        var ay = Math.Ceiling(alimBedeli.Value / aylikNet);
        return ay > 1200m ? null : (int)ay;
    }

    /// <summary>Bir hesabın (Kasa/Banka) defteri: tarihe göre sıralı, yürüyen bakiyeli.</summary>
    public async Task<IReadOnlyList<LedgerLineDto>> GetAccountLedgerAsync(
        LedgerAccountType type, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLedgerRowsAsync([type], from, to, ct);

        var result = new List<LedgerLineDto>(rows.Count);
        decimal running = 0m;
        foreach (var r in rows.OrderBy(r => r.Tarih))
        {
            var borc = r.Direction == LedgerDirection.Debit ? r.Base : 0m;
            var alacak = r.Direction == LedgerDirection.Credit ? r.Base : 0m;
            running += borc - alacak;
            result.Add(new LedgerLineDto(r.Tarih, r.SourceType, r.Aciklama, borc, alacak, running));
        }
        return result;
    }

    /// <summary>Kasa & banka giriş/çıkış/bakiye özeti.</summary>
    public async Task<CashboxSummaryDto> GetKasaBankaSummaryAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLedgerRowsAsync(
            [LedgerAccountType.Kasa, LedgerAccountType.Banka], from, to, ct);

        decimal kasaGiris = Sum(rows, LedgerAccountType.Kasa, LedgerDirection.Debit);
        decimal kasaCikis = Sum(rows, LedgerAccountType.Kasa, LedgerDirection.Credit);
        decimal bankaGiris = Sum(rows, LedgerAccountType.Banka, LedgerDirection.Debit);
        decimal bankaCikis = Sum(rows, LedgerAccountType.Banka, LedgerDirection.Credit);
        return new CashboxSummaryDto(
            kasaGiris, kasaCikis, kasaGiris - kasaCikis,
            bankaGiris, bankaCikis, bankaGiris - bankaCikis);
    }

    /// <summary>Home mini-trend (FAZ 6.2): son n ayın ay-pencereli GELİR toplamı (GetGelirGiderAsync ile
    /// aynı netleme — iade düşer). Ay çıpası UTC ayın 1'i; pencere [ayBas, sonrakiAyBas) — GetLedgerRows
    /// üst-ucu DAHİL olduğundan bitiş AddTicks(-1) ile verilir (sınır kaydı iki aya sayılmaz).</summary>
    public async Task<IReadOnlyList<AylikGelirNokta>> GetAylikGelirTrendAsync(
        int aySayisi = 6, DateTimeOffset? simdi = null, CancellationToken ct = default)
    {
        var s = simdi ?? DateTimeOffset.UtcNow;
        var buAy = new DateTimeOffset(s.Year, s.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var sonuc = new List<AylikGelirNokta>(aySayisi);
        for (int i = aySayisi - 1; i >= 0; i--)
        {
            var ayBas = buAy.AddMonths(-i);
            var gg = await GetGelirGiderAsync(ayBas, ayBas.AddMonths(1).AddTicks(-1), ct);
            sonuc.Add(new AylikGelirNokta(ayBas, gg.GelirToplam));
        }
        return sonuc;
    }

    /// <summary>Dönem gelir-gider özeti + KDV + net kâr + SourceType kırılımı.</summary>
    public async Task<GelirGiderDto> GetGelirGiderAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLedgerRowsAsync(
            [LedgerAccountType.Gelir, LedgerAccountType.Gider, LedgerAccountType.Kdv], from, to, ct);

        // İade faturası TERS kayıt yazar (Borç Gelir / Borç KDV) → gelir ve tahsil edilen KDV netleşir.
        var gelirCredit = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Credit).ToList();
        var gelirDebit = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Debit).ToList();
        // Gider de İKİ YÖNLÜ netlenir (4.3 adversarial Medium): DisHizmet iptali Gider'e Credit yazan
        // ilk akış — Debit-only toplam iptal sonrası hayalet gider bırakıp Karlilik/karne ile mutabakatı kırıyordu.
        var giderDebit = rows.Where(r => r.AccountType == LedgerAccountType.Gider && r.Direction == LedgerDirection.Debit).ToList();
        var giderCredit = rows.Where(r => r.AccountType == LedgerAccountType.Gider && r.Direction == LedgerDirection.Credit).ToList();

        decimal gelir = gelirCredit.Sum(r => r.Base) - gelirDebit.Sum(r => r.Base); // iade neti düşürür
        decimal gider = giderDebit.Sum(r => r.Base) - giderCredit.Sum(r => r.Base); // ters kayıt neti düşürür
        // İade'nin Borç KDV'si tahsil edilen KDV'yi DÜŞÜRÜR (KDV indirimi/input VAT DEĞİL); gerçek
        // indirim (varsa iade-dışı Borç KDV) kdvInd olarak ayrı kalır.
        decimal kdvIadeRev = rows.Where(r => r.AccountType == LedgerAccountType.Kdv && r.Direction == LedgerDirection.Debit && r.SourceType == "FaturaIade").Sum(r => r.Base);
        decimal kdvTahsil = Sum(rows, LedgerAccountType.Kdv, LedgerDirection.Credit) - kdvIadeRev;
        decimal kdvInd = rows.Where(r => r.AccountType == LedgerAccountType.Kdv && r.Direction == LedgerDirection.Debit && r.SourceType != "FaturaIade").Sum(r => r.Base);

        // Gelir kırılımı: iade (Borç Gelir) ilgili kaynağı negatif kalem olarak gösterir → toplamla tutarlı.
        var gelirKirilim = gelirCredit.Select(r => (r.SourceType, Tutar: r.Base))
            .Concat(gelirDebit.Select(r => (r.SourceType, Tutar: -r.Base)))
            .GroupBy(x => x.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(x => x.Tutar)))
            .OrderByDescending(k => k.Tutar).ToList();
        var giderKirilim = giderDebit.Select(r => (r.SourceType, Tutar: r.Base))
            .Concat(giderCredit.Select(r => (r.SourceType, Tutar: -r.Base)))
            .GroupBy(x => x.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(x => x.Tutar)))
            .OrderByDescending(k => k.Tutar).ToList();

        return new GelirGiderDto(gelir, gider, kdvTahsil, kdvInd, gelir - gider, gelirKirilim, giderKirilim);
    }

    /// <summary>
    /// Günlük faaliyet raporu: verilen günün ([gün 00:00, ertesi gün − tick]) operasyonel
    /// sayaçları + tutarları. Repo'da sayım/toplam; burası gün sınırlarını kurar.
    /// </summary>
    public Task<GunlukFaaliyetDto> GetGunlukFaaliyetAsync(DateTimeOffset gun, CancellationToken ct = default)
    {
        var from = new DateTimeOffset(gun.Date, TimeSpan.Zero);
        var to = from.AddDays(1).AddTicks(-1);
        return _repository.GetGunlukFaaliyetAsync(from, to, ct);
    }

    /// <summary>
    /// KDV listesi: dönemdeki (fatura tarihi) İptal olmayan faturaların KDV oranı bazında
    /// kırılımı (Net/KDV/Brüt + o oranı içeren fatura adedi) + genel toplamlar. Beyanname/muhasebe.
    /// </summary>
    public async Task<KdvListesiDto> GetKdvListesiAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetKdvLineRowsAsync(from, to, ct);

        var satirlar = rows
            .GroupBy(r => r.Oran)
            .Select(g => new KdvListesiRowDto(
                g.Key,
                g.Sum(r => r.Net),
                g.Sum(r => r.Kdv),
                g.Sum(r => r.Brut),
                g.Select(r => r.InvoiceId).Distinct().Count()))
            .OrderBy(s => s.Oran)
            .ToList();

        return new KdvListesiDto(
            satirlar,
            satirlar.Sum(s => s.Net),
            satirlar.Sum(s => s.Kdv),
            satirlar.Sum(s => s.Brut),
            rows.Select(r => r.InvoiceId).Distinct().Count());
    }

    /// <summary>
    /// Ek hizmet satış raporu: dönemde (kalem eklenme tarihi) İptal olmayan kiralara satılan ek
    /// hizmetlerin ADINA göre özeti (toplam miktar/net/KDV/brüt + kaç kirada) + genel toplamlar.
    /// </summary>
    public async Task<EkHizmetRaporDto> GetEkHizmetRaporuAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetEkHizmetSalesRowsAsync(from, to, ct);

        var satirlar = rows
            .GroupBy(r => r.Ad)
            .Select(g => new EkHizmetRaporRowDto(
                g.Key,
                g.Sum(r => r.Miktar),
                g.Sum(r => r.Net),
                g.Sum(r => r.Kdv),
                g.Sum(r => r.Brut),
                g.Select(r => r.RentalId).Distinct().Count()))
            .OrderByDescending(s => s.Brut)
            .ToList();

        return new EkHizmetRaporDto(
            satirlar,
            satirlar.Sum(s => s.Net),
            satirlar.Sum(s => s.Kdv),
            satirlar.Sum(s => s.Brut),
            rows.Select(r => r.RentalId).Distinct().Count());
    }

    /// <summary>Tüm cariler için net bakiye (Σ Borç − Σ Alacak), sıfır olmayanlar, borçtan-alacağa sıralı.</summary>
    public async Task<IReadOnlyList<CariBalanceDto>> GetCariBalancesAsync(CancellationToken ct = default)
    {
        var rows = await _repository.GetCariLedgerRowsAsync(asOf: null, ct);
        return rows
            .GroupBy(r => (r.CariId, r.Ad))
            .Select(g => new CariBalanceDto(g.Key.CariId, g.Key.Ad, g.Sum(Signed)))
            .Where(b => b.Bakiye != 0m)
            .OrderByDescending(b => b.Bakiye)
            .ToList();
    }

    /// <summary>
    /// Cari borç yaşlandırma (v1: BRÜT borç, tahsilat mahsubu yok). Borç satırları yaşa (asOf−Tarih,
    /// gün) göre 0-30 / 31-60 / 61-90 / 90+ kovalarına. Yalnız borç bakiyesi olan cariler.
    /// </summary>
    public async Task<IReadOnlyList<AgingRowDto>> GetAgingAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var rows = await _repository.GetCariLedgerRowsAsync(asOf, ct);
        return rows
            .Where(r => r.Direction == LedgerDirection.Debit) // yalnız borç (brüt)
            .GroupBy(r => (r.CariId, r.Ad))
            .Select(g =>
            {
                decimal b0 = 0, b30 = 0, b60 = 0, b90 = 0;
                foreach (var r in g)
                {
                    var gun = (asOf.UtcDateTime.Date - r.Tarih.UtcDateTime.Date).Days;
                    if (gun <= 30) b0 += r.Base;
                    else if (gun <= 60) b30 += r.Base;
                    else if (gun <= 90) b60 += r.Base;
                    else b90 += r.Base;
                }
                return new AgingRowDto(g.Key.CariId, g.Key.Ad, b0, b30, b60, b90, b0 + b30 + b60 + b90);
            })
            .Where(a => a.Toplam != 0m)
            .OrderByDescending(a => a.Toplam)
            .ToList();
    }

    /// <summary>
    /// Dönem doluluk: araç-gün kapasitesi üzerinden kira-gün oranı. DonemGun = (to−from) takvim-günü
    /// (kapsayıcı). KiraGun = Σ (kira aralığı ∩ dönem) kapsayıcı takvim-günü. Yüzde = KiraGun/AracGun×100.
    /// Beklenen değerler senaryodan türetilir (bkz. DolulukTests bağımsız oracle).
    /// </summary>
    public async Task<DolulukDto> GetDolulukAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var aracSayisi = (await _repository.GetVehicleStatusesAsync(ct)).Count;
        var rows = await _repository.GetRentalIntervalsAsync(from, to, ct);

        var fromD = from.UtcDateTime.Date;
        var toD = to.UtcDateTime.Date;
        int donemGun = toD >= fromD ? (toD - fromD).Days + 1 : 0;

        int kiraGun = rows.Sum(r => OverlapDays(r.Bas.UtcDateTime.Date, r.Bit.UtcDateTime.Date, fromD, toD));
        int aracGun = aracSayisi * donemGun;
        decimal yuzde = aracGun > 0 ? Math.Round((decimal)kiraGun * 100m / aracGun, 2, MidpointRounding.AwayFromZero) : 0m;

        return new DolulukDto(aracSayisi, donemGun, aracGun, kiraGun, yuzde);
    }

    /// <summary>İki kapsayıcı tarih aralığının kesişim gün sayısı (kesişim yoksa 0).</summary>
    private static int OverlapDays(DateTime aBas, DateTime aBit, DateTime bBas, DateTime bBit)
    {
        var lo = aBas > bBas ? aBas : bBas;
        var hi = aBit < bBit ? aBit : bBit;
        return hi >= lo ? (hi - lo).Days + 1 : 0;
    }
    /// Dönem tahsilat-fatura mutabakatı: kesilen fatura (İptal hariç) vs alınan tahsilat (ters hariç)
    /// + fark. Repo sayım/toplamı yapar; Fark = FaturaToplam − TahsilatToplam (repo'da hesaplı). Pass-through.
    /// </summary>
    public Task<TahsilatFaturaDto> GetTahsilatFaturaAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetTahsilatFaturaAsync(from, to, ct);

    /// <summary>Filo durum dağılımı + aktif kira sayısı.</summary>
    public async Task<FleetUtilizationDto> GetFleetUtilizationAsync(CancellationToken ct = default)
    {
        var statuses = await _repository.GetVehicleStatusesAsync(ct);
        var aktifKira = await _repository.GetActiveRentalCountAsync(ct);
        int Count(VehicleStatus s) => statuses.Count(x => x == s);
        return new FleetUtilizationDto(
            statuses.Count,
            Count(VehicleStatus.Musait), Count(VehicleStatus.Kirada),
            Count(VehicleStatus.Serviste), Count(VehicleStatus.Pasif), Count(VehicleStatus.Satildi),
            aktifKira);
    }

    /// <summary>Tamamlanmış servislerin araç+tip başına maliyet özeti (Σ ToplamIscilik + adet).</summary>
    public async Task<IReadOnlyList<ServiceCostSummaryDto>> GetServiceCostSummaryAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetServiceCostRowsAsync(from, to, ct);
        return rows
            .GroupBy(r => (r.VehicleId, r.Plaka, r.Tip))
            .Select(g => new ServiceCostSummaryDto(
                g.Key.VehicleId, g.Key.Plaka, g.Key.Tip, g.Sum(r => r.ToplamIscilik), g.Count()))
            .OrderByDescending(s => s.Toplam)
            .ToList();
    }

    /// <summary>Periyodik servis raporu (roadmap H1): KM-bazlı bakım uyarısı, KalanKm artan sıralı.</summary>
    public Task<IReadOnlyList<PeriyodikServisRow>> GetPeriyodikServisAsync(CancellationToken ct = default)
        => _repository.GetPeriyodikServisRowsAsync(ct);

    /// <summary>Kira KM detay raporu (roadmap H1): dönmüş kiraların çıkış/dönüş/katedilen km'si.</summary>
    public Task<IReadOnlyList<KmDetayRow>> GetKmDetayAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetKmDetayRowsAsync(from, to, ct);

    /// <summary>Rezervasyon kaynak raporu (roadmap H2): kaynak başına adet/gün/ciro.</summary>
    public Task<IReadOnlyList<RezervasyonKaynakRow>> GetRezervasyonKaynakAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetRezervasyonKaynakRowsAsync(from, to, ct);

    /// <summary>Fatura dönem raporu (roadmap H2): tarih filtreli fatura listesi (vade/cari/tutar/durum).</summary>
    public Task<IReadOnlyList<FaturaDonemRow>> GetFaturaDonemAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetFaturaDonemRowsAsync(from, to, ct);

    /// <summary>Araç durum-takip raporu (roadmap H3): gün kırılımı dolu/bakım/boş (varsayılan son 30 gün).</summary>
    public Task<IReadOnlyList<AracDurumTakipRow>> GetAracDurumTakipAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var bit = to ?? DateTimeOffset.UtcNow;
        var bas = from ?? bit.AddDays(-29);
        return _repository.GetAracDurumTakipRowsAsync(bas, bit, ct);
    }

    /// <summary>Müşteri CRM segment (roadmap N3): kira sayısı/ciro/segment.</summary>
    public Task<IReadOnlyList<MusteriSegmentRow>> GetMusteriSegmentAsync(CancellationToken ct = default)
        => _repository.GetMusteriSegmentRowsAsync(ct);

    /// <summary>Personel çalışma grafiği (roadmap N3): personel başına BAF tahsis sayısı.</summary>
    public Task<IReadOnlyList<PersonelCalismaRow>> GetPersonelCalismaAsync(CancellationToken ct = default)
        => _repository.GetPersonelCalismaRowsAsync(ct);

    private static decimal Signed(CariLedgerRowDto r)
        => r.Direction == LedgerDirection.Debit ? r.Base : -r.Base;

    private static List<GelirGiderKalemDto> Kirilim(IEnumerable<LedgerRowDto> rows)
        => rows.GroupBy(r => r.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(r => r.Base)))
            .OrderByDescending(k => k.Tutar).ToList();

    private static decimal Sum(IEnumerable<LedgerRowDto> rows, LedgerAccountType type, LedgerDirection dir)
        => rows.Where(r => r.AccountType == type && r.Direction == dir).Sum(r => r.Base);
}
