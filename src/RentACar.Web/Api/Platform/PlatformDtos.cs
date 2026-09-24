namespace RentACar.Web.Api.Platform;

// F12.1 — /api/ui/v1/platform/* request/response shapes. JSON field names are Turkish (API contract);
// type names are English. Only tenant META data travels here: no customer PII, no ledger rows, no passwords.

/// <summary>Platform operator login body.</summary>
public sealed record PlatformLoginRequest(string? Kullanici, string? Sifre);

/// <summary>Current platform session.</summary>
public sealed record PlatformSessionResponse(string Kullanici);

/// <summary>Console summary strip (whole platform).</summary>
public sealed record PlatformSummary(
    int ToplamKiraci, int Aktif, int Pasif, int Kapali,
    int ToplamKullanici, int ToplamArac, int AktifKira, int Son30GunYeniKiraci);

/// <summary>Tenant list row. <c>Durum</c>: <c>Aktif</c> | <c>Pasif</c> | <c>Kapali</c> (closed stamp wins over IsActive).</summary>
public sealed record PlatformTenantRowDto(
    Guid Id, string Kod, string Ad, string Durum, DateTimeOffset? KapanisTarihi, DateTimeOffset Olusturma,
    int KullaniciSayisi, int AracSayisi, int AktifKira, DateTimeOffset? SonGiris);

/// <summary>Tenant picker option (document targets).</summary>
public sealed record PlatformTenantOptionDto(Guid Id, string Kod, string Ad, string Durum);

/// <summary>Public-site host row (read-only).</summary>
public sealed record PlatformDomainDto(string Host, string Tur, string Durum);

/// <summary>PDF logo state. <c>Uyari</c> is shown but does not block; <c>BaskiyaUygun=false</c> means PDFs ignore it.</summary>
public sealed record PlatformLogoDto(bool Var, int? Bayt, int? Genislik, int? Yukseklik, bool BaskiyaUygun, string? Uyari);

/// <summary>
/// Tenant detail (Blazor <c>TenantDetail</c> parity). <c>Surum</c> is the optimistic-concurrency token for
/// <c>PUT</c> (string: tick values exceed JS safe integers). <c>Gelir30Gun</c> is a single aggregate (ledger
/// revenue, base currency) — no ledger rows are exposed.
/// </summary>
public sealed record PlatformTenantDetailDto(
    Guid Id, string Kod, string Ad, string Durum, DateTimeOffset? KapanisTarihi, DateTimeOffset Olusturma,
    DateTimeOffset? Guncelleme, string Surum,
    string? YetkiliAd, string? Eposta, string? Telefon, string? Notlar, string? Plan,
    int KullaniciSayisi, int AracSayisi, int AktifKira, int ToplamKira, DateTimeOffset? SonGiris, decimal Gelir30Gun,
    bool WebSitesiModulu, bool YeniArayuzPilot, bool HalkaAcikSite, IReadOnlyList<PlatformDomainDto> Domainler,
    PlatformLogoDto Logo);

/// <summary>New tenant + first Admin user. The password is hashed and never echoed or audited.</summary>
public sealed record PlatformTenantCreateRequest(string? Kod, string? Ad, string? AdminKullanici, string? AdminSifre);

/// <summary>Full replace of the tenant info fields (code is immutable — it is the login key).</summary>
public sealed record PlatformTenantUpdateRequest(
    string? Ad, string? YetkiliAd, string? Eposta, string? Telefon, string? Notlar, string? Plan, string? Surum);

/// <summary>Status change. <c>Kapali</c> requires <c>OnayKod</c> equal to the tenant code (typed confirmation).</summary>
public sealed record PlatformTenantStatusRequest(string? Durum, string? OnayKod);

/// <summary>On/off switch body (pilot, web-site module).</summary>
public sealed record PlatformSwitchRequest(bool? Aktif);

/// <summary>Document Center row — PDF bytes are NOT part of the list. <c>Durum</c>: <c>Taslak</c> | <c>Yayinda</c> | <c>Arsiv</c>.
/// F12.2: renamed from <c>PlatformDocumentDto</c> — the tenant-side <c>Api.Tanim.PlatformDocumentDto</c> (F11.1a) has the
/// same simple name, and OpenAPI schema ids are simple names: the console list was published with the TENANT shape
/// (no <c>durum</c>, <c>hedefKodlar</c>…), so the generated SPA types were wrong.</summary>
public sealed record PlatformConsoleDocumentDto(
    Guid Id, string Baslik, string? Aciklama, string DosyaAdi, long Boyut, int Surum, string Durum,
    bool YalnizYoneticiler, DateTimeOffset Guncelleme, string? YukleyenOperator, IReadOnlyList<string> HedefKodlar);

/// <summary>Document upload (multipart/form-data). <c>Hedef</c> is repeatable; none = all tenants.
/// <c>YalnizYoneticiler</c> is text (<c>true</c>/<c>false</c>) so an empty field is not a binding 400.</summary>
public sealed class PlatformDocumentUploadForm
{
    public string? Baslik { get; set; }
    public string? Aciklama { get; set; }
    public IFormFile? Dosya { get; set; }
    public string? YalnizYoneticiler { get; set; }
    public List<string>? Hedef { get; set; }
}

/// <summary>Document status change body.</summary>
public sealed record PlatformDocumentStatusRequest(string? Durum);
