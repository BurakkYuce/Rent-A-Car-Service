using RentACar.Domain.Entities;

namespace RentACar.Application.Baflar;

/// <summary>BAF (personel araç tahsis) kalıcılığı (roadmap L5). CreateAsync boşluksuz No (BAF-000001) tahsis eder.</summary>
public interface IBafRepository
{
    Task<IReadOnlyList<Baf>> ListAsync(Authorization.BranchScope.BranchFilter scope, CancellationToken ct = default);

    /// <summary>FAZ-18 — filtreli liste. Şube kapsamı filtreden BAĞIMSIZ olarak ayrıca uygulanır.</summary>
    Task<IReadOnlyList<Baf>> SearchAsync(Authorization.BranchScope.BranchFilter scope, BafFilter filter, CancellationToken ct = default);

    Task<Baf?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(Baf row, CancellationToken ct = default);
    Task<bool> ReceiveAsync(Guid id, int returnKm, int? returnFuel, DateTimeOffset returnDate,
        string? returnBranch, TimeOnly? returnHour, CancellationToken ct = default);
    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);

    /// <summary>F6.1b — satır kilidi (FOR UPDATE) altında güncelleme; durum çitleri <paramref name="apply"/> içinde.</summary>
    Task<bool> UpdateLockedAsync(Guid id, Action<Baf> apply, CancellationToken ct = default);
}
