using RentACar.Domain.Entities;

namespace RentACar.Application.Periods;

public interface IDonemKilidiRepository
{
    /// <summary>Tenant'ın tek kilit satırı (yoksa null).</summary>
    Task<DonemKilidi?> GetAsync(CancellationToken ct = default);

    /// <summary>Tek satırı upsert eder (yoksa oluştur, varsa güncelle).</summary>
    Task UpsertAsync(Action<DonemKilidi> apply, CancellationToken ct = default);
}
