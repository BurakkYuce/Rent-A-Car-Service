using RentACar.Application.Authorization;
using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

/// <summary>Vardiya kalıcılığı (FAZ-45).</summary>
public interface IPersonelVardiyaRepository
{
    /// <summary>Tarih aralığı + rol kapsamı + kullanıcı filtresiyle vardiyalar (personel adıyla).</summary>
    Task<IReadOnlyList<VardiyaSatir>> SearchAsync(
        DateOnly bas, DateOnly bit, VardiyaFilter filtre,
        BranchScope.BranchFilter kapsam, CancellationToken ct = default);

    /// <summary>Çakışma kontrolü için: personelin [bas-1, bit+1] penceresindeki TÜM vardiyaları.
    /// Gece vardiyası gün sınırını aştığından komşu günler de gerekir.</summary>
    Task<IReadOnlyList<PersonelVardiya>> ListForOverlapAsync(
        Guid personelId, DateOnly gun, Guid? excludeId, CancellationToken ct = default);

    Task<PersonelVardiya?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(PersonelVardiya row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<PersonelVardiya> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}
