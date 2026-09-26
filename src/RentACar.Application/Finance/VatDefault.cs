using RentACar.Application.TenantSettings;

namespace RentACar.Application.Finance;

/// <summary>
/// Tenant varsayılan KDV oranı çözücüsü (FAZ 3.A6): TenantSettings.VarsayilanKdvOrani ??
/// KdvMath.VarsayilanOran (0.20). Alan HASSAS DEĞİLDİR — admin-gate'li TenantSettingsService yerine
/// repo'dan okunur (fiyatlama/fatura yolları operatör/muhasebe yetkisiyle çalışır; ayar okuma
/// yetki gerektirmez). AYAR YOKKEN davranış bugünkü paketle BAYT-ÖZDEŞ (0.20 sabitine düşer).
/// </summary>
public sealed class VatDefault(ITenantSettingsRepository settings)
{
    public async Task<decimal> RateAsync(CancellationToken ct = default)
    {
        var rate = (await settings.GetAsync(ct))?.VarsayilanKdvOrani;
        // Bozuk/aralık-dışı kayıt (eski veri) fiyatı saptırmasın — sessizce 0.20'ye değil,
        // yalnız 0..1 aralığındaki değere güvenilir.
        return rate is >= 0m and <= 1m ? rate.Value : VatMath.DefaultRate;
    }
}
