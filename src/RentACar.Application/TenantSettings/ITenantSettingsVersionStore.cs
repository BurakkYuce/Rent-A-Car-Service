namespace RentACar.Application.TenantSettings;

/// <summary>
/// F11.1b — firma ayarlarının iyimser eşzamanlılığı (tam değiştirme PUT'u). <see cref="ITenantSettingsRepository"/>'ye
/// üye eklemek yerine ayrı sözleşme: o arayüzün iş/job yolu uygulamaları (DogrudanAyarRepository) sürüm bilmez ve
/// bilmesi gerekmez.
/// </summary>
public interface ITenantSettingsVersionStore
{
    /// <summary>Ayar satırının sürümü (Postgres xmin, opak metin); satır yoksa <c>null</c>.</summary>
    Task<string?> VersionAsync(CancellationToken ct = default);

    /// <summary>
    /// Satır kilidi + sürüm karşılaştırması + upsert TEK işlemde. Satır varsa <paramref name="expectedVersion"/> onun
    /// sürümüne eşit olmalı; satır yoksa <c>null</c> olmalı. Aksi halde
    /// <see cref="Common.EszamanliDegisiklikException"/> ve hiçbir şey yazılmaz.
    /// </summary>
    Task UpsertAsync(Action<RentACar.Domain.Entities.TenantSettings> apply, string? expectedVersion, CancellationToken ct = default);
}
