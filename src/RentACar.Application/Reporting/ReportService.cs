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

        var gelirRows = rows.Where(r => r.AccountType == LedgerAccountType.Gelir && r.Direction == LedgerDirection.Credit).ToList();
        var giderRows = rows.Where(r => r.AccountType == LedgerAccountType.Gider && r.Direction == LedgerDirection.Debit).ToList();

        decimal gelir = gelirRows.Sum(r => r.Base);
        decimal gider = giderRows.Sum(r => r.Base);
        decimal kdvTahsil = Sum(rows, LedgerAccountType.Kdv, LedgerDirection.Credit);
        decimal kdvInd = Sum(rows, LedgerAccountType.Kdv, LedgerDirection.Debit);

        var gelirKirilim = Kirilim(gelirRows);
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
            Count(VehicleStatus.Stokta), Count(VehicleStatus.Musait), Count(VehicleStatus.Kirada),
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
