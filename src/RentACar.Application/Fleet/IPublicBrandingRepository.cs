namespace RentACar.Application.Fleet;

/// <summary>PR-4: halka açık site marka/iletişim bilgisi — <see cref="FleetShowcaseService"/> için.
/// Guard'sız (TenantSettingsService.GetAsync'in ManageUsers kilidini bilinçli atlar — bu alanlar zaten
/// fatura başlığında müşteri-yüzü bilgiler).</summary>
public interface IPublicBrandingRepository
{
    Task<FleetBranding> GetAsync(Guid tenantId, CancellationToken ct = default);
}
