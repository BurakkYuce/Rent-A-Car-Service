using RentACar.Domain.Entities;

namespace RentACar.Application.Authorization;

/// <summary>Bir yetki-grubu (şablon) kalemi: bir ekran + o ekrana izinli roller. JSON serileştirilir.</summary>
public sealed record YetkiGrupKalem(string Ekran, string[] Roller);

public interface IPermissionGroupRepository
{
    Task<IReadOnlyList<YetkiGrup>> ListAsync(CancellationToken ct = default);
    Task<YetkiGrup?> FindByNameAsync(string name, CancellationToken ct = default);
    Task UpsertAsync(string name, Action<YetkiGrup> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(string name, CancellationToken ct = default);
}
