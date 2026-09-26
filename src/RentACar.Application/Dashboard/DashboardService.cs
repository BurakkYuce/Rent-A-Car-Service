using RentACar.Application.Reporting;

namespace RentACar.Application.Dashboard;

/// <summary>Panel özet kartları (roadmap D3): filo durumu + günün operasyonu + kasa/banka bakiye + açık bakiye.</summary>
public sealed record DashboardDto(
    int AktifKira, int ToplamArac, int MusaitArac, int KiradaArac,
    int BugunCikis, int BugunDonus, int BugunTahsilatAdet, decimal BugunTahsilatTutar,
    decimal KasaBakiye, decimal BankaBakiye, decimal AcikBakiye);

/// <summary>
/// Dashboard (roadmap D3): MEVCUT <see cref="ReportService"/> üstünden salt-okur derleme — yeni tablo/sorgu
/// YOK, tek doğruluk kaynağı raporlar. Yetki gerektirmez (panel her oturuma açık; veri tenant-kapsamlı).
/// </summary>
public sealed class DashboardService(ReportService report)
{
    private readonly ReportService _report = report;

    public async Task<DashboardDto> GetAsync(DateTimeOffset day, CancellationToken ct = default)
    {
        var fleet = await _report.GetFleetUtilizationAsync(ct);
        var daily = await _report.GetDailyActivityAsync(day, ct: ct);
        var kb = await _report.GetCashBankSummaryAsync(ct: ct);   // tüm zaman bakiye
        var tf = await _report.GetCollectionInvoiceAsync(ct: ct);     // tüm zaman fatura-tahsilat farkı

        return new DashboardDto(
            fleet.AktifKira, fleet.Toplam, fleet.Musait, fleet.Kirada,
            daily.Cikis, daily.Donus, daily.TahsilatAdet, daily.TahsilatTutar,
            kb.KasaBakiye, kb.BankaBakiye, tf.Fark);
    }
}
