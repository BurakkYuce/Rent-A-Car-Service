using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracKredileri;

/// <summary>Araç kredisi kalıcılığı (roadmap L4). CreateAsync boşluksuz No (KR-000001) tahsis eder.</summary>
public interface IAracKrediRepository
{
    Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default);
    Task<AracKredi?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AracKredi row, CancellationToken ct = default);
    Task<bool> TaksitOdeAsync(Guid id, CancellationToken ct = default);
    Task<bool> SetDurumAsync(Guid id, KrediDurum durum, CancellationToken ct = default);
}
