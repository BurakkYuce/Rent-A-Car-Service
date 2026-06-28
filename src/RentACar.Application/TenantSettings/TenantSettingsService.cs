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
    ITenantSettingsRepository repository, ICurrentUser currentUser, ISecretProtector secrets)
{
    public async Task<TenantSettingsModel> GetAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
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
            PosApiKey = secrets.Unprotect(s.PosApiKeyEnc)
        };
    }

    public async Task SaveAsync(TenantSettingsModel m, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
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
        }, ct);
    }

    private string? Secret(string? yeni, string? mevcutCipher)
        => string.IsNullOrWhiteSpace(yeni) ? mevcutCipher : secrets.Protect(yeni.Trim());

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
