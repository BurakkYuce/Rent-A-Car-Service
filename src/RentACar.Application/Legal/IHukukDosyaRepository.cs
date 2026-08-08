using RentACar.Domain.Entities;

namespace RentACar.Application.Legal;

public interface IHukukDosyaRepository
{
    Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default);

    /// <summary>FAZ-41 — filtreli liste; müşteri adı/telefonu ÇÖZÜLMÜŞ döner. <c>null</c> filtre → tümü.</summary>
    Task<IReadOnlyList<HukukDosyaSatirDto>> SearchAsync(HukukDosyaFilter? filter = null, CancellationToken ct = default);

    Task<HukukDosya?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> DosyaNoExistsAsync(string dosyaNo, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(HukukDosya row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<HukukDosya> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
