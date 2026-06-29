using RentACar.Domain.Entities;

namespace RentACar.Application.Baflar;

/// <summary>BAF (personel araç tahsis) kalıcılığı (roadmap L5). CreateAsync boşluksuz No (BAF-000001) tahsis eder.</summary>
public interface IBafRepository
{
    Task<IReadOnlyList<Baf>> ListAsync(CancellationToken ct = default);
    Task<Baf?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(Baf row, CancellationToken ct = default);
    Task<bool> TeslimAlAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset donusTarihi, CancellationToken ct = default);
    Task<bool> IptalAsync(Guid id, CancellationToken ct = default);
}
