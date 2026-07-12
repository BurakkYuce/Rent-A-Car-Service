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
public sealed class ReportService(IReportRepository repository)
{
    private readonly IReportRepository _repository = repository;

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

        return new AracKarneDto(header, toplamGelir, toplamGider, netKar,
            yillik, gelirKaynak, giderKategori, raw.Olaylar, kpi, maliyetModel);
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

    /// <summary>Dönem gelir-gider özeti + KDV + net kâr + SourceType kırılımı.</summary>
    public async Task<GelirGiderDto> GetGelirGiderAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLedgerRowsAsync(
            [LedgerAccountType.Gelir, LedgerAccountType.Gider, LedgerAccountType.Kdv], from, to, ct);

        // İade faturası TERS kayıt yazar (Borç Gelir / Borç KDV) → gelir ve tahsil edilen KDV netleşir.
        var gelirCredit = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Credit).ToList();
        var gelirDebit = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Debit).ToList();
        var giderRows = rows.Where(r => r.AccountType == LedgerAccountType.Gider && r.Direction == LedgerDirection.Debit).ToList();

        decimal gelir = gelirCredit.Sum(r => r.Base) - gelirDebit.Sum(r => r.Base); // iade neti düşürür
        decimal gider = giderRows.Sum(r => r.Base);
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
        var giderKirilim = Kirilim(giderRows);

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
