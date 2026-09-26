using RentACar.Domain.Entities;

namespace RentACar.Application.Legal;

public interface ILegalCaseRepository
{
    Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default);

    /// <summary>FAZ-41 — filtreli liste; müşteri adı/telefonu ÇÖZÜLMÜŞ döner. <c>null</c> filtre → tümü.</summary>
    Task<IReadOnlyList<HukukDosyaSatirDto>> SearchAsync(HukukDosyaFilter? filter = null, CancellationToken ct = default);

    Task<HukukDosya?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> FileNoExistsAsync(string fileNo, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(HukukDosya row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<HukukDosya> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>F7.1 — satır kilidi + iyimser sürüm karşılaştırması (sürüm farklı → 409, hiçbir şey yazılmaz).</summary>
    Task<bool> UpdateAsync(Guid id, string expectedVersion, Action<HukukDosya> apply, CancellationToken ct = default);

    /// <summary>F7.1 — satır sürümü (opak); yok/başka kiracı → null.</summary>
    Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default);
}
