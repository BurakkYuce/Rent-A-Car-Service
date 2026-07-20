using RentACar.Domain.Entities;

namespace RentACar.Application.Authorization;

/// <summary>Bir yetki-grubu (şablon) kalemi: bir ekran + o ekrana izinli roller. JSON serileştirilir.</summary>
public sealed record YetkiGrupKalem(string Ekran, string[] Roller);

public interface IYetkiGrupRepository
{
    Task<IReadOnlyList<YetkiGrup>> ListAsync(CancellationToken ct = default);
    Task<YetkiGrup?> FindByAdAsync(string ad, CancellationToken ct = default);
    Task UpsertAsync(string ad, Action<YetkiGrup> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(string ad, CancellationToken ct = default);
}
