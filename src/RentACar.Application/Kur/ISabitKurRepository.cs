using RentACar.Domain.Entities;

namespace RentACar.Application.Kur;

/// <summary>Tenant kur sabitleme CRUD (tenant-owned/RLS; izolasyon alt katmanda otomatik).</summary>
public interface ISabitKurRepository
{
    Task<IReadOnlyList<SabitKur>> ListAsync(CancellationToken ct = default);
    Task<SabitKur?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>Bu kod için AKTİF + tarih penceresinde geçerli sabit kur (yoksa null). Çözümleme kullanır.</summary>
    Task<SabitKur?> GetActiveAsync(string kod, DateTimeOffset tarih, CancellationToken ct = default);

    Task<bool> KodExistsAsync(string kod, Guid? excludeId, CancellationToken ct = default);
    Task CreateAsync(SabitKur sabit, CancellationToken ct = default);
    Task<bool> UpdateAsync(SabitKur sabit, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
