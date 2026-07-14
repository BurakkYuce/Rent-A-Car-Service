using RentACar.Application.TenantSettings;

namespace RentACar.Application.Finance;

/// <summary>
/// Tenant varsayılan KDV oranı çözücüsü (FAZ 3.A6): TenantSettings.VarsayilanKdvOrani ??
/// KdvMath.VarsayilanOran (0.20). Alan HASSAS DEĞİLDİR — admin-gate'li TenantSettingsService yerine
/// repo'dan okunur (fiyatlama/fatura yolları operatör/muhasebe yetkisiyle çalışır; ayar okuma
/// yetki gerektirmez). AYAR YOKKEN davranış bugünkü paketle BAYT-ÖZDEŞ (0.20 sabitine düşer).
/// </summary>
public sealed class KdvVarsayilan(ITenantSettingsRepository ayarlar)
{
    public async Task<decimal> OranAsync(CancellationToken ct = default)
    {
        var oran = (await ayarlar.GetAsync(ct))?.VarsayilanKdvOrani;
        // Bozuk/aralık-dışı kayıt (eski veri) fiyatı saptırmasın — sessizce 0.20'ye değil,
        // yalnız 0..1 aralığındaki değere güvenilir.
        return oran is >= 0m and <= 1m ? oran.Value : KdvMath.VarsayilanOran;
    }
}
