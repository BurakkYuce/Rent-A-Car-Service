using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracSiparisleri;

/// <summary>Araç sipariş kalıcılığı (roadmap L3). CreateAsync boşluksuz No (SP-000001) tahsis eder.</summary>
public interface IAracSiparisRepository
{
    Task<IReadOnlyList<AracSiparis>> ListAsync(CancellationToken ct = default);
    Task<AracSiparis?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AracSiparis row, CancellationToken ct = default);
    Task<bool> SetDurumAsync(Guid id, SiparisDurum durum, CancellationToken ct = default);
}
