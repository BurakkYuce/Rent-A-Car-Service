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
    ITenantSettingsRepository repository, ICurrentUser currentUser, ISecretProtector secrets, ScreenPermissionService screens)
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
            EFaturaKullanici = s.EFaturaKullanici,
            EFaturaSifre = secrets.Unprotect(s.EFaturaSifreEnc),
            SmsBaslik = s.SmsBaslik,
            SmsApiKey = secrets.Unprotect(s.SmsApiKeyEnc),
            PosMerchantId = s.PosMerchantId,
            PosApiKey = secrets.Unprotect(s.PosApiKeyEnc),
            // roadmap M1
            LogoUrl = s.LogoUrl,
            VarsayilanDoviz = s.VarsayilanDoviz,
            VarsayilanKdvOrani = s.VarsayilanKdvOrani,
            MinKiraGun = s.MinKiraGun,
            MaxKiraGun = s.MaxKiraGun,
            RezOnayZorunlu = s.RezOnayZorunlu,
            SmtpHost = s.SmtpHost,
            SmtpPort = s.SmtpPort,
            SmtpKullanici = s.SmtpKullanici,
            SmtpSifre = secrets.Unprotect(s.SmtpSifreEnc),
            SmtpSsl = s.SmtpSsl
        };
    }

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
            s.VarsayilanKdvOrani = m.VarsayilanKdvOrani;
            s.MinKiraGun = m.MinKiraGun;
            s.MaxKiraGun = m.MaxKiraGun;
            s.RezOnayZorunlu = m.RezOnayZorunlu;
            s.SmtpHost = Trim(m.SmtpHost);
            s.SmtpPort = m.SmtpPort;
            s.SmtpKullanici = Trim(m.SmtpKullanici);
            s.SmtpSifreEnc = Secret(m.SmtpSifre, s.SmtpSifreEnc);
            s.SmtpSsl = m.SmtpSsl;
        }, ct);
    }

    private string? Secret(string? yeni, string? mevcutCipher)
        => string.IsNullOrWhiteSpace(yeni) ? mevcutCipher : secrets.Protect(yeni.Trim());

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
