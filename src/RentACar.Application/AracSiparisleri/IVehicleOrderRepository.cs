using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracSiparisleri;

/// <summary>Araç sipariş kalıcılığı (roadmap L3). CreateAsync boşluksuz No (SP-000001) tahsis eder.</summary>
public interface IVehicleOrderRepository
{
    Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default);

    /// <summary>FAZ-17 — filtreli liste (Cari/Ad-Soyad/Araç/Dosya/Durum/Tarih aralığı).</summary>
    Task<IReadOnlyList<AracSiparis>> SearchAsync(AracSiparisFilter filter, CancellationToken ct = default);

    Task<AracSiparis?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AracSiparis row, CancellationToken ct = default);

    /// <summary>FAZ-17 — alan güncelleme. <c>No</c> ve <c>Durum</c> BURADAN değişmez (durum
    /// geçişleri <see cref="SetStatusAsync"/> ile yapılır).</summary>
    Task<bool> UpdateAsync(Guid id, Action<AracSiparis> apply, CancellationToken ct = default);

    Task<bool> SetStatusAsync(Guid id, OrderStatus status, CancellationToken ct = default);

    /// <summary>F6.1b — satır kilidi (FOR UPDATE) + <paramref name="expectedVersion"/> doluysa xmin karşılaştırması
    /// (uyuşmazlık 409 cakisma) altında güncelleme; durum çitleri <paramref name="apply"/> içinde kilit altında.</summary>
    Task<bool> UpdateLockedAsync(Guid id, string? expectedVersion, Action<AracSiparis> apply, CancellationToken ct = default);

    /// <summary>F6.1b — satır sürümü (xmin); yoksa null.</summary>
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);
}
