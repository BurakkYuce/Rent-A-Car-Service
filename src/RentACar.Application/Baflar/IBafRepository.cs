using RentACar.Domain.Entities;

namespace RentACar.Application.Baflar;

/// <summary>BAF (personel araç tahsis) kalıcılığı (roadmap L5). CreateAsync boşluksuz No (BAF-000001) tahsis eder.</summary>
public interface IBafRepository
{
    Task<IReadOnlyList<Baf>> ListAsync(Authorization.BranchScope.BranchFilter kapsam, CancellationToken ct = default);

    /// <summary>FAZ-18 — filtreli liste. Şube kapsamı filtreden BAĞIMSIZ olarak ayrıca uygulanır.</summary>
    Task<IReadOnlyList<Baf>> SearchAsync(Authorization.BranchScope.BranchFilter kapsam, BafFilter filtre, CancellationToken ct = default);

    Task<Baf?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(Baf row, CancellationToken ct = default);
    Task<bool> TeslimAlAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset donusTarihi,
        string? donusSube, TimeOnly? donusSaat, CancellationToken ct = default);
    Task<bool> IptalAsync(Guid id, CancellationToken ct = default);
}
