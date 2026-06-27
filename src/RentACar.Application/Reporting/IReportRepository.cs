using RentACar.Domain.Enums;

namespace RentACar.Application.Reporting;

/// <summary>
/// Salt-okunur defter sorgusu (raporlama için). Verilen hesap türleri + tarih aralığındaki
/// AccountLedgerEntry satırlarını base tutarıyla (Amount×Rate) düz DTO olarak döndürür.
/// Tenant izolasyonu DbContext/RLS ile otomatiktir.
/// </summary>
public interface IReportRepository
{
    Task<IReadOnlyList<LedgerRowDto>> GetLedgerRowsAsync(
        IReadOnlyCollection<LedgerAccountType> accountTypes,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);

    /// <summary>
    /// Cari (AccountType=Cari) defter satırları, cari adı çözümlenmiş (Customers join, bellek-içi
    /// DisplayName). <paramref name="asOf"/> verilirse o tarihe kadar. Bakiye + yaşlandırma için.
    /// </summary>
    Task<IReadOnlyList<CariLedgerRowDto>> GetCariLedgerRowsAsync(
        DateTimeOffset? asOf, CancellationToken ct = default);

    /// <summary>Tüm araçların durumları (filo dağılımı için).</summary>
    Task<IReadOnlyList<VehicleStatus>> GetVehicleStatusesAsync(CancellationToken ct = default);

    /// <summary>Aktif (Kirada) kira sözleşmesi sayısı.</summary>
    Task<int> GetActiveRentalCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Tamamlanmış servis kayıtları (CikisTarihi aralığında), araç plakası çözümlenmiş.
    /// Servis maliyet özeti için ham satırlar.
    /// </summary>
    Task<IReadOnlyList<ServiceCostRowDto>> GetServiceCostRowsAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct = default);
}
