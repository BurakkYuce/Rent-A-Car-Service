using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;

namespace RentACar.Application.TenantSettings;

/// <summary>
/// Tenant ayarları iş mantığı (roadmap D1): firma + entegrasyon kimlikleri. Hassas alanlar
/// <see cref="ISecretProtector"/> ile at-rest ŞİFRELENİR (yaz) / çözülür (oku). Ayarlar hassas
/// (entegrasyon sırları) → yalnız <see cref="Permission.ManageUsers"/> (admin) okur/yazar.
/// Sır alanı yazmada BOŞ ise mevcut korunur (her kayıtta sır yeniden girilmesin).
/// </summary>
public sealed class TenantSettingsService(
    ITenantSettingsRepository repository, ICurrentUser currentUser, ISecretProtector secrets, ScreenPermissionService screens,
    ITenantDomainRepository domains, ITenantContext tenant)
{
    public async Task<TenantSettingsModel> GetAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        var s = await repository.GetAsync(ct);
        if (s is null) return new TenantSettingsModel();
        return new TenantSettingsModel
        {
            FirmaUnvan = s.FirmaUnvan,
            FirmaVergiDairesi = s.FirmaVergiDairesi,
            FirmaVergiNo = s.FirmaVergiNo,
            FirmaAdres = s.FirmaAdres,
            FirmaTel = s.FirmaTel,
            FirmaEmail = s.FirmaEmail,
            FirmaMobilTel = s.FirmaMobilTel,
            FirmaMarka = s.FirmaMarka,
            EFaturaKullanici = s.EFaturaKullanici,
            EFaturaSifre = secrets.Unprotect(s.EFaturaSifreEnc),
            SmsBaslik = s.SmsBaslik,
            SmsApiKey = secrets.Unprotect(s.SmsApiKeyEnc),
            PosMerchantId = s.PosMerchantId,
            PosApiKey = secrets.Unprotect(s.PosApiKeyEnc),
            // roadmap M1
            LogoUrl = s.LogoUrl,
            LogoBytes = s.LogoBytes, // PR-C

            VarsayilanDoviz = s.VarsayilanDoviz,
            VarsayilanKdvOrani = s.VarsayilanKdvOrani,
            VarsayilanGrupId = s.VarsayilanGrupId, // PR-10
            DonemselFaturalamaJob = s.DonemselFaturalamaJob,
            DonemselOtomatikTahsilat = s.DonemselOtomatikTahsilat,
            MinKiraGun = s.MinKiraGun,
            MaxKiraGun = s.MaxKiraGun,
            RezOnayZorunlu = s.RezOnayZorunlu,
            SmtpHost = s.SmtpHost,
            SmtpPort = s.SmtpPort,
            SmtpKullanici = s.SmtpKullanici,
            SmtpSifre = secrets.Unprotect(s.SmtpSifreEnc),
            SmtpSsl = s.SmtpSsl,
            WhatsAppNumarasi = s.WhatsAppNumarasi,
            WhatsAppGunlukOzet = s.WhatsAppGunlukOzet,
            // PR-2: public-site
            PublicSiteEnabled = s.PublicSiteEnabled,
            PublicSiteHost = await domains.GetActiveHostAsync(TenantId, ct),
            // PR-5: tüm domain kayıtları (durum rozeti için)
            CustomDomains = (await domains.ListAsync(TenantId, ct))
                .Select(d => new TenantDomainRow(d.Host, d.Kind.ToString(), DurumMetni(d.Status)))
                .ToList()
        };
    }

    /// <summary>PR-2: "Sitemi Aç" — subdomain host'unu (idempotent) oluşturur + siteyi aktifleştirir.
    /// Çağıranın kendi çerez-scoped ITenantContext'i altında çalışır (owner-bypass YOK).</summary>
    public async Task OpenPublicSiteAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        await domains.EnsureSubdomainAsync(TenantId, ct);
        await repository.UpsertAsync(s => s.PublicSiteEnabled = true, ct);
    }

    /// <summary>PR-5: özel domain ekler — `PendingVerification` ile başlar, Caddy `on_demand_tls`'in
    /// ilk gerçek isteği başarıyla çözdüğü an kendi kendini `Active`'e doğrular (bkz. PublicTenantResolver).</summary>
    public async Task AddCustomDomainAsync(string host, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        if (string.IsNullOrWhiteSpace(host))
            throw new ValidationException("Alan adı zorunludur.");
        await domains.AddCustomAsync(TenantId, host, ct);
    }

    private static string DurumMetni(RentACar.Domain.Entities.TenantDomainStatus status) => status switch
    {
        RentACar.Domain.Entities.TenantDomainStatus.Active => "Aktif",
        RentACar.Domain.Entities.TenantDomainStatus.PendingVerification => "Doğrulama Bekliyor",
        RentACar.Domain.Entities.TenantDomainStatus.Failed => "Başarısız",
        _ => status.ToString()
    };

    private Guid TenantId => tenant.TenantId
        ?? throw new ValidationException("Tenant bağlamı yok — işlem yapılamaz.");

    public async Task SaveAsync(TenantSettingsModel m, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        await repository.UpsertAsync(s =>
        {
            s.FirmaUnvan = Trim(m.FirmaUnvan);
            s.FirmaVergiDairesi = Trim(m.FirmaVergiDairesi);
            s.FirmaVergiNo = Trim(m.FirmaVergiNo);
            s.FirmaAdres = Trim(m.FirmaAdres);
            s.FirmaTel = Trim(m.FirmaTel);
            s.FirmaEmail = Trim(m.FirmaEmail);
            s.FirmaMobilTel = Trim(m.FirmaMobilTel);
            s.FirmaMarka = Trim(m.FirmaMarka);
            s.EFaturaKullanici = Trim(m.EFaturaKullanici);
            s.SmsBaslik = Trim(m.SmsBaslik);
            s.PosMerchantId = Trim(m.PosMerchantId);
            // Sır: dolu ise şifrele+güncelle; boş ise mevcut cipher KORUNUR.
            s.EFaturaSifreEnc = Secret(m.EFaturaSifre, s.EFaturaSifreEnc);
            s.SmsApiKeyEnc = Secret(m.SmsApiKey, s.SmsApiKeyEnc);
            s.PosApiKeyEnc = Secret(m.PosApiKey, s.PosApiKeyEnc);
            // roadmap M1 — görünüm/operasyon (düz) + SMTP (host/port/user düz, şifre Enc)
            s.LogoUrl = Trim(m.LogoUrl);
            s.VarsayilanDoviz = string.IsNullOrWhiteSpace(m.VarsayilanDoviz) ? null : m.VarsayilanDoviz.Trim().ToUpperInvariant();
            if (m.VarsayilanKdvOrani is < 0m or > 1m)
                throw new ValidationException("Varsayılan KDV oranı kesir olmalı (0.20 = %20); 0-1 arası."); // A6
            s.VarsayilanKdvOrani = m.VarsayilanKdvOrani;
            // PR-10: FK'si ON DELETE SET NULL; ayrıca çözücü grubu AKTİF olarak arar → pasifleşen
            // ya da başka tenant'a ait bir Id sessizce Ekonomi zincirine düşer, araç yanlış gruba girmez.
            s.VarsayilanGrupId = m.VarsayilanGrupId;
            s.DonemselFaturalamaJob = m.DonemselFaturalamaJob;
            s.DonemselOtomatikTahsilat = m.DonemselOtomatikTahsilat;
            s.MinKiraGun = m.MinKiraGun;
            s.MaxKiraGun = m.MaxKiraGun;
            s.RezOnayZorunlu = m.RezOnayZorunlu;
            s.SmtpHost = Trim(m.SmtpHost);
            s.SmtpPort = m.SmtpPort;
            s.SmtpKullanici = Trim(m.SmtpKullanici);
            s.SmtpSifreEnc = Secret(m.SmtpSifre, s.SmtpSifreEnc);
            s.SmtpSsl = m.SmtpSsl;
            s.WhatsAppNumarasi = Trim(m.WhatsAppNumarasi);
            s.WhatsAppGunlukOzet = m.WhatsAppGunlukOzet ?? false;
        }, ct);
    }

    /// <summary>PR-C: PDF firma logosunu ayarla/kaldır (Ayarlar upload). null → logoyu sil. En fazla 1 MB.</summary>
    public async Task SetLogoAsync(byte[]? bytes, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        await screens.EnsureScreenAccessAsync("ayarlar", Permission.ManageUsers, ct);
        if (bytes is { Length: > 1_048_576 })
            throw new ValidationException("Logo en fazla 1 MB olabilir.");
        await repository.UpsertAsync(s => s.LogoBytes = bytes is { Length: > 0 } ? bytes : null, ct);
    }

    public Task<IReadOnlyList<RentACar.Domain.Entities.WhatsAppGonderim>> ListWhatsAppGonderimAsync(int n = 7, CancellationToken ct = default)
        => repository.ListWhatsAppGonderimAsync(n, ct);

    private string? Secret(string? yeni, string? mevcutCipher)
        => string.IsNullOrWhiteSpace(yeni) ? mevcutCipher : secrets.Protect(yeni.Trim());

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
