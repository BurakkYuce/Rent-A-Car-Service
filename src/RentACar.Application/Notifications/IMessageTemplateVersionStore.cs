namespace RentACar.Application.Notifications;

/// <summary>
/// F11.1b — mesaj şablonu tam değiştirme PUT'unun iyimser eşzamanlılığı. <see cref="IMessageRepository"/>'ye üye
/// eklemek yerine ayrı sözleşme: job yolu uygulaması (DogrudanMesajRepository) sürüm bilmez.
/// </summary>
public interface IMessageTemplateVersionStore
{
    /// <summary>Şablon kimliği → sürüm (Postgres xmin, opak metin).</summary>
    Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(CancellationToken ct = default);

    /// <summary>
    /// (tür, kanal) satırını kilit + sürüm karşılaştırmasıyla upsert eder. Satır varsa <paramref name="expectedVersion"/>
    /// onun sürümüne eşit olmalı; yoksa <c>null</c> olmalı. Aksi halde <see cref="Common.ConcurrentModificationException"/>.
    /// </summary>
    Task UpsertAsync(MesajSablonInput input, string? expectedVersion, CancellationToken ct = default);
}
