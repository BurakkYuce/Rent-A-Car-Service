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

    private static decimal Signed(CariLedgerRowDto r)
        => r.Direction == LedgerDirection.Debit ? r.Base : -r.Base;

    private static List<GelirGiderKalemDto> Kirilim(IEnumerable<LedgerRowDto> rows)
        => rows.GroupBy(r => r.SourceType)
            .Select(g => new GelirGiderKalemDto(g.Key, g.Sum(r => r.Base)))
            .OrderByDescending(k => k.Tutar).ToList();

    private static decimal Sum(IEnumerable<LedgerRowDto> rows, LedgerAccountType type, LedgerDirection dir)
        => rows.Where(r => r.AccountType == type && r.Direction == dir).Sum(r => r.Base);
}
