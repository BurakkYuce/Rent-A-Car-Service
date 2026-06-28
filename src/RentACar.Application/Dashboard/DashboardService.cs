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

    public async Task<DashboardDto> GetAsync(DateTimeOffset gun, CancellationToken ct = default)
    {
        var filo = await _report.GetFleetUtilizationAsync(ct);
        var gunluk = await _report.GetGunlukFaaliyetAsync(gun, ct);
        var kb = await _report.GetKasaBankaSummaryAsync(ct: ct);   // tüm zaman bakiye
        var tf = await _report.GetTahsilatFaturaAsync(ct: ct);     // tüm zaman fatura-tahsilat farkı

        return new DashboardDto(
            filo.AktifKira, filo.Toplam, filo.Musait, filo.Kirada,
            gunluk.Cikis, gunluk.Donus, gunluk.TahsilatAdet, gunluk.TahsilatTutar,
            kb.KasaBakiye, kb.BankaBakiye, tf.Fark);
    }
}
