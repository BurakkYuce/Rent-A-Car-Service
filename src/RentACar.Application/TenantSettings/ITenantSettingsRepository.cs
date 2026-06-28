using RentACar.Domain.Entities;

namespace RentACar.Application.TenantSettings;

public interface ITenantSettingsRepository
{
    /// <summary>Tenant'ın tek ayar satırı (yoksa null). Tenant izolasyonu query filter + RLS.</summary>
    Task<RentACar.Domain.Entities.TenantSettings?> GetAsync(CancellationToken ct = default);

    /// <summary>Tek satırı upsert eder: yoksa oluşturur, varsa günceller; apply mevcut/yeni varlığa uygulanır.</summary>
    Task UpsertAsync(Action<RentACar.Domain.Entities.TenantSettings> apply, CancellationToken ct = default);
}
