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
public sealed class ReportService(IReportRepository repository, TutSatEsikleri holdSellThresholds)
{
    private readonly IReportRepository _repository = repository;
    private readonly TutSatEsikleri _holdSell = holdSellThresholds;

    /// <summary>
    /// Araç-bazlı kârlılık raporu (roadmap B2): defterden türetilen Gelir/Gider satırları (repo'da
    /// SourceId→Fatura→Kira→Araç atfı). Opsiyonel şube/grup/plaka/kaynak/SIPP filtresi (filtre varsa
    /// "(Atanmamış)" satırı hariç). Filtresiz toplamlar defter Gelir/Gider toplamıyla MUTABIK (invariant).
    /// NetKar desc sıralı.
    ///
    /// <para><b>FAZ-79 — P&amp;L SÖZLEŞMESİ:</b> Gelir/Gider/NetKar satır ve toplamları YALNIZ
    /// <see cref="IReportRepository.GetProfitabilityRowsAsync"/> çıktısıdır. Bu metodun eklediği tüm yeni
    /// kolonlar (SIPP/Otopark/Rez.Kaynağı/Cari Bakiye/Referans Maliyet/Potansiyel Gelir/KDV/Doluluk/
    /// RevPACD/ADR) <c>with</c> ile SATIRA EKLENİR, hiçbiri para toplamına GİRMEZ. Kaynak-varlık
    /// tutarının (ör. <c>Vehicle.AylikMaliyet</c>) Gider'e eklenmesi çift-sayımdır ve Critical'dır.</para>
    ///
    /// <para>KPI kolonları (Doluluk/RevPACD/ADR) <b>sahiplik penceresi = ÖMÜR BOYU</b>dur; sayfa dönem
    /// filtresi P&amp;L'i daraltır ama KPI'yı DARALTMAZ (karışık payda yasak — Araç Karnesi/Filo Analiz
    /// ile aynı tanım). <paramref name="vatStatus"/> SALT GÖSTERİM anahtarıdır: tutarları değiştirmez.</para>
    /// </summary>
    public async Task<KarlilikDto> GetProfitabilityAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        string? branch = null, string? group = null, string? plate = null,
        string? source = null, string? sipp = null, VatStatus vatStatus = VatStatus.Kdvsiz,
        CancellationToken ct = default)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
        // (1) P&L — DEFTERDEN. Bu liste aşağıda para bakımından DEĞİŞTİRİLMEZ.
        var rows = await _repository.GetProfitabilityRowsAsync(from, to, ct);
        // (2) Defter-DIŞI zenginleştirme (araç kartı / kira metası / tarife / cari defteri).
        var rich = await EnrichAsync(rows, from, to, ct);

        var hasFilter = !string.IsNullOrWhiteSpace(branch) || !string.IsNullOrWhiteSpace(group)
            || !string.IsNullOrWhiteSpace(plate) || !string.IsNullOrWhiteSpace(source) || !string.IsNullOrWhiteSpace(sipp);
        IEnumerable<KarlilikSatirDto> q = rich;
        if (hasFilter)
            q = rich.Where(r => r.VehicleId != null
                && (string.IsNullOrWhiteSpace(branch) || string.Equals(r.Sube, branch.Trim(), OIC))
                && (string.IsNullOrWhiteSpace(group) || string.Equals(r.Grup, group.Trim(), OIC))
                && (string.IsNullOrWhiteSpace(plate) || r.Plaka.Contains(plate.Trim(), OIC))
                && (string.IsNullOrWhiteSpace(source) || string.Equals(r.RezKaynagi, source.Trim(), OIC))
                && (string.IsNullOrWhiteSpace(sipp) || string.Equals(r.Sipp, sipp.Trim(), OIC)));

        var list = q.OrderByDescending(r => r.NetKar).ThenBy(r => r.Plaka, StringComparer.OrdinalIgnoreCase).ToList();
        // Referans toplamları AYRI alanlarda taşınır; hiçbiri ToplamGelir/ToplamGider'e eklenmez.
        decimal? Ref(Func<KarlilikSatirDto, decimal?> select)
        {
            var v = list.Select(select).Where(x => x != null).Select(x => x!.Value).ToList();
            return v.Count > 0 ? v.Sum() : null;
        }
        return new KarlilikDto(list,
            list.Sum(r => r.Gelir), list.Sum(r => r.Gider), list.Sum(r => r.NetKar),
            vatStatus,
            ToplamPotansiyelGelir: Ref(r => r.PotansiyelGelir),
            ToplamReferansMaliyet: Ref(r => r.ReferansToplamMaliyet),
            ToplamHesaplananKdv: Ref(r => r.HesaplananKdv));
    }

    /// <summary>Çok-boyutlu kârlılık özeti (roadmap #2): araç-bazlı P&amp;L satırlarını bir boyuta göre toplar.
    /// boyut: "grup"|"sube"|"segment"|"otopark"|"sipp" (varsayılan grup). Yalnız araca atfedilmiş satırlar
    /// (VehicleId!=null) — "(Atanmamış)" gruba dahil edilmez (boyut değeri yok). Aggregation saf gruplama;
    /// para mantığı KarlilikDto'dan.
    /// <para>FAZ-79: "otopark" (araç kartının şube FK'sı) ve "sipp" boyutları eklendi; her araç TEK kovaya
    /// düştüğü için Σ boyut = Σ araç invaryantı korunur. <b>Rez. kaynağı boyutu bilinçli EKLENMEDİ</b> — bir
    /// araç dönemde birden çok kaynaktan kiralanabilir, "baskın kaynak" ile P&amp;L toplamak parayı yanlış
    /// kaynağa yazardı; kaynak satır kolonu + filtresi olarak durur.</para></summary>
    public async Task<KarlilikOzetDto> GetProfitabilitySummaryAsync(
        string size, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var pnl = await _repository.GetProfitabilityRowsAsync(from, to, ct);
        var rows = (await EnrichAsync(pnl, from, to, ct)).Where(r => r.VehicleId != null).ToList();
        var b = (size ?? "grup").Trim().ToLowerInvariant();
        string name = b switch
        {
            "sube" => "Şube", "segment" => "Segment", "otopark" => "Otopark", "sipp" => "SIPP", _ => "Grup"
        };
        string Key(KarlilikSatirDto r) => b switch
        {
            "sube" => string.IsNullOrWhiteSpace(r.Sube) ? "(Şubesiz)" : r.Sube!.Trim(),
            "segment" => string.IsNullOrWhiteSpace(r.Segment) ? "(Segmentsiz)" : r.Segment!.Trim(),
            "otopark" => string.IsNullOrWhiteSpace(r.Otopark) ? "(Otoparksız)" : r.Otopark!.Trim(),
            "sipp" => string.IsNullOrWhiteSpace(r.Sipp) ? "(SIPP yok)" : r.Sipp!.Trim(),
            _ => string.IsNullOrWhiteSpace(r.Grup) ? "(Grupsuz)" : r.Grup!.Trim()
        };
        decimal? R2(decimal? x) => x is { } d ? decimal.Round(d, 2, MidpointRounding.AwayFromZero) : null;
        decimal? Ref(IEnumerable<decimal?> xs)
        {
            var v = xs.Where(x => x != null).Select(x => x!.Value).ToList();
            return v.Count > 0 ? v.Sum() : null;
        }
        var rowList = rows.GroupBy(Key)
            .Select(g =>
            {
                var revenue = g.Sum(r => r.Gelir);
                var count = g.Count();
                // Havuz doluluğu: Σ kiralanan ÷ Σ sahiplik (satır yüzdelerinin ortalaması DEĞİL).
                var ownership = g.Sum(r => r.SahiplikGun);
                var rented = g.Sum(r => r.KiralananGun);
                return new KarlilikOzetSatirDto(g.Key, count, revenue, g.Sum(r => r.Gider), g.Sum(r => r.NetKar),
                    AracBasiGelir: count > 0 ? R2(revenue / count) : null,
                    DolulukYuzde: ownership > 0 ? R2(Math.Min(100m, rented * 100m / ownership)) : null,
                    PotansiyelGelir: Ref(g.Select(r => r.PotansiyelGelir)),
                    ReferansToplamMaliyet: Ref(g.Select(r => r.ReferansToplamMaliyet)));
            })
            .OrderByDescending(s => s.NetKar).ThenBy(s => s.Boyut, StringComparer.CurrentCulture).ToList();
        return new KarlilikOzetDto(name, rowList, rowList.Sum(s => s.Gelir), rowList.Sum(s => s.Gider), rowList.Sum(s => s.NetKar));
    }

    /// <summary>
    /// FAZ-79 — Karlılık satırlarını DEFTER-DIŞI bilgi kolonlarıyla zenginleştirir.
    ///
    /// <para><b>Değişmez:</b> girdi satırlarının <c>Gelir</c>/<c>Gider</c>/<c>NetKar</c> alanlarına
    /// DOKUNULMAZ — yalnız <c>with</c> ile referans alanları doldurulur. Satır sayısı ve sırası da
    /// korunur (yalnız aynı listenin zenginleştirilmiş kopyası döner).</para>
    /// </summary>
    private async Task<IReadOnlyList<KarlilikSatirDto>> EnrichAsync(
        IReadOnlyList<KarlilikSatirDto> rows, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        if (rows.Count == 0) return rows;
        var extra = await _repository.GetProfitabilityExtraRawAsync(from, to, ct);
        // Ömür listesi boşsa pencere zaten ömrün tamamıdır (repo sözleşmesi) → aynı liste kullanılır.
        var lifetimeByVeh = (extra.Omur.Count == 0 ? rows : extra.Omur)
            .Where(r => r.VehicleId != null).ToDictionary(r => r.VehicleId!.Value);
        var metaByVeh = extra.Araclar.ToDictionary(a => a.Id);
        var rentalByVeh = extra.Kiralar.GroupBy(k => k.VehicleId).ToDictionary(g => g.Key, g => g.ToList());
        var vatByVeh = extra.KdvSatirlari.Where(k => k.VehicleId != null)
            .ToDictionary(k => k.VehicleId!.Value, k => k.Kdv);
        var vatUnassigned = extra.KdvSatirlari.Where(k => k.VehicleId == null).Sum(k => k.Kdv);

        // Cari bakiye: CARİ defterinden (araç P&L'i değil) — sıfır bakiyeli cari listede olmaz, o yüzden
        // bulunamayan cari için 0 doğrudur (hareketsiz ya da net'i kapanmış).
        var customers = (await GetAccountBalancesAsync(null, ct)).ToDictionary(c => c.CariId);

        var now = DateTimeOffset.UtcNow;
        var result = new List<KarlilikSatirDto>(rows.Count);
        foreach (var r in rows)
        {
            if (r.VehicleId is not Guid vid)
            {
                // "(Atanmamış)" satırı: araç kartı yok → yalnız atfedilemeyen KDV bilgisi taşınır.
                result.Add(r with { HesaplananKdv = vatUnassigned == 0m ? null : vatUnassigned });
                continue;
            }
            var rentals = rentalByVeh.TryGetValue(vid, out var ks) ? ks : [];
            metaByVeh.TryGetValue(vid, out var meta);

            // Ömür KPI'ları — sahiplik penceresi (dönem filtresinden BAĞIMSIZ).
            var lifetimeRevenue = lifetimeByVeh.TryGetValue(vid, out var og) ? og.Gelir : 0m;
            var (ownership, rented, _, _) = meta is null
                ? (0, 0, default(DateTime), default(DateTime))
                : OwnershipDays(meta.FiloGirisTarih, meta.AlimTarihi, meta.FiloCikisTarih,
                    meta.Durum == VehicleStatus.Satildi && meta.SonSatisTarih is not null, meta.SonSatisTarih,
                    rentals.Select(k => (k.Bas, k.Bit)), now);

            decimal? R2(decimal? x) => x is { } d ? decimal.Round(d, 2, MidpointRounding.AwayFromZero) : null;

            // Potansiyel gelir — DÖNEM kapasitesi × onaylı tarife (KARARLAR: RateMatrix). Dönem verilmemişse
            // sahiplik penceresinin tamamı. Tarife tarihi pencere sonu (bugün geçerli liste fiyatı).
            var (periodOwnership, _, _, _) = meta is null
                ? (0, 0, default(DateTime), default(DateTime))
                : OwnershipDays(meta.FiloGirisTarih, meta.AlimTarihi, meta.FiloCikisTarih,
                    meta.Durum == VehicleStatus.Satildi && meta.SonSatisTarih is not null, meta.SonSatisTarih,
                    [], now, from, to);
            var dailyTariff = meta is null ? null
                : PotentialRevenueCalculation.DailyTariff(extra.Tarifeler, meta.GrupKod, meta.SubeAdi, to ?? now);

            // Kira-türevli bilgi: dönem içindeki kiralar (kesişen) — kaynak/müşteri BİLGİSİ, tutar DEĞİL.
            var periodRentals = rentals
                .Where(k => (from is null || k.Bit >= from) && (to is null || k.Bas <= to))
                .OrderBy(k => k.Bas).ToList();
            var dominantSource = periodRentals
                .Where(k => !string.IsNullOrWhiteSpace(k.Kaynak))
                .GroupBy(k => k.Kaynak!.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()?.Key;
            var lastRental = periodRentals.LastOrDefault();
            CariBalanceDto? account = lastRental is not null && customers.TryGetValue(lastRental.MusteriId, out var c) ? c : null;

            result.Add(r with
            {
                Sipp = meta?.Sipp,
                Otopark = meta?.Otopark,
                RezKaynagi = dominantSource,
                CariAd = account?.Ad,
                CariBakiye = lastRental is null ? null : (account?.Bakiye ?? 0m),
                ReferansAylikMaliyet = meta?.AylikMaliyet,
                ReferansFiloYonetimMaliyeti = meta?.FiloYonetimMaliyeti,
                PotansiyelGelir = PotentialRevenueCalculation.Calculate(dailyTariff, periodOwnership),
                HesaplananKdv = vatByVeh.TryGetValue(vid, out var vat) ? vat : null,
                DolulukYuzde = ownership > 0 ? R2(Math.Min(100m, rented * 100m / ownership)) : null,
                RevPacd = ownership > 0 ? R2(lifetimeRevenue / ownership) : null,
                Adr = rented > 0 ? R2(lifetimeRevenue / rented) : null,
                SahiplikGun = ownership,
                KiralananGun = rented,
                KiraAdet = periodRentals.Count
            });
        }
        return result;
    }

    /// <summary>
    /// Sahiplik penceresi gün matematiği — Araç Karnesi / Filo Analiz / Karlılık için TEK kaynak.
    /// Pencere: bas = FiloGirisTarih ?? AlimTarihi; bit = FiloCikisTarih ?? (satılmışsa son satış) ?? şimdi.
    /// Gün sayımı KAPSAYICI takvim günü (GetDolulukAsync deseni). <paramref name="clampStart"/>/
    /// <paramref name="clampEnd"/> verilirse pencere o aralıkla KESİŞTİRİLİR (dönem kapasitesi hesabı);
    /// verilmezse ömür boyu.
    /// </summary>
    private static (int Sahiplik, int Kiralanan, DateTime BasD, DateTime BitD) OwnershipDays(
        DateTimeOffset? fleetEntry, DateTimeOffset? purchaseDate, DateTimeOffset? fleetExit,
        bool sold, DateTimeOffset? lastSale,
        IEnumerable<(DateTimeOffset Bas, DateTimeOffset Bit)> rentals, DateTimeOffset now,
        DateTimeOffset? clampStart = null, DateTimeOffset? clampEnd = null)
    {
        var wStart = fleetEntry ?? purchaseDate;
        var wBit = fleetExit ?? (sold ? lastSale : null) ?? now;
        if (wStart is not { } wb) return (0, 0, default, default);

        var startD = wb.UtcDateTime.Date;
        var bitD = wBit.UtcDateTime.Date;
        if (clampStart is { } kb && kb.UtcDateTime.Date > startD) startD = kb.UtcDateTime.Date;
        if (clampEnd is { } kt && kt.UtcDateTime.Date < bitD) bitD = kt.UtcDateTime.Date;

        var ownership = bitD >= startD ? (bitD - startD).Days + 1 : 0;
        var rented = ownership == 0 ? 0
            : rentals.Sum(k => OverlapDays(k.Bas.UtcDateTime.Date, k.Bit.UtcDateTime.Date, startD, bitD));
        return (ownership, rented, startD, bitD);
    }

    /// <summary>
    /// Araç karnesi (araç ön muhasebe 360°): tek aracın defterden P&L'i (yıllık kırılım + gelir-kaynak +
    /// gider-kategori) + kaynak-varlık olay zaman çizelgesi. Toplamlar o aracın Karlilik satırıyla MUTABIK
    /// (atıf kuralları birebir; parite testi kilitler). Araç yoksa/başka tenant'sa null (sayfa 404).
    /// Saf toplama — DB erişimi repo'da; KPI/amortisman bloğu ayrı artışta eklenir.
    /// </summary>
    public async Task<AracKarneDto?> GetVehicleScorecardAsync(
        Guid vehicleId, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var raw = await _repository.GetVehicleScorecardRawAsync(vehicleId, from, to, ct);
        if (raw.Vehicle is null) return null;
        var v = raw.Vehicle;

        var header = new AracKarneHeaderDto(
            v.Id, v.Plaka, v.Marka, v.Tip, v.Grup, v.Segment, v.Sube, v.AracSahibi, v.Durum, v.Km,
            v.AlimBedeli, v.AlimTarihi, v.IkinciElDeger, v.FiloGirisTarih, v.FiloCikisTarih,
            v.SonBakimTarih, v.SonBakimKm);

        var totalRevenue = raw.Gelirler.Sum(g => g.Tutar);
        var totalExpense = raw.Giderler.Sum(g => g.Tutar);

        // Yıllık P&L: gelir ∪ gider yıllarının birleşimi (yalnız gelirli/yalnız giderli yıl kaybolmaz).
        var annual = raw.Gelirler.Select(g => g.Yil).Concat(raw.Giderler.Select(g => g.Yil))
            .Distinct().OrderBy(y => y)
            .Select(y =>
            {
                var ge = raw.Gelirler.Where(g => g.Yil == y).Sum(g => g.Tutar);
                var gi = raw.Giderler.Where(g => g.Yil == y).Sum(g => g.Tutar);
                return new AracYilPnlRow(y, ge, gi, ge - gi);
            }).ToList();

        // Kırılım yüzdesi: tutar ÷ toplam gelir (kurumsal "% of revenue"). Yalnız POZİTİF gelirde
        // anlamlı — dönem-net'i 0/negatifse (iade > gelir) yüzde yanıltır → null (adversarial F1).
        decimal? Pct(decimal t) => totalRevenue > 0m
            ? Math.Round(t * 100m / totalRevenue, 2, MidpointRounding.AwayFromZero) : null;
        var revenueSource = raw.Gelirler.GroupBy(g => g.Kaynak)
            .Select(g => new AracKirilimRow(g.Key, g.Sum(x => x.Tutar), Pct(g.Sum(x => x.Tutar))))
            .OrderByDescending(r => r.Tutar).ToList();
        var expenseCategory = raw.Giderler.GroupBy(g => g.Kategori)
            .Select(g => new AracKirilimRow(g.Key, g.Sum(x => x.Tutar), Pct(g.Sum(x => x.Tutar))))
            .OrderByDescending(r => r.Tutar).ToList();

        var netProfit = totalRevenue - totalExpense;
        // KPI para girdileri ÖMÜR-BOYU (raw.OmurGelir/OmurGider) — sayfanın dönem filtresi P&L'i daraltır
        // ama KPI paydaları/payları daralmaz (adversarial F2: karışık-payda sızıntısı kapatıldı).
        var kpi = CalculateVehicleKpi(v, raw, out var durationMonths);

        // Amortisman/başabaş modeli: yalnız AlimBedeli>0 (MaliyetHesapService guard'ları ValidationException
        // atar — null bırakılır). Ömür-boyu holding varsayımı; FaizOran=0 (uydurma finansman yazılmaz),
        // Residual = IkinciElDeger/AlimBedeli (yoksa varsayılan 0.30), AylikGider = gerçekleşen ortalama.
        // TEK payda: model her yerde AYNI clamp'lenmiş ayı kullanır (>120 ayda karışık-payda çarpıklığı olmaz).
        MaliyetHesapSonuc? costModel = null;
        if (v.AlimBedeli is > 0m)
        {
            var modelMonth = Math.Clamp(durationMonths, 1, 120);
            costModel = CostCalculationService.Calculate(new MaliyetHesapInput
            {
                AlisBedeli = v.AlimBedeli.Value,
                SureAy = modelMonth,
                ResidualYuzde = v.IkinciElDeger is > 0m
                    ? Math.Clamp(v.IkinciElDeger.Value / v.AlimBedeli.Value, 0m, 1m) : 0.30m,
                AylikGider = decimal.Round(raw.OmurGider / modelMonth, 2, MidpointRounding.AwayFromZero),
                FaizOran = 0m, DamgaOran = 0m
            });
        }

        var holdSell = HoldSellCalculation.Calculate(raw.TutSatHam, v.IkinciElDeger, raw.GrupOrtDegerOrani, _holdSell);
        // FAZ 2.3: başabaş GÜNLÜK (model) = BasaBasAylik / 30.44 — Kpi.Adr ile kıyas satırı
        // (aynı 30.44 gün/ay paydası; model yoksa null → UI "—" gösterir, uydurma değer yok).
        decimal? breakEvenDaily = costModel is null ? null
            : decimal.Round(costModel.BasaBasAylik / 30.44m, 2, MidpointRounding.AwayFromZero);
        // FAZ 2.4: kalıntı projeksiyonu (azalan bakiye; salt-hesap, deftere yazmaz).
        var residual = ResidualProjection.Calculate(v.AlimBedeli, v.IkinciElDeger, v.AlimTarihi, DateTimeOffset.UtcNow);

        // FAZ 2.5: dönemsel km — km-log serisinden pencere farkı. BİLGİ satırı: KPI km-maliyeti
        // ömür-boyu tanımını KORUR (karışık-payda yasak). Tanım: pencere-içi SON log −
        // (pencere-öncesi SON log ?? pencere-içi İLK log); maliyet = dönem P&L gideri ÷ dönem km.
        int? periodKm = null; decimal? periodKmCost = null;
        if (raw.KmLoglari is { Count: > 0 } logs)
        {
            var endLimit = to ?? DateTimeOffset.UtcNow;
            var insideWindow = logs.Where(k => k.Tarih <= endLimit && (from is null || k.Tarih >= from)).ToList();
            if (insideWindow.Count > 0)
            {
                var floor = from is { } f
                    ? logs.Where(k => k.Tarih < f).Select(k => (int?)k.Km).LastOrDefault() ?? insideWindow[0].Km
                    : insideWindow[0].Km;
                periodKm = Math.Max(0, insideWindow[^1].Km - floor);
                if (periodKm > 0 && totalExpense > 0m)
                    periodKmCost = decimal.Round(totalExpense / periodKm.Value, 2, MidpointRounding.AwayFromZero);
            }
        }

        return new AracKarneDto(header, totalRevenue, totalExpense, netProfit,
            annual, revenueSource, expenseCategory, raw.Olaylar, kpi, costModel, holdSell, breakEvenDaily, residual,
            periodKm, periodKmCost);
    }

    /// <summary>
    /// Kurumsal KPI bloğu — sahiplik penceresi (ömür boyu; sayfa dönem-filtresinden bağımsız, raw KPI hamı
    /// da öyle). Gün matematiği GetDolulukAsync ile aynı: .UtcDateTime.Date + kapsayıcı OverlapDays.
    /// Oranlar yalnız pozitif paydayla anlamlı; aksi null (negatif dönem-net'i yüzdesi dersi).
    /// sureAy = sahiplik günü / 30.44 yuvarlanmış (yaklaşık takvim ayı), min 1 — amortisman/başabaş paydası.
    /// </summary>
    private static AracKpiDto CalculateVehicleKpi(Vehicle v, AracKarneRawDto raw, out int durationMonths)
    {
        // Para girdileri ömür-boyu — dönem filtresinden bağımsız (DTO sözleşmesi).
        var revenue = raw.OmurGelir;
        var expense = raw.OmurGider;
        var netProfit = revenue - expense;
        var sold = v.Durum == VehicleStatus.Satildi && raw.SonSatisTarih is not null;
        // Gün matematiği PAYLAŞILAN helper'dan (filo analiz / karlılık ile TEK tanım).
        var (ownership, rented, startD, bitD) = OwnershipDays(
            v.FiloGirisTarih, v.AlimTarihi, v.FiloCikisTarih, sold, raw.SonSatisTarih,
            raw.KiraAraliklari.Select(r => (r.Bas, r.Bit)), DateTimeOffset.UtcNow);
        var service = 0;
        if (ownership > 0)
        {
            var now = DateTimeOffset.UtcNow.UtcDateTime.Date;
            service = raw.ServisAraliklari.Sum(a =>
                OverlapDays(a.Giris.UtcDateTime.Date, (a.Cikis?.UtcDateTime.Date ?? now), startD, bitD));
        }
        durationMonths = Math.Max(1, (int)Math.Round(ownership / 30.44, MidpointRounding.AwayFromZero));

        decimal? R2(decimal? x) => x is { } d ? decimal.Round(d, 2, MidpointRounding.AwayFromZero) : null;
        // Gerçekleşen amortisman: SATILMIŞ araçta kalıntı realize edildi ve satış geliri netKar'ın İÇİNDE →
        // tam AlimBedeli düşülür (yoksa kalıntı çift sayılır — adversarial F3). Aktif araçta tahmin:
        // AlimBedeli − IkinciElDeger (yalnız >0; 0/negatif "veri yok" — modelin 0.30 varsayımıyla çelişmesin).
        var depreciation = v.AlimBedeli is > 0m
            ? sold ? v.AlimBedeli
                       : v.IkinciElDeger is > 0m ? v.AlimBedeli.Value - v.IkinciElDeger.Value : (decimal?)null
            : null;

        return new AracKpiDto(
            SahiplikGun: ownership,
            KiralananGun: rented,
            ServisGun: service,
            BosGun: Math.Max(0, ownership - rented - service),
            // Doluluk 100 ile sınırlanır: geç dönüş sonraki sözleşmeyle çakışabilir (veri gerçeği) —
            // kurumsal panoda >%100 doluluk güven zedeler (adversarial F4).
            DolulukYuzde: ownership > 0 ? R2(Math.Min(100m, rented * 100m / ownership)) : null,
            RevPacd: ownership > 0 ? R2(revenue / ownership) : null,
            Adr: rented > 0 ? R2(revenue / rented) : null,
            KmBasinaMaliyet: raw.ToplamKatedilenKm > 0 ? R2(expense / raw.ToplamKatedilenKm) : null,
            NetMarjYuzde: revenue > 0m ? R2(netProfit * 100m / revenue) : null,
            // ROI: satılmışta yaşam-döngüsü KAPANIŞ getirisi (satış netKar'da, alım düşülür — çift sayım yok);
            // aktifte defter ROI (amortisman EkonomikKar satırında ayrıca görünür).
            RoiYuzde: v.AlimBedeli is > 0m
                ? R2((sold ? netProfit - v.AlimBedeli.Value : netProfit) * 100m / v.AlimBedeli.Value) : null,
            GeriOdemeAy: CalculatePaybackMonths(v.AlimBedeli, netProfit, durationMonths, ownership),
            Tco: (v.AlimBedeli ?? 0m) + expense,
            GerceklesenAmortisman: depreciation,
            AylikAmortisman: depreciation is { } a2 ? R2(a2 / durationMonths) : null,
            EkonomikKar: depreciation is { } a3 ? netProfit - a3 : null,
            ToplamKatedilenKm: raw.ToplamKatedilenKm,
            KiraSayisi: raw.KiraSayisi);
    }

    /// <summary>
    /// Filo analiz panosu: dönem-pencereli araç P&L satırları (Karlilik ile mutabık) + ÖMÜR-BOYU KPI
    /// sütunları (Doluluk cap-100 / ROI [satılmışta kapanış] / km-maliyet — karne semantiğiyle birebir,
    /// karışık-payda yok) + yaş kohortu (alım tarihi kovası → ort. km-maliyet & doluluk).
    /// siralama: "net"(vars.) | "zarar" | "doluluk" | "roi". Toplamlar (satır+Atanmamış) defterle mutabık.
    /// </summary>
    public async Task<FiloAnalizDto> GetFleetAnalysisAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? sort = null,
        CancellationToken ct = default)
    {
        var raw = await _repository.GetFleetAnalysisRawAsync(from, to, ct);
        var lifetimeByVeh = raw.KarlilikOmur.Where(r => r.VehicleId != null).ToDictionary(r => r.VehicleId!.Value);
        var windowByVeh = raw.KarlilikPencere.Where(r => r.VehicleId != null).ToDictionary(r => r.VehicleId!.Value);
        var rentalByVeh = raw.Kiralar.GroupBy(k => k.VehicleId).ToDictionary(g => g.Key, g => g.ToList());
        var vehicleById = raw.Araclar.ToDictionary(a => a.Id);
        var now = DateTimeOffset.UtcNow;

        // Satırlar TÜM filodan tohumlanır (adversarial F-D): dönemde hareketi olmayan araç 0 P&L ile
        // görünür — gizli zararlı / hiç kiralanmamış araç panodan kaçmaz; kohort filoyu sayar.
        var rows = new List<FiloAnalizRow>();
        foreach (var a in raw.Araclar)
        {
            var p = windowByVeh.TryGetValue(a.Id, out var pr) ? pr : null;
            var om = lifetimeByVeh.TryGetValue(a.Id, out var o) ? o : null;
            var lifetimeNet = (om?.Gelir ?? 0m) - (om?.Gider ?? 0m);
            var rentals = rentalByVeh.TryGetValue(a.Id, out var ks) ? ks : [];

            int? ageMonths = null;
            decimal? occupancy = null, roi = null, kmCost = null;
            var sold = a.Durum == VehicleStatus.Satildi && a.SonSatisTarih is not null;
            // Gün matematiği PAYLAŞILAN helper'dan (karne/karlılık ile TEK tanım — üç kopya yazılmaz).
            var (ownership, rented, _, _) = OwnershipDays(
                a.FiloGirisTarih, a.AlimTarihi, a.FiloCikisTarih, sold, a.SonSatisTarih,
                rentals.Select(k => (k.Bas, k.Bit)), now);
            if (ownership > 0)
                occupancy = decimal.Round(Math.Min(100m, rented * 100m / ownership), 2, MidpointRounding.AwayFromZero);
            if (a.AlimBedeli is > 0m)
                roi = decimal.Round((sold ? lifetimeNet - a.AlimBedeli.Value : lifetimeNet) * 100m / a.AlimBedeli.Value,
                    2, MidpointRounding.AwayFromZero);
            if (a.AlimTarihi is { } at)
            {
                var last = (a.Durum == VehicleStatus.Satildi ? a.SonSatisTarih : null) ?? now;
                // Gün-hassas ay farkı (adversarial F-E): gün-of-ay geçmemişse ay tamamlanmadı sayılır
                // (362 günlük araç "1-2 yıl" kovasına düşmesin).
                var month = (last.Year - at.Year) * 12 + last.Month - at.Month - (last.Day < at.Day ? 1 : 0);
                ageMonths = Math.Max(0, month);
            }
            var km = rentals.Where(k => k.CikisKm != null && k.DonusKm != null)
                .Sum(k => k.DonusKm!.Value - k.CikisKm!.Value);
            if (km > 0 && om is not null)
                kmCost = decimal.Round(om.Gider / km, 2, MidpointRounding.AwayFromZero);

            // FAZ 2.2: tut/sat sinyali — grup ortalaması aşağıda TÜM satırlar kurulduktan sonra
            // hesaplanacağından burada ham+İkinciEl saklanır; sinyal ikinci geçişte eklenir.
            rows.Add(new FiloAnalizRow(a.Id, a.Plaka, a.Grup, a.Segment, a.Sube,
                p?.Gelir ?? 0m, p?.Gider ?? 0m, p?.NetKar ?? 0m,
                occupancy, roi, kmCost, ownership, rented, ageMonths));
        }

        // FAZ 2.2/2.3 ikinci geçiş — grup ortalamaları ORTAK helper'dan (GrupOrtalama; iki kopya yasak):
        // (2.2) gider/değer oranı → tut/sat kural-b; (2.3) km-maliyet → SinifEndeks (1.00 = sınıf ort.).
        var holdSellByVeh = raw.TutSatHam.ToDictionary(x => x.VehicleId);
        var secondHandByVehicle = raw.Araclar.ToDictionary(a => a.Id, a => a.IkinciElDeger);
        var groupAvgValueRatio = GroupAverage.Calculate(raw.Araclar, a => a.Grup,
            a => a.IkinciElDeger is > 0m
                ? holdSellByVeh.GetValueOrDefault(a.Id, new FiloTutSatRow(a.Id, 0m, 0m, 0, 0)).Gider12 / a.IkinciElDeger.Value
                : null);
        var groupAvgKmCost = GroupAverage.Calculate(rows, r => r.Grup, r => r.KmBasinaMaliyet);
        rows = rows.Select(r =>
        {
            var rawData = holdSellByVeh.GetValueOrDefault(r.VehicleId, new FiloTutSatRow(r.VehicleId, 0m, 0m, 0, 0));
            var signal = HoldSellCalculation.Calculate(rawData, secondHandByVehicle.GetValueOrDefault(r.VehicleId),
                GroupAverage.Value(groupAvgValueRatio, r.Grup), _holdSell);
            decimal? index = null;
            if (r.KmBasinaMaliyet is { } km && GroupAverage.Value(groupAvgKmCost, r.Grup) is { } go && go > 0m)
                index = decimal.Round(km / go, 2, MidpointRounding.AwayFromZero);
            return r with { TutSatSinyal = signal.Sinyal, SinifEndeks = index };
        }).ToList();

        // FAZ 2.3: filo-geneli HAVUZ KPI — Σ havuzlardan (satır KPI'larının ortalaması DEĞİL; karışık-payda
        // yasak). Ömür semantik: gelir = Σ araç ömür geliri (defter), günler = Σ sahiplik/kiralanan.
        // Silinmiş-araç kalıntısı eklenmeden ÖNCE hesaplanır (paydasız gelir RevPACD'yi şişirmesin).
        var poolOwnership = rows.Sum(r => r.SahiplikGun);
        var poolRented = rows.Sum(r => r.KiralananGun);
        var poolRevenue = rows.Sum(r => lifetimeByVeh.TryGetValue(r.VehicleId, out var og) ? og.Gelir : 0m);
        var poolKpi = new FiloHavuzKpiDto(poolOwnership, poolRented, poolRevenue,
            poolOwnership > 0 ? decimal.Round(Math.Min(100m, poolRented * 100m / poolOwnership), 2, MidpointRounding.AwayFromZero) : null,
            poolOwnership > 0 ? decimal.Round(poolRevenue / poolOwnership, 2, MidpointRounding.AwayFromZero) : null,
            poolRented > 0 ? decimal.Round(poolRevenue / poolRented, 2, MidpointRounding.AwayFromZero) : null);

        // FAZ 2.4: tut/sat-adayı özeti — sinyal ≥2 araçlar + 12-ay-sonu tahmini kalıntı toplamı
        // (KalintiProjeksiyon; İkinciEl'siz aday projeksiyona katılmaz — uydurma taban yok).
        var candidates = rows.Where(r => r.TutSatSinyal >= 2)
            .Select(r => vehicleById.GetValueOrDefault(r.VehicleId)).Where(a => a is not null).ToList();
        var holdSellCandidate = new TutSatAdayOzetDto(candidates.Count,
            candidates.Sum(a => ResidualProjection.Calculate(a!.AlimBedeli, a.IkinciElDeger, a.AlimTarihi, now)?.Deger12Ay ?? 0m));
        // Silinmiş aracın defter kalıntısı: satır olarak korunur (Σ satır + Atanmamış = defter mutabakatı),
        // KPI'sız; kohorta girmez, karne linki çizilmez ("(bilinmeyen araç)").
        foreach (var p in raw.KarlilikPencere.Where(x => x.VehicleId is Guid vid && !vehicleById.ContainsKey(vid)))
            rows.Add(new FiloAnalizRow(p.VehicleId!.Value, p.Plaka, p.Grup, p.Segment, p.Sube,
                p.Gelir, p.Gider, p.NetKar, null, null, null, 0, 0, null));

        rows = (sort ?? "net").Trim().ToLowerInvariant() switch
        {
            "zarar" => [.. rows.OrderBy(x => x.NetKar).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            "doluluk" => [.. rows.OrderByDescending(x => x.DolulukYuzde ?? -1m).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            "roi" => [.. rows.OrderByDescending(x => x.RoiYuzde ?? decimal.MinValue).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            "tutsat" => [.. rows.OrderByDescending(x => x.TutSatSinyal).ThenBy(x => x.NetKar).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)],
            _ => [.. rows.OrderByDescending(x => x.NetKar).ThenBy(x => x.Plaka, StringComparer.OrdinalIgnoreCase)]
        };

        var unassigned = raw.KarlilikPencere.FirstOrDefault(x => x.VehicleId == null);

        static string Bucket(int? ageMonths) => ageMonths switch
        {
            null => "Alım tarihi yok",
            < 12 => "0-1 yıl",
            < 24 => "1-2 yıl",
            < 36 => "2-3 yıl",
            _ => "3+ yıl"
        };
        static int BucketOrder(string k) => k switch
        { "0-1 yıl" => 0, "1-2 yıl" => 1, "2-3 yıl" => 2, "3+ yıl" => 3, _ => 4 };
        static decimal? Avg(IEnumerable<decimal?> xs)
        {
            var v = xs.Where(x => x != null).Select(x => x!.Value).ToList();
            return v.Count > 0 ? decimal.Round(v.Average(), 2, MidpointRounding.AwayFromZero) : null;
        }
        var cohort = rows.Where(x => vehicleById.ContainsKey(x.VehicleId)).GroupBy(x => Bucket(x.YasAy))
            .Select(g => new FiloKohortRow(g.Key, g.Count(),
                Avg(g.Select(x => x.KmBasinaMaliyet)), Avg(g.Select(x => x.DolulukYuzde))))
            .OrderBy(k => BucketOrder(k.Kova)).ToList();

        // İnvaryant: satırlar + Atanmamış = dönem defter toplamı (Karlilik ile aynı).
        var totalRevenue = rows.Sum(x => x.Gelir) + (unassigned?.Gelir ?? 0m);
        var totalExpense = rows.Sum(x => x.Gider) + (unassigned?.Gider ?? 0m);
        return new FiloAnalizDto(rows, totalRevenue, totalExpense, totalRevenue - totalExpense,
            unassigned?.Gelir ?? 0m, unassigned?.Gider ?? 0m, cohort, poolKpi, holdSellCandidate);
    }

    /// <summary>Geri-ödeme ayı: alım bedelinin aylık net kârla amortismanı. TAMAMEN decimal hesap —
    /// (int) cast taşması yok (adversarial F1: 1-kuruş net + milyonluk araç OverflowException veriyordu).
    /// 1200 aydan (100 yıl) uzun geri ödeme pratikte "geri ödemez" → null.</summary>
    private static int? CalculatePaybackMonths(decimal? purchasePrice, decimal netProfit, int durationMonths, int ownership)
    {
        if (netProfit <= 0m || purchasePrice is not > 0m || ownership <= 0) return null;
        var monthlyNet = netProfit / durationMonths;
        if (monthlyNet <= 0m) return null;
        var month = Math.Ceiling(purchasePrice.Value / monthlyNet);
        return month > 1200m ? null : (int)month;
    }

    /// <summary>
    /// Bir hesabın (Kasa/Banka) defteri: tarihe göre sıralı, yürüyen bakiyeli.
    ///
    /// <para>FAZ-50 <paramref name="accountId"/>: <b>null</b> → o türün TÜM hesapları (eski davranış);
    /// <b><see cref="Guid.Empty"/></b> → yalnız "hesap belirtilmemiş" (legacy) satırlar; başka değer
    /// → yalnız o spesifik hesap. Filtre yürüyen bakiyeden ÖNCE uygulanır — aksi halde bakiye
    /// sütunu ekranda görünmeyen satırları da sayardı.</para>
    /// </summary>
    public async Task<IReadOnlyList<LedgerLineDto>> GetAccountLedgerAsync(
        LedgerAccountType type, DateTimeOffset? from = null, DateTimeOffset? to = null,
        Guid? accountId = null, CancellationToken ct = default,
        // ---- FAZ-57 süzgeçleri (hepsi opsiyonel; verilmezse davranış FAZ-50'deki gibi) ----
        string? currency = null, string? transactionType = null, string? branch = null, bool carryForward = false)
    {
        var rows = await _repository.GetLedgerRowsAsync([type], from, to, ct);

        // Belge künyeleri (cari/evrak/şube/kanal) — şube süzgeci de buradan çalıştığı için
        // süzmeden ÖNCE çözülür.
        var documents = await _repository.GetMovementDocumentsAsync(
            [.. rows.Select(r => r.SourceId).Where(x => x != Guid.Empty).Distinct()], ct);

        IEnumerable<LedgerRowDto> selection = Filter(rows);

        // DEVİR (açılış bakiyesi): yalnız tarih ALT SINIRI verildiğinde anlamlıdır — "başlangıçtan
        // önce ne vardı" sorusunun cevabı. Üst sınır varsa ve alt sınır yoksa devir 0'dır (liste
        // zaten en baştan başlıyor). Aynı süzgeçler devir hesabına da uygulanır; aksi hâlde devir
        // ile liste FARKLI kümeyi toplar ve yürüyen bakiye ilk satırdan itibaren yanlış olurdu.
        decimal opening = 0m;
        if (carryForward && from is { } start)
        {
            var previousItems = await _repository.GetLedgerRowsAsync([type], null, start.AddTicks(-1), ct);
            var previousDocuments = await _repository.GetMovementDocumentsAsync(
                [.. previousItems.Select(r => r.SourceId).Where(x => x != Guid.Empty).Distinct()], ct);
            opening = Filter(previousItems, previousDocuments).Sum(
                r => r.Direction == LedgerDirection.Debit ? r.Base : -r.Base);
        }

        var result = new List<LedgerLineDto>();
        decimal running = opening;
        if (carryForward && from is not null)
            result.Add(new LedgerLineDto(
                from.Value, "Devir", "Önceki dönemden devir", 0m, 0m, opening, accountId, 0m, "TRY",
                DevirMi: true));

        // ADVERSARIAL L4 — aynı tarihli satırlarda DB'nin keyfi sırası yürüyen bakiyenin ARA
        // değerlerini oynatıyordu (son bakiye her koşulda doğru ama mutabakat aracı olarak
        // kullanılan bir listede ara değerler de kararlı olmalı). Eşitlikte kaynak+açıklama ile
        // kırılır — tamamen deterministik.
        foreach (var r in selection.OrderBy(r => r.Tarih)
                     .ThenBy(r => r.SourceType, StringComparer.Ordinal)
                     .ThenBy(r => r.Aciklama, StringComparer.Ordinal))
        {
            var debit = r.Direction == LedgerDirection.Debit ? r.Base : 0m;
            var credit = r.Direction == LedgerDirection.Credit ? r.Base : 0m;
            running += debit - credit;
            var b = documents.GetValueOrDefault(r.SourceId);
            result.Add(new LedgerLineDto(
                r.Tarih, r.SourceType, r.Aciklama, debit, credit, running, r.HesapId, r.Native, r.Doviz,
                b?.CariAd, b?.BelgeNo, b?.Sube, b?.Kanal));
        }
        return result;

        // Süzgeçler TEK yerde: devir hesabı ile liste AYNI kuralı kullanmak zorunda.
        IEnumerable<LedgerRowDto> Filter(
            IReadOnlyList<LedgerRowDto> source, IReadOnlyDictionary<Guid, HareketBelgeDto>? profile = null)
        {
            var k = profile ?? documents;
            IEnumerable<LedgerRowDto> q = source;
            if (accountId is { } h)
                q = h == Guid.Empty ? q.Where(r => r.HesapId is null) : q.Where(r => r.HesapId == h);
            if (!string.IsNullOrWhiteSpace(currency))
            {
                var d = currency.Trim();
                q = q.Where(r => string.Equals(r.Doviz, d, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(transactionType))
            {
                var t = transactionType.Trim();
                q = q.Where(r => string.Equals(r.SourceType, t, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(branch))
            {
                var sb = branch.Trim();
                // Şube belgede tutulur; künyesi olmayan satır şube süzgecine TAKILMAZ (gizlenir) —
                // "şubesi bilinmeyen" ile "o şubeye ait" karıştırılmaz.
                q = q.Where(r => k.TryGetValue(r.SourceId, out var b) && b.Sube != null
                                 && string.Equals(b.Sube.Trim(), sb, StringComparison.OrdinalIgnoreCase));
            }
            return q;
        }
    }

    /// <summary>
    /// FAZ-50 — <b>hesap-bazlı</b> kasa/banka özeti: her <c>FinancialAccount</c> için ayrı satır,
    /// artı hesap bilgisi taşımayan geçmiş kayıtlar için AYRI bir "hesap belirtilmemiş" satırı
    /// (<c>HesapId = null</c>). İki kova BİLİNÇLİ olarak karıştırılmaz.
    /// Ad çözümü çağırana aittir (rapor katmanı hesap sözlüğünü tanımaz).
    /// </summary>
    public async Task<IReadOnlyList<HesapOzetDto>> GetAccountBasedSummaryAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLedgerRowsAsync(
            [LedgerAccountType.Kasa, LedgerAccountType.Banka], from, to, ct);

        return [.. rows
            .GroupBy(r => (r.AccountType, r.HesapId))
            .Select(g =>
            {
                var entry = g.Where(x => x.Direction == LedgerDirection.Debit).Sum(x => x.Base);
                var pickup = g.Where(x => x.Direction == LedgerDirection.Credit).Sum(x => x.Base);
                return new HesapOzetDto(g.Key.AccountType, g.Key.HesapId, entry, pickup, entry - pickup);
            })
            // Sıralama: önce tür (Kasa, Banka), sonra tanımlı hesaplar, en sonda legacy kova.
            .OrderBy(x => x.Tur).ThenBy(x => x.HesapId is null).ThenBy(x => x.HesapId)];
    }

    /// <summary>Kasa & banka giriş/çıkış/bakiye özeti.</summary>
    public async Task<CashboxSummaryDto> GetCashBankSummaryAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetLedgerRowsAsync(
            [LedgerAccountType.Kasa, LedgerAccountType.Banka], from, to, ct);

        decimal cashInflow = Sum(rows, LedgerAccountType.Kasa, LedgerDirection.Debit);
        decimal cashOutflow = Sum(rows, LedgerAccountType.Kasa, LedgerDirection.Credit);
        decimal bankInflow = Sum(rows, LedgerAccountType.Banka, LedgerDirection.Debit);
        decimal bankOutflow = Sum(rows, LedgerAccountType.Banka, LedgerDirection.Credit);
        return new CashboxSummaryDto(
            cashInflow, cashOutflow, cashInflow - cashOutflow,
            bankInflow, bankOutflow, bankInflow - bankOutflow);
    }

    /// <summary>Home mini-trend (FAZ 6.2): son n ayın ay-pencereli GELİR toplamı (GetGelirGiderAsync ile
    /// aynı netleme — iade düşer). Ay çıpası UTC ayın 1'i; pencere [ayBas, sonrakiAyBas) — GetLedgerRows
    /// üst-ucu DAHİL olduğundan bitiş AddTicks(-1) ile verilir (sınır kaydı iki aya sayılmaz).</summary>
    public async Task<IReadOnlyList<AylikGelirNokta>> GetMonthlyRevenueTrendAsync(
        int monthCount = 6, DateTimeOffset? now = null, CancellationToken ct = default)
        => (await GetMonthlyRevenueExpenseTrendAsync(monthCount, now, ct))
            .Select(n => new AylikGelirNokta(n.AyBas, n.Gelir)).ToList(); // tek döngü — tam sürüme delege

    /// <summary>Finans Analiz 12-ay grafiği: son n ayın ay-pencereli GELİR + GİDER + NET KÂR toplamları
    /// (GetGelirGiderAsync ile aynı netleme — iade düşer). Ay çıpası/pencere GetAylikGelirTrendAsync
    /// ile birebir aynı (UTC ayın 1'i; bitiş AddTicks(-1) — sınır kaydı iki aya sayılmaz).</summary>
    public async Task<IReadOnlyList<AylikGelirGiderNokta>> GetMonthlyRevenueExpenseTrendAsync(
        int monthCount = 12, DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var s = now ?? DateTimeOffset.UtcNow;
        var thisMonth = new DateTimeOffset(s.Year, s.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var result = new List<AylikGelirGiderNokta>(monthCount);
        for (int i = monthCount - 1; i >= 0; i--)
        {
            var monthStart = thisMonth.AddMonths(-i);
            var gg = await GetRevenueExpenseAsync(monthStart, monthStart.AddMonths(1).AddTicks(-1), ct);
            result.Add(new AylikGelirGiderNokta(monthStart, gg.GelirToplam, gg.GiderToplam, gg.NetKar));
        }
        return result;
    }

    /// <summary>Dönem gelir-gider özeti + KDV + net kâr + SourceType kırılımı.</summary>
    public async Task<GelirGiderDto> GetRevenueExpenseAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = (await _repository.GetLedgerRowsAsync(
            [LedgerAccountType.Gelir, LedgerAccountType.Gider, LedgerAccountType.Kdv], from, to, ct))
            // PR-A: dönem kapanış fişi Gelir/Gider'i sıfırlayan İÇ virmandır (gerçek gelir/gider değil) → P&L'den HARİÇ.
            .Where(r => r.SourceType != "DonemKapanis").ToList();

        // İade faturası TERS kayıt yazar (Borç Gelir / Borç KDV) → gelir ve tahsil edilen KDV netleşir.
        var revenueCredit = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Credit).ToList();
        var revenueDebit = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Debit).ToList();
        // Gider de İKİ YÖNLÜ netlenir (4.3 adversarial Medium): DisHizmet iptali Gider'e Credit yazan
        // ilk akış — Debit-only toplam iptal sonrası hayalet gider bırakıp Karlilik/karne ile mutabakatı kırıyordu.
        var expenseDebit = rows.Where(r => r.AccountType == LedgerAccountType.Gider && r.Direction == LedgerDirection.Debit).ToList();
        var expenseCredit = rows.Where(r => r.AccountType == LedgerAccountType.Gider && r.Direction == LedgerDirection.Credit).ToList();

        decimal revenue = revenueCredit.Sum(r => r.Base) - revenueDebit.Sum(r => r.Base); // iade neti düşürür
        decimal expense = expenseDebit.Sum(r => r.Base) - expenseCredit.Sum(r => r.Base); // ters kayıt neti düşürür
        // İade'nin Borç KDV'si tahsil edilen KDV'yi DÜŞÜRÜR (KDV indirimi/input VAT DEĞİL); gerçek
        // indirim (varsa iade-dışı Borç KDV) kdvInd olarak ayrı kalır.
        decimal vatRefundRev = rows.Where(r => r.AccountType == LedgerAccountType.Kdv && r.Direction == LedgerDirection.Debit && r.SourceType == "FaturaIade").Sum(r => r.Base);
        decimal vatCollected = Sum(rows, LedgerAccountType.Kdv, LedgerDirection.Credit) - vatRefundRev;
        decimal vatDeductible = rows.Where(r => r.AccountType == LedgerAccountType.Kdv && r.Direction == LedgerDirection.Debit && r.SourceType != "FaturaIade").Sum(r => r.Base);

        // Gelir kırılımı: iade (Borç Gelir) ilgili kaynağı negatif kalem olarak gösterir → toplamla tutarlı.
        var revenueBreakdown = revenueCredit.Select(r => (r.SourceType, Tutar: r.Base))
            .Concat(revenueDebit.Select(r => (r.SourceType, Tutar: -r.Base)))
            .GroupBy(x => x.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(x => x.Tutar)))
            .OrderByDescending(k => k.Tutar).ToList();
        var expenseBreakdown = expenseDebit.Select(r => (r.SourceType, Tutar: r.Base))
            .Concat(expenseCredit.Select(r => (r.SourceType, Tutar: -r.Base)))
            .GroupBy(x => x.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(x => x.Tutar)))
            .OrderByDescending(k => k.Tutar).ToList();

        return new GelirGiderDto(revenue, expense, vatCollected, vatDeductible, revenue - expense, revenueBreakdown, expenseBreakdown);
    }

    /// <summary>Dönem-sonu özet mizan (PR-A): <paramref name="asOf"/> tarihine (dahil) kadar hesap-tipi bazında
    /// Σ Borç / Σ Alacak / net bakiye. TÜM tipler (kapanış fişi dahil — mizan gerçek defter durumunu yansıtır:
    /// kapanış öncesi Gelir/Gider dolu + DonemSonucu 0; kapanış sonrası Gelir/Gider 0 + DonemSonucu = net kâr).
    /// Bakiye toplamı 0 olmalı (defter her zaman dengeli).</summary>
    public async Task<IReadOnlyList<MizanSatirDto>> GetTrialBalanceAsync(DateTimeOffset? asOf = null, CancellationToken ct = default)
    {
        var types = new[]
        {
            LedgerAccountType.Cari, LedgerAccountType.Kasa, LedgerAccountType.Banka, LedgerAccountType.Gelir,
            LedgerAccountType.Kdv, LedgerAccountType.Gider, LedgerAccountType.Depozito, LedgerAccountType.DonemSonucu,
            // F8.1a: bakiye düzeltmesinin karşı bacağı. Eksikken düzeltme yapılmış kiracıda mizan bakiye toplamı 0
            // tutmuyordu (Cari bacağı görünür, karşı bacak görünmez) — "defter dengesi" göstergesi yanlış alarm verirdi.
            LedgerAccountType.MuhasebeDuzeltmesi
        };
        var rows = await _repository.GetLedgerRowsAsync(types, null, asOf, ct);
        return types
            .Select(t =>
            {
                var debit = rows.Where(r => r.AccountType == t && r.Direction == LedgerDirection.Debit).Sum(r => r.Base);
                var credit = rows.Where(r => r.AccountType == t && r.Direction == LedgerDirection.Credit).Sum(r => r.Base);
                return new MizanSatirDto(t, AccountName(t), debit, credit, debit - credit);
            })
            .Where(m => m.Borc != 0 || m.Alacak != 0) // hareketsiz hesabı gizle
            .ToList();
    }

    /// <summary>Hesap türü Türkçe etiketi (mizan/rapor gösterimi).</summary>
    public static string AccountName(LedgerAccountType t) => t switch
    {
        LedgerAccountType.Cari => "Cari (müşteri/tedarikçi)",
        LedgerAccountType.Kasa => "Kasa",
        LedgerAccountType.Banka => "Banka",
        LedgerAccountType.Gelir => "Gelir",
        LedgerAccountType.Kdv => "KDV",
        LedgerAccountType.Gider => "Gider",
        LedgerAccountType.Depozito => "Depozito",
        LedgerAccountType.DonemSonucu => "Dönem Sonucu (kâr/zarar)",
        LedgerAccountType.MuhasebeDuzeltmesi => "Muhasebe Düzeltmesi",
        _ => t.ToString()
    };

    /// <summary>
    /// Günlük faaliyet raporu: verilen günün ([gün 00:00, ertesi gün − tick]) operasyonel
    /// sayaçları + tutarları. Repo'da sayım/toplam; burası gün sınırlarını kurar.
    /// </summary>
    public Task<GunlukFaaliyetDto> GetDailyActivityAsync(
        DateTimeOffset day, string? branch = null, CancellationToken ct = default)
    {
        var from = new DateTimeOffset(day.Date, TimeSpan.Zero);
        var to = from.AddDays(1).AddTicks(-1);
        return _repository.GetDailyActivityAsync(from, to, branch, ct);
    }

    /// <summary>
    /// KDV listesi: dönemdeki (fatura tarihi) İptal olmayan faturaların KDV oranı bazında
    /// kırılımı (Net/KDV/Brüt + o oranı içeren fatura adedi) + genel toplamlar. Beyanname/muhasebe.
    /// </summary>
    public async Task<KdvListesiDto> GetVatListAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetVatLineRowsAsync(from, to, ct);

        var rowList = rows
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
            rowList,
            rowList.Sum(s => s.Net),
            rowList.Sum(s => s.Kdv),
            rowList.Sum(s => s.Brut),
            rows.Select(r => r.InvoiceId).Distinct().Count());
    }

    /// <summary>
    /// FAZ-53 — KDV GENİŞ format (canlı <c>kdv_raporu.aspx</c> grain'i): SATIR = belge,
    /// SÜTUN = KDV oranı. <paramref name="includePurchases"/> true ise gelen e-Faturaların indirilecek
    /// KDV'si de listelenir.
    ///
    /// <para><b>Neden pivot (<see cref="GetVatListAsync"/>) genişletilmedi de yeni bir görünüm
    /// açıldı:</b> alış KDV'si mevcut pivotun "Net/KDV/Brüt" kolonlarına eklenseydi, hesaplanan
    /// (borç) ve indirilecek (alacak) KDV tek toplamda erirdi — beyanname için anlamsız bir sayı.
    /// Pivot SATIŞ-only kaldı (regresyon sıfır), tür ayrımı ve <c>NetKdv = Satış − Alış</c> bu
    /// görünümde durur. (FAZ-53 spec'i 4. maddede <c>GetKdvListesiAsync</c>'e <c>dahilAlis</c>
    /// eklemeyi öneriyordu; muhasebe olarak yanlış olduğu için o madde uygulanmadı.)</para>
    /// </summary>
    public async Task<KdvGenisDto> GetVatExtendedAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, bool includePurchases = false,
        CancellationToken ct = default)
    {
        var (rows, skipped) = await _repository.GetVatExtendedRowsAsync(from, to, includePurchases, ct);

        var sale = rows.Where(r => !r.AlisMi).ToList();
        var purchase = rows.Where(r => r.AlisMi).ToList();

        return new KdvGenisDto(
            rows,
            sale.Sum(r => r.ToplamNet), sale.Sum(r => r.ToplamKdv),
            purchase.Sum(r => r.ToplamNet), purchase.Sum(r => r.ToplamKdv),
            sale.Count, purchase.Count, skipped);
    }

    /// <summary>
    /// Ek hizmet satış raporu: dönemde (kalem eklenme tarihi) İptal olmayan kiralara satılan ek
    /// hizmetlerin ADINA göre özeti (toplam miktar/net/KDV/brüt + kaç kirada) + genel toplamlar.
    /// </summary>
    public async Task<EkHizmetRaporDto> GetAddOnReportAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetAddOnSalesRowsAsync(from, to, ct);

        var rowList = rows
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
            rowList,
            rowList.Sum(s => s.Net),
            rowList.Sum(s => s.Kdv),
            rowList.Sum(s => s.Brut),
            rows.Select(r => r.RentalId).Distinct().Count());
    }

    /// <summary>
    /// Tüm cariler için net bakiye (Σ Borç − Σ Alacak), sıfır olmayanlar, borçtan-alacağa sıralı.
    ///
    /// <para>FAZ-62: net bakiye hesabı AYNEN korundu (<c>Σ SignedBase</c>); yanına brüt Borç/Alacak
    /// toplamları ve cari kart bilgileri eklendi. <paramref name="filter"/> null iken davranış
    /// öncekiyle BİREBİR aynıdır — mevcut çağrılar (API, export) daralmaz.</para>
    ///
    /// <para>Sıfır-bakiye elemesi filtreden ÖNCE uygulanır (rapor "bakiyeli cariler" raporu);
    /// hareketi olup net'i sıfırlanan cari yine listelenmez, ama artık brüt Borç/Alacak sütunlarıyla
    /// "hiç hareket yok" durumundan ayrılabilir hâle geldiği için bu ayrım kaybolmuyor.</para>
    /// </summary>
    public async Task<IReadOnlyList<CariBalanceDto>> GetAccountBalancesAsync(
        CariBakiyeFilter? filter = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetAccountLedgerRowsAsync(asOf: null, ct);
        var cards = (await _repository.GetAccountCardsAsync(ct)).ToDictionary(k => k.CariId);

        var list = rows
            .GroupBy(r => (r.CariId, r.Ad))
            .Select(g =>
            {
                cards.TryGetValue(g.Key.CariId, out var k);
                return new CariBalanceDto(
                    g.Key.CariId, g.Key.Ad, g.Sum(Signed),
                    ToplamBorc: g.Where(r => r.Direction == LedgerDirection.Debit).Sum(r => r.Base),
                    ToplamAlacak: g.Where(r => r.Direction == LedgerDirection.Credit).Sum(r => r.Base),
                    Telefon: k?.Telefon, Email: k?.Email, Banka: k?.Banka, Doviz: k?.Doviz,
                    OzelKod: k?.OzelKod, Sinif: k?.Sinif,
                    Kurumsal: k?.Kurumsal ?? false, Pasif: k?.Pasif ?? false);
            })
            .Where(b => b.Bakiye != 0m)
            .OrderByDescending(b => b.Bakiye)
            .ToList();

        if (filter is null) return list;

        var taxNos = cards.ToDictionary(x => x.Key, x => x.Value.VergiNo);
        IEnumerable<CariBalanceDto> q = list;

        if (Filled(filter.Ara))
        {
            var t = filter.Ara!.Trim();
            q = q.Where(b => Contains(b.Ad, t) || Contains(b.Telefon, t) || Contains(b.Email, t)
                             || Contains(taxNos.GetValueOrDefault(b.CariId), t));
        }
        if (Filled(filter.OzelKod)) q = q.Where(b => AreEqual(b.OzelKod, filter.OzelKod));
        if (Filled(filter.Sinif)) q = q.Where(b => AreEqual(b.Sinif, filter.Sinif));
        if (Filled(filter.Doviz)) q = q.Where(b => AreEqual(b.Doviz, filter.Doviz));
        if (filter.Kurumsal is { } exchangeRate) q = q.Where(b => b.Kurumsal == exchangeRate);
        if (string.Equals(filter.BakiyeTuru, "borclu", StringComparison.OrdinalIgnoreCase))
            q = q.Where(b => b.Bakiye > 0m);
        else if (string.Equals(filter.BakiyeTuru, "alacakli", StringComparison.OrdinalIgnoreCase))
            q = q.Where(b => b.Bakiye < 0m);
        // Min tutar MUTLAK bakiyeye uygulanır: −5.000'lik bir alacaklı cariyi "küçük" saymak yanlış olur.
        if (filter.MinTutar is { } min) q = q.Where(b => Math.Abs(b.Bakiye) >= min);

        return q.ToList();

        static bool Filled(string? s) => !string.IsNullOrWhiteSpace(s);
        static bool Contains(string? source, string searched)
            => source is not null && source.Contains(searched, StringComparison.OrdinalIgnoreCase);
        static bool AreEqual(string? a, string? b)
            => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// FAZ-61 — extre özeti: fatura seviyesinde müşteri + plaka + vade görünümü.
    /// Tutar BRÜT (fatura-bazlı mahsup sistemde yok); iadeler negatif işaretli.
    /// </summary>
    public Task<IReadOnlyList<ExtreOzetiRowDto>> GetStatementSummaryAsync(
        ExtreOzetiFilter? filter = null, DateTimeOffset? asOf = null, CancellationToken ct = default)
        => _repository.GetStatementSummaryRowsAsync(filter, asOf ?? DateTimeOffset.UtcNow, ct);

    /// <summary>
    /// Cari borç yaşlandırma (v1: BRÜT borç, tahsilat mahsubu yok). Borç satırları yaşa (asOf−Tarih,
    /// gün) göre 0-30 / 31-60 / 61-90 / 90+ kovalarına. Yalnız borç bakiyesi olan cariler.
    /// </summary>
    public async Task<IReadOnlyList<AgingRowDto>> GetAgingAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var rows = await _repository.GetAccountLedgerRowsAsync(asOf, ct);
        return rows
            .Where(r => r.Direction == LedgerDirection.Debit) // yalnız borç (brüt)
            .GroupBy(r => (r.CariId, r.Ad))
            .Select(g =>
            {
                decimal b0 = 0, b30 = 0, b60 = 0, b90 = 0;
                foreach (var r in g)
                {
                    var day = (asOf.UtcDateTime.Date - r.Tarih.UtcDateTime.Date).Days;
                    if (day <= 30) b0 += r.Base;
                    else if (day <= 60) b30 += r.Base;
                    else if (day <= 90) b60 += r.Base;
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
    public async Task<DolulukDto> GetOccupancyAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var vehicleCount = (await _repository.GetVehicleStatusesAsync(ct)).Count;
        var rows = await _repository.GetRentalIntervalsAsync(from, to, ct);

        var fromD = from.UtcDateTime.Date;
        var toD = to.UtcDateTime.Date;
        int periodDays = toD >= fromD ? (toD - fromD).Days + 1 : 0;

        int rentalDays = rows.Sum(r => OverlapDays(r.Bas.UtcDateTime.Date, r.Bit.UtcDateTime.Date, fromD, toD));
        int vehicleDays = vehicleCount * periodDays;
        decimal percent = vehicleDays > 0 ? Math.Round((decimal)rentalDays * 100m / vehicleDays, 2, MidpointRounding.AwayFromZero) : 0m;

        return new DolulukDto(vehicleCount, periodDays, vehicleDays, rentalDays, percent);
    }

    /// <summary>İki kapsayıcı tarih aralığının kesişim gün sayısı (kesişim yoksa 0).</summary>
    private static int OverlapDays(DateTime aStart, DateTime aBit, DateTime bStart, DateTime bBit)
    {
        var lo = aStart > bStart ? aStart : bStart;
        var hi = aBit < bBit ? aBit : bBit;
        return hi >= lo ? (hi - lo).Days + 1 : 0;
    }

    /// <summary>
    /// FAZ-75 — sigorta/muayene birleşik envanteri. Filtre yalnız DARALTIR; kayıt üretmez.
    /// </summary>
    public async Task<IReadOnlyList<SigortaMuayeneRow>> GetInsuranceInspectionAsync(
        SigortaMuayeneFilter? filter = null, CancellationToken ct = default)
    {
        // Yetki: rapor sayfası zaten rol-kapılı (Authorize) ve bu servis diğer raporlarla AYNI
        // yüzeyde — ReportService'te guard yok, tutarlılık için burada da yok.
        var rows = await _repository.GetInsuranceInspectionRowsAsync(ct);
        var f = filter ?? new SigortaMuayeneFilter();

        IEnumerable<SigortaMuayeneRow> q = rows;
        if (!string.IsNullOrWhiteSpace(f.AracSahibi))
            q = q.Where(r => Common.TurkishText.EqualsIgnoreTurkishCase(r.AracSahibi, f.AracSahibi));
        if (!string.IsNullOrWhiteSpace(f.Plaka))
        {
            // Plaka DB'de normalize (34AA01); arama terimi de normalize edilir (FAZ-63 dersi).
            var p = new string(f.Plaka.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            if (p.Length > 0) q = q.Where(r => r.Plaka.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
        if (f.Tur != InsuranceInspectionType.Hepsi)
        {
            // Tür seçilince o belgesi OLMAYAN araç da görünmeli (eksik belge raporun asıl konusu);
            // bu yüzden tür filtresi satırı ELEMEZ, yalnız BitisEnGec ile birlikte anlam kazanır.
            if (f.BitisEnGec is { } enGec)
                q = q.Where(r => r.End(f.Tur) is null || r.End(f.Tur) <= enGec);
        }
        else if (f.BitisEnGec is { } enGec2)
        {
            // Tür seçilmemişse HERHANGİ bir belgesi o tarihten önce bitenler.
            q = q.Where(r => new[] { r.TrafikBitis, r.KaskoBitis, r.MuayeneBitis, r.MtvVade, r.ZIzniBitis, r.SeyrusiferBitis }
                .Any(b => b is not null && b <= enGec2));
        }
        return q.ToList();
    }

    // ------------------------------------------------------------------
    // FAZ-77 — filo & doluluk grafik derinliği
    // ------------------------------------------------------------------

    /// <summary>İleri-bakış penceresi varsayılanı (Dönecekler/Çıkacaklar/Giden Rez).</summary>
    public const int DefaultFleetWindow = 7;

    /// <summary>Gün-kırılımlı doluluk için tavan (satır sayısı = gün × seri).</summary>
    public const int MaxOccupancyDays = 366;

    /// <summary>
    /// FAZ-77 — şube kırılımlı filo durumu. <see cref="GetFleetUtilizationAsync"/> DEĞİŞMEDEN
    /// durur (Home.razor gibi tüketiciler bozulmasın); bu onun yerine geçmez, yanında durur.
    /// Atıf ve payda kuralları için bkz. <see cref="FiloSubeRow"/>.
    /// </summary>
    public async Task<FiloSubeDto> GetFleetUtilizationByBranchAsync(
        int windowDays = DefaultFleetWindow, CancellationToken ct = default)
    {
        windowDays = Math.Clamp(windowDays, 1, 90);
        var today = DateTimeOffset.UtcNow.UtcDateTime.Date;
        var windowEnd = today.AddDays(windowDays);

        var raw = await _repository.GetFleetBranchRawAsync(
            new DateTimeOffset(today, TimeSpan.Zero),
            new DateTimeOffset(windowEnd.AddDays(1).AddTicks(-1), TimeSpan.Zero), ct);

        var bafCount = raw.AcikBafSubeleri.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());

        // Seriler: araç TAŞIMAYAN ama kira/rez/BAF taşıyan şube de satır almalı (0-filo satırı
        // gizlenirse o şubenin işi rapordan sessizce düşerdi).
        var branches = raw.Araclar.Select(a => a.Sube)
            .Concat(raw.Kiralar.Select(k => k.Sube))
            .Concat(raw.Rezervasyonlar.Select(r => r.Sube))
            .Concat(raw.AcikBafSubeleri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.CurrentCulture)
            .ToList();

        var rows = new List<FiloSubeRow>(branches.Count);
        foreach (var branch in branches)
        {
            var a = raw.Araclar.Where(x => x.Sube == branch).ToList();
            var k = raw.Kiralar.Where(x => x.Sube == branch).ToList();

            int Status(VehicleStatus s) => a.Count(x => x.Durum == s);
            int fleet = a.Count, isSold = Status(VehicleStatus.Satildi), inactive = Status(VehicleStatus.Pasif);
            int onRent = Status(VehicleStatus.Kirada);

            // Payda = KULLANILABİLİR filo; ≤0 ise oran anlamsız → null (0 değil).
            int usable = fleet - isSold - inactive;
            decimal? occupancy = usable > 0
                ? Math.Round((decimal)onRent * 100m / usable, 2, MidpointRounding.AwayFromZero)
                : null;

            bool Day(DateTimeOffset d, DateTime target) => d.UtcDateTime.Date == target;
            bool Forward(DateTimeOffset d) => d.UtcDateTime.Date > today && d.UtcDateTime.Date <= windowEnd;

            rows.Add(new FiloSubeRow(
                branch, fleet, Status(VehicleStatus.Musait), onRent, Status(VehicleStatus.Serviste),
                inactive, isSold,
                a.Count(x => x.FiloDurum == FleetLifecycleStatus.IkinciElSatis),
                bafCount.GetValueOrDefault(branch),
                occupancy,
                Cikislar: k.Count(x => Day(x.Bas, today)),
                Donusler: k.Count(x => Day(x.Bit, today)),
                Cikacaklar: k.Count(x => Forward(x.Bas)),
                Donecekler: k.Count(x => Forward(x.Bit)),
                GidenRez: raw.Rezervasyonlar.Count(x => x.Sube == branch && x.Bas.UtcDateTime.Date <= windowEnd)));
        }

        return new FiloSubeDto(rows, windowDays);
    }

    /// <summary>
    /// FAZ-77 — gün-kırılımlı doluluk (+ Karşılaştır boyutu). <see cref="GetOccupancyAsync"/>
    /// DEĞİŞMEDEN durur; ikisi AYNI <c>OverlapDays</c> helper'ını kullanır, bu yüzden gün
    /// toplamları birbirine eşittir (çapraz-doğrulama testi kalıcı kilit).
    /// Payda semantiği için bkz. <see cref="DolulukGunlukDto"/>.
    /// </summary>
    public async Task<DolulukGunlukDto> GetOccupancyDailyAsync(
        DateTimeOffset from, DateTimeOffset to, OccupancyDimension size = OccupancyDimension.Yok,
        CancellationToken ct = default)
    {
        var fromD = from.UtcDateTime.Date;
        var toD = to.UtcDateTime.Date;
        if (toD < fromD) (fromD, toD) = (toD, fromD);
        if ((toD - fromD).Days + 1 > MaxOccupancyDays) toD = fromD.AddDays(MaxOccupancyDays - 1);
        int periodDays = (toD - fromD).Days + 1;

        var raw = await _repository.GetOccupancyAttributionAsync(
            new DateTimeOffset(fromD, TimeSpan.Zero),
            new DateTimeOffset(toD.AddDays(1).AddTicks(-1), TimeSpan.Zero), ct);

        // Seri anahtarı + payda: Şube/Grup GERÇEK filo bölüntüsü (payda = kendi araçları);
        // Rezervasyon Kaynağı bölüntü DEĞİL (araç bir kaynağa ait olmaz) → payda TÜM FİLO.
        static string RentalSeries(DolulukKiraAtifRow r, OccupancyDimension b) => b switch
        {
            OccupancyDimension.Sube => r.Sube,
            OccupancyDimension.AracGrubu => r.Grup,
            // Kaynak modunda kiranın kaynağı yoktur → AYRI etiketli tek kovaya düşer. Düz "Tüm filo"
            // deseydik, "Tüm filo" adlı bir rezervasyon kaynağı tanımlanırsa iki metrik aynı satırda
            // birleşirdi.
            OccupancyDimension.RezervasyonKaynagi => WholeFleetRental,
            _ => WholeFleet
        };
        static string ReservationSeries(DolulukRezAtifRow r, OccupancyDimension b) => b switch
        {
            OccupancyDimension.Sube => r.Sube,
            OccupancyDimension.AracGrubu => r.Grup,
            OccupancyDimension.RezervasyonKaynagi => r.Kaynak,
            _ => WholeFleet
        };

        var totalVehicles = raw.Araclar.Count;
        Dictionary<string, int> denominatorCount = size switch
        {
            OccupancyDimension.Sube => raw.Araclar.GroupBy(a => a.Sube).ToDictionary(g => g.Key, g => g.Count()),
            OccupancyDimension.AracGrubu => raw.Araclar.GroupBy(a => a.Grup).ToDictionary(g => g.Key, g => g.Count()),
            _ => new Dictionary<string, int> { [WholeFleet] = totalVehicles }
        };

        // Kaynak boyutunda kira satırlarının kaynağı yok → hepsi tek "tüm filo" serisine düşer;
        // bu bilinçli: kaynak kırılımı REZERVASYON metriğidir, kira değil.
        var series = size switch
        {
            OccupancyDimension.RezervasyonKaynagi =>
                raw.Rezervasyonlar.Select(r => r.Kaynak).Append(WholeFleetRental).Distinct(StringComparer.Ordinal),
            OccupancyDimension.Yok => [WholeFleet],
            _ => denominatorCount.Keys
                .Concat(raw.Kiralar.Select(r => RentalSeries(r, size)))
                .Concat(raw.Rezervasyonlar.Select(r => ReservationSeries(r, size)))
                .Distinct(StringComparer.Ordinal)
        };
        var seriesList = series.OrderBy(s => s, StringComparer.CurrentCulture).ToList();

        var rows = new List<DolulukGunRow>(periodDays * Math.Max(1, seriesList.Count));
        for (var g = fromD; g <= toD; g = g.AddDays(1))
        {
            foreach (var seriesItem in seriesList)
            {
                int rentalDays = raw.Kiralar.Count(r => RentalSeries(r, size) == seriesItem
                    && OverlapDays(r.Bas.UtcDateTime.Date, r.Bit.UtcDateTime.Date, g, g) > 0);
                int resDays = raw.Rezervasyonlar.Count(r => ReservationSeries(r, size) == seriesItem
                    && OverlapDays(r.Bas.UtcDateTime.Date, r.Bit.UtcDateTime.Date, g, g) > 0);

                int denominator = size == OccupancyDimension.RezervasyonKaynagi
                    ? totalVehicles
                    : denominatorCount.GetValueOrDefault(seriesItem);

                decimal? Y(int count) => denominator > 0
                    ? Math.Round((decimal)count * 100m / denominator, 2, MidpointRounding.AwayFromZero)
                    : null;

                rows.Add(new DolulukGunRow(
                    DateOnly.FromDateTime(g), seriesItem, denominator, rentalDays, resDays, Y(rentalDays), Y(resDays)));
            }
        }

        var description = size switch
        {
            OccupancyDimension.Sube => "Payda: o şubenin KENDİ araçları — yüzdeler şubenin kendi doluluğudur, toplamları genel doluluğa eşit değildir.",
            OccupancyDimension.AracGrubu => "Payda: o grubun KENDİ araçları — yüzdeler grubun kendi doluluğudur, toplamları genel doluluğa eşit değildir.",
            OccupancyDimension.RezervasyonKaynagi => "Payda: TÜM FİLO — kaynak bir filo bölüntüsü değildir; yüzde 'kaynak filo kapasitesinin ne kadarını doldurdu' demektir ve toplanabilir. Kira satırları kaynak taşımaz.",
            _ => "Payda: tüm filo."
        };

        return new DolulukGunlukDto(
            rows, size, description, periodDays,
            rows.Sum(x => x.KiraGun), rows.Sum(x => x.RezGun));
    }

    /// <summary>Kırılımsız seri adı (tek seri modu).</summary>
    public const string WholeFleet = "Tüm filo";

    /// <summary>Kaynak boyutunda kiraların düştüğü kova — kaynak adlarıyla çakışmaması için ayrı.</summary>
    public const string WholeFleetRental = "Tüm filo (kira)";
    /// Dönem tahsilat-fatura mutabakatı: kesilen fatura (İptal hariç) vs alınan tahsilat (ters hariç)
    /// + fark. Repo sayım/toplamı yapar; Fark = FaturaToplam − TahsilatToplam (repo'da hesaplı). Pass-through.
    /// </summary>
    public Task<TahsilatFaturaDto> GetCollectionInvoiceAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetCollectionInvoiceAsync(from, to, ct);

    /// <summary>
    /// FAZ-68 — tahsilat raporu SATIR modu (sözleşme başına mutabakat). Dönem-toplamı modu
    /// <see cref="GetCollectionInvoiceAsync"/> ile yan yana durur; biri diğerinin yerine geçmez.
    /// </summary>
    public Task<IReadOnlyList<TahsilatMutabakatRowDto>> GetCollectionReconciliationAsync(
        TahsilatMutabakatFilter? filter = null, CancellationToken ct = default)
        => _repository.GetCollectionReconciliationRowsAsync(filter, ct);

    /// <summary>
    /// FAZ-78 — ek hizmet raporu SATIR-BAZLI detay. Ad-bazlı özet
    /// (<see cref="GetAddOnReportAsync"/>) olduğu gibi durur; bu onun yerine geçmez.
    /// </summary>
    public Task<IReadOnlyList<EkHizmetDetayRow>> GetAddOnDetailAsync(
        EkHizmetDetayFilter? filter = null, CancellationToken ct = default)
        => _repository.GetAddOnDetailRowsAsync(filter, ct);

    /// <summary>
    /// FAZ-27 — karşılaştırmalı durum analizi (hacim pivotu). Salt okuma; tutar üretmez.
    /// </summary>
    public Task<KarsilastirmaliAnalizDto> GetComparativeAnalysisAsync(
        KarsilastirmaliAnalizFilter? filter = null, CancellationToken ct = default)
        => _repository.GetComparativeAnalysisAsync(filter ?? new KarsilastirmaliAnalizFilter(), ct);

    /// <summary>Filo durum dağılımı + aktif kira sayısı.</summary>
    public async Task<FleetUtilizationDto> GetFleetUtilizationAsync(CancellationToken ct = default)
    {
        var statuses = await _repository.GetVehicleStatusesAsync(ct);
        var activeRental = await _repository.GetActiveRentalCountAsync(ct);
        int Count(VehicleStatus s) => statuses.Count(x => x == s);
        return new FleetUtilizationDto(
            statuses.Count,
            Count(VehicleStatus.Musait), Count(VehicleStatus.Kirada),
            Count(VehicleStatus.Serviste), Count(VehicleStatus.Pasif), Count(VehicleStatus.Satildi),
            activeRental);
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
    public Task<IReadOnlyList<PeriyodikServisRow>> GetPeriodicServiceAsync(
        PeriyodikServisFilter? filter = null, CancellationToken ct = default)
        => _repository.GetPeriodicServiceRowsAsync(filter, ct);

    /// <summary>Kira KM detay raporu (roadmap H1): dönmüş kiraların çıkış/dönüş/katedilen km'si.</summary>
    public Task<IReadOnlyList<KmDetayRow>> GetKmDetailAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetKmDetailRowsAsync(from, to, ct);

    /// <summary>Rezervasyon kaynak raporu (roadmap H2): kaynak başına adet/gün/ciro.</summary>
    public Task<IReadOnlyList<RezervasyonKaynakRow>> GetReservationSourceAsync(
        RezervasyonKaynakFilter? filter = null, CancellationToken ct = default)
        => _repository.GetReservationSourceRowsAsync(filter ?? new RezervasyonKaynakFilter(), ct);

    /// <summary>Fatura dönem raporu (roadmap H2): tarih filtreli fatura listesi (vade/cari/tutar/durum).</summary>
    public Task<IReadOnlyList<FaturaDonemRow>> GetInvoicePeriodAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
        => _repository.GetInvoicePeriodRowsAsync(from, to, ct);

    /// <summary>
    /// FAZ-53 — kira faturalama durumu ("faturalanmamış kira" sekmesi). Dönemle KESİŞEN kiralar
    /// listelenir; süzgeçle yalnız faturalanmamışlar (veya yalnız faturalanmışlar) daraltılabilir.
    /// Fatura dönem raporu KESİLMİŞ belgeyi anlatır, bu görünüm EKSİK kalanı.
    /// </summary>
    public Task<IReadOnlyList<KiraFaturaDurumRow>> GetRentalInvoiceStatusAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null,
        KiraFaturaDurumFilter? filter = null, CancellationToken ct = default)
        => _repository.GetRentalInvoiceStatusRowsAsync(from, to, filter, ct);

    /// <summary>Araç durum-takip raporu (roadmap H3): gün kırılımı dolu/bakım/boş (varsayılan son 30 gün).</summary>
    public Task<IReadOnlyList<AracDurumTakipRow>> GetVehicleStatusTrackingAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, string? branch = null,
        CancellationToken ct = default)
        => GetVehicleStatusTrackingAsync(new AracDurumTakipFilter { Sube = branch }, from, to, ct);

    /// <summary>
    /// FAZ-12 — araç durum-takip GÜN kırılımı, tam filtreyle. Şube-only kısayolu (yukarıdaki aşırı
    /// yükleme) bunu çağırır; davranışı filtresiz çağrıda BİREBİR eskisi gibidir.
    /// </summary>
    public Task<IReadOnlyList<AracDurumTakipRow>> GetVehicleStatusTrackingAsync(
        AracDurumTakipFilter filter, DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var (start, bit) = TrackingWindow(from, to);
        return _repository.GetVehicleStatusTrackingRowsAsync(start, bit, filter, ct);
    }

    /// <summary>
    /// FAZ-12 Bölüm A — araç durum-takip ARAÇ kırılımı: araç başına dolu/bakım/baf/boş GÜN.
    /// Gün kırılımıyla aynı pencere ve aynı araç süzgecini kullanır (iki görünüm ayrışmaz).
    /// </summary>
    public Task<IReadOnlyList<AracDurumTakipAracRow>> GetVehicleStatusTrackingByVehicleAsync(
        AracDurumTakipFilter? filter = null, DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        var (start, bit) = TrackingWindow(from, to);
        return _repository.GetVehicleStatusTrackingByVehicleRowsAsync(start, bit, filter, ct);
    }

    /// <summary>Varsayılan pencere: son 30 gün (bitiş dahil) — iki görünüm için TEK yerde.</summary>
    private static (DateTimeOffset Bas, DateTimeOffset Bit) TrackingWindow(DateTimeOffset? from, DateTimeOffset? to)
    {
        var bit = to ?? DateTimeOffset.UtcNow;
        return (from ?? bit.AddDays(-29), bit);
    }

    /// <summary>
    /// FAZ-12 Bölüm B — araç günlük durum: verilen GÜNDE aktif kiraların araç-bazlı günlük gelir
    /// kesiti (varsayılan bugün). <b>PROJEKSİYONDUR</b> — sözleşme tutarının faturalanan gün
    /// sayısına düz bölümü; deftere yazılmaz, P&amp;L raporlarına girmez.
    /// </summary>
    public Task<IReadOnlyList<AracGunlukDurumRow>> GetVehicleDailyStatusAsync(
        DateTimeOffset? day = null, AracGunlukDurumFilter? filter = null, CancellationToken ct = default)
        => _repository.GetVehicleDailyStatusRowsAsync(day ?? DateTimeOffset.UtcNow, filter, ct);

    /// <summary>
    /// FAZ-12 Bölüm C (KARARLAR.md "Seçenek B") — ek hizmet raporunun ARAÇ bazlı pivot modu:
    /// satır = araç, sütun = hizmet adı, hücre = brüt. Ad-bazlı özet
    /// (<see cref="GetAddOnReportAsync"/>) DEĞİŞMEZ; bu onun yerine geçmez.
    ///
    /// <para>Değişmez: pivotun genel toplamı aynı pencerede özetin <c>ToplamBrut</c>'una EŞİTTİR —
    /// pivot para üretmez, var olanı başka eksende dizer.</para>
    /// </summary>
    public async Task<EkHizmetAracPivotDto> GetAddOnVehiclePivotAsync(
        DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken ct = default)
    {
        var rows = await _repository.GetAddOnVehicleSalesRowsAsync(from, to, ct);
        if (rows.Count == 0) return new EkHizmetAracPivotDto([], [], [], 0m);

        // Sütun sırası: en çok satan hizmet solda (canlıdaki sabit ~25 kolonun dinamik karşılığı).
        var columns = rows.GroupBy(r => r.Ad)
            .Select(g => (Ad: g.Key, Brut: g.Sum(x => x.Brut)))
            .OrderByDescending(x => x.Brut).ThenBy(x => x.Ad, StringComparer.CurrentCulture)
            .Select(x => x.Ad).ToList();
        var columnIndex = columns.Select((name, i) => (ad: name, i)).ToDictionary(x => x.ad, x => x.i);

        // Araç kimliği null (silinmiş araç) satırları TEK mutabakat satırında toplanır — atılsalardı
        // pivot toplamı özetten kayardı.
        var rowList = rows
            .GroupBy(r => r.VehicleId)
            .Select(g =>
            {
                var first = g.First();
                var cells = new decimal[columns.Count];
                foreach (var r in g) cells[columnIndex[r.Ad]] += r.Brut;
                return new EkHizmetAracPivotSatir(
                    g.Key, first.Plaka, first.Grup, first.Sipp,
                    cells, g.Sum(r => r.Brut), g.Count());
            })
            .OrderByDescending(s => s.Toplam).ThenBy(s => s.Plaka, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columnTotal = columns
            .Select((_, i) => rowList.Sum(s => s.Hucreler[i]))
            .ToList();

        return new EkHizmetAracPivotDto(columns, rowList, columnTotal, rowList.Sum(s => s.Toplam));
    }

    /// <summary>Müşteri CRM segment (roadmap N3): kira sayısı/ciro/segment. FAZ-41: opsiyonel süzgeç.</summary>
    public Task<IReadOnlyList<MusteriSegmentRow>> GetCustomerSegmentAsync(
        MusteriSegmentFilter? filter = null, CancellationToken ct = default)
        => _repository.GetCustomerSegmentRowsAsync(filter, ct);

    /// <summary>FAZ-41 — segment süzgeci seçenekleri (filtresiz kiralardan; süzgeç kendini kilitlemesin).</summary>
    public Task<MusteriSegmentSecenekleri> GetCustomerSegmentOptionsAsync(CancellationToken ct = default)
        => _repository.GetCustomerSegmentOptionsAsync(ct);

    /// <summary>Personel çalışma grafiği (roadmap N3): personel başına BAF tahsis sayısı.</summary>
    public Task<IReadOnlyList<PersonelCalismaRow>> GetPersonnelWorkAsync(CancellationToken ct = default)
        => _repository.GetPersonnelWorkRowsAsync(ct);

    private static decimal Signed(CariLedgerRowDto r)
        => r.Direction == LedgerDirection.Debit ? r.Base : -r.Base;

    private static List<GelirGiderKalemDto> Breakdown(IEnumerable<LedgerRowDto> rows)
        => rows.GroupBy(r => r.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(r => r.Base)))
            .OrderByDescending(k => k.Tutar).ToList();

    private static decimal Sum(IEnumerable<LedgerRowDto> rows, LedgerAccountType type, LedgerDirection dir)
        => rows.Where(r => r.AccountType == type && r.Direction == dir).Sum(r => r.Base);
}
