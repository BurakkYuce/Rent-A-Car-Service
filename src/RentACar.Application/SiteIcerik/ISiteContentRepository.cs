using RentACar.Domain.Entities;

namespace RentACar.Application.SiteIcerik;

/// <summary>Yönetim listesi satırı — gövde TAŞINMAZ (liste yalnız üstveri gösteriyor).</summary>
public sealed record SayfaOzet(Guid Id, string Slug, string Baslik, int Sira, bool Yayinda);

/// <summary>Halka açık sayfa gösterimi.</summary>
public sealed record SayfaGoster(string Slug, string Baslik, string Govde, string? MetaAciklama);

/// <summary>SSS satırı — yönetimde ve sitede aynı şekil.</summary>
public sealed record SssSatiri(Guid Id, string Soru, string Cevap, int Sira, bool Yayinda);

public interface ISiteContentRepository
{
    // ---- Sayfalar ----

    /// <summary>Yönetim listesi: yayında olmayanlar DA gelir. Sıra, sonra başlık.</summary>
    Task<IReadOnlyList<SayfaOzet>> ListAsync(CancellationToken ct = default);

    /// <summary>Sitede gösterilecekler (footer + sitemap). Yalnız <c>Yayinda</c>.</summary>
    Task<IReadOnlyList<SayfaOzet>> PublishedAsync(CancellationToken ct = default);

    /// <summary>Yayındaki sayfanın tam içeriği; yoksa/yayında değilse <c>null</c> (uç 404 döner).</summary>
    Task<SayfaGoster?> FindAsync(string slug, CancellationToken ct = default);

    /// <summary>Düzenleme formu için — yayında olmasa da gelir.</summary>
    Task<SayfaIcerik?> FetchAsync(Guid id, CancellationToken ct = default);

    /// <summary>Aynı slug BAŞKA bir sayfada var mı (yeniden adlandırmada kendini saymaz).</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? excludedId, CancellationToken ct = default);

    Task AddAsync(SayfaIcerik page, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<SayfaIcerik> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    // ---- SSS ----

    Task<IReadOnlyList<SssSatiri>> ListFaqAsync(bool publishedOnly, CancellationToken ct = default);
    Task AddFaqAsync(SssKaydi record, CancellationToken ct = default);
    Task<bool> UpdateFaqAsync(Guid id, Action<SssKaydi> apply, CancellationToken ct = default);
    Task<bool> DeleteFaqAsync(Guid id, CancellationToken ct = default);

    // ---- F11.1b: iyimser eşzamanlılık (satır kilidi + sürüm; uyuşmazlık EszamanliDegisiklikException) ----

    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<SayfaIcerik> apply, CancellationToken ct = default);
    Task<bool> UpdateFaqAsync(Guid id, string? expectedVersion, Action<SssKaydi> apply, CancellationToken ct = default);
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);
    Task<string?> FaqVersionAsync(Guid id, CancellationToken ct = default);
}
