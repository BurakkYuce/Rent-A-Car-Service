using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

public interface IPersonnelRepository
{
    Task<IReadOnlyList<Personel>> ListAsync(CancellationToken ct = default);
    Task<Personel?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Personel row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Personel> apply, CancellationToken ct = default);
    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması; uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<Personel> apply, CancellationToken ct = default);
    /// <summary>F11.1b — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
