using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracSiparisleri;

/// <summary>Araç sipariş kalıcılığı (roadmap L3). CreateAsync boşluksuz No (SP-000001) tahsis eder.</summary>
public interface IAracSiparisRepository
{
    Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default);

    /// <summary>FAZ-17 — filtreli liste (Cari/Ad-Soyad/Araç/Dosya/Durum/Tarih aralığı).</summary>
    Task<IReadOnlyList<AracSiparis>> SearchAsync(AracSiparisFilter filtre, CancellationToken ct = default);

    Task<AracSiparis?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AracSiparis row, CancellationToken ct = default);

    /// <summary>FAZ-17 — alan güncelleme. <c>No</c> ve <c>Durum</c> BURADAN değişmez (durum
    /// geçişleri <see cref="SetDurumAsync"/> ile yapılır).</summary>
    Task<bool> UpdateAsync(Guid id, Action<AracSiparis> apply, CancellationToken ct = default);

    Task<bool> SetDurumAsync(Guid id, SiparisDurum durum, CancellationToken ct = default);
}
