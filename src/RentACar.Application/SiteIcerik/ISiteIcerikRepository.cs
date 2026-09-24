using RentACar.Domain.Entities;

namespace RentACar.Application.SiteIcerik;

/// <summary>Yönetim listesi satırı — gövde TAŞINMAZ (liste yalnız üstveri gösteriyor).</summary>
public sealed record SayfaOzet(Guid Id, string Slug, string Baslik, int Sira, bool Yayinda);

/// <summary>Halka açık sayfa gösterimi.</summary>
public sealed record SayfaGoster(string Slug, string Baslik, string Govde, string? MetaAciklama);

/// <summary>SSS satırı — yönetimde ve sitede aynı şekil.</summary>
public sealed record SssSatiri(Guid Id, string Soru, string Cevap, int Sira, bool Yayinda);

public interface ISiteIcerikRepository
{
    // ---- Sayfalar ----

    /// <summary>Yönetim listesi: yayında olmayanlar DA gelir. Sıra, sonra başlık.</summary>
    Task<IReadOnlyList<SayfaOzet>> ListeleAsync(CancellationToken ct = default);

    /// <summary>Sitede gösterilecekler (footer + sitemap). Yalnız <c>Yayinda</c>.</summary>
    Task<IReadOnlyList<SayfaOzet>> YayindakilerAsync(CancellationToken ct = default);

    /// <summary>Yayındaki sayfanın tam içeriği; yoksa/yayında değilse <c>null</c> (uç 404 döner).</summary>
    Task<SayfaGoster?> BulAsync(string slug, CancellationToken ct = default);

    /// <summary>Düzenleme formu için — yayında olmasa da gelir.</summary>
    Task<SayfaIcerik?> GetirAsync(Guid id, CancellationToken ct = default);

    /// <summary>Aynı slug BAŞKA bir sayfada var mı (yeniden adlandırmada kendini saymaz).</summary>
    Task<bool> SlugVarMiAsync(string slug, Guid? haricId, CancellationToken ct = default);

    Task EkleAsync(SayfaIcerik sayfa, CancellationToken ct = default);
    Task<bool> GuncelleAsync(Guid id, Action<SayfaIcerik> apply, CancellationToken ct = default);
    Task<bool> SilAsync(Guid id, CancellationToken ct = default);

    // ---- SSS ----

    Task<IReadOnlyList<SssSatiri>> SssListeAsync(bool yalnizYayinda, CancellationToken ct = default);
    Task SssEkleAsync(SssKaydi kayit, CancellationToken ct = default);
    Task<bool> SssGuncelleAsync(Guid id, Action<SssKaydi> apply, CancellationToken ct = default);
    Task<bool> SssSilAsync(Guid id, CancellationToken ct = default);

    // ---- F11.1b: iyimser eşzamanlılık (satır kilidi + sürüm; uyuşmazlık EszamanliDegisiklikException) ----

    Task<bool> GuncelleAsync(Guid id, string? expectedVersion, Action<SayfaIcerik> apply, CancellationToken ct = default);
    Task<bool> SssGuncelleAsync(Guid id, string? expectedVersion, Action<SssKaydi> apply, CancellationToken ct = default);
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);
    Task<string?> FaqVersionAsync(Guid id, CancellationToken ct = default);
}
