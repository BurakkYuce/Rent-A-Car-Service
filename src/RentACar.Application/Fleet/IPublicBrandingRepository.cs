namespace RentACar.Application.Fleet;

/// <summary>PR-4: halka açık site marka/iletişim bilgisi — <see cref="FleetShowcaseService"/> için.
/// Guard'sız (TenantSettingsService.GetAsync'in ManageUsers kilidini bilinçli atlar — bu alanlar zaten
/// fatura başlığında müşteri-yüzü bilgiler).</summary>
public interface IPublicBrandingRepository
{
    Task<FleetBranding> GetAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// PR-9: tenant'ın KANONİK host'u — SEO'nun TEK doğruluk kaynağı. Kural DETERMİNİSTİK olmalı çünkü
    /// PR-5'in Pending sınırı (tenant başına 2) zamanla BİRDEN FAZLA Active özel domain bırakabilir:
    /// **en eski `VerifiedAtUtc`'ye sahip Active Custom** varsa O, yoksa Active subdomain.
    ///
    /// `canonical` link etiketi, `sitemap.xml` ve `robots.txt` HEPSİ buradan okur — `req.Host`'tan DEĞİL:
    /// ziyaretçi subdomain'den gelse bile tenant'ın canonical'i özel domain ise sitemap subdomain'i
    /// gösterirse canonical etiketiyle ÇELİŞİR (kendi kendini çürüten SEO sinyali).
    /// </summary>
    Task<string?> GetCanonicalHostAsync(Guid tenantId, CancellationToken ct = default);
}
