using RentACar.Application.VehicleGroups;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;

namespace RentACar.Application.Fleet;

public sealed record FleetShowcaseCard(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, Guid? CoverPhotoId);

public sealed record FleetShowcaseDetail(
    Guid GroupId, string Ad, string? Aciklama, string? KasaTuru,
    int? KoltukSayisi, int? KapiSayisi, int? BagajSayisi, IReadOnlyList<Guid> PhotoIds);

public sealed record FleetBranding(string? Marka, string? Adres, string? Tel, string? Email);

/// <summary>
/// Public-site filo vitrini (PR-4). Yetki gerektirmez — mevcut guard-free okuma servisleri (
/// <see cref="VehicleGroupService.ListActiveAsync"/>, <see cref="VehicleService.ListAsync"/>,
/// <see cref="VehiclePhotoService"/>) üstünden salt-okur derleme (DashboardService deseni).
/// Vitrin GRUP bazlı (tekil araç/plaka değil — VehicleGroup.WebSira zaten bunun için tanımlanmış
/// ama hiç okunmuyordu); Vehicle.Grup FK değil string eşleşmesi (VehicleGroup.cs doc-yorumu).
/// </summary>
public sealed class FleetShowcaseService(
    VehicleGroupService groups, VehicleService vehicles, VehiclePhotoService photos,
    IPublicBrandingRepository branding, ITenantContext tenant)
{
    public async Task<IReadOnlyList<FleetShowcaseCard>> ListShowcaseGroupsAsync(CancellationToken ct = default)
    {
        var activeGroups = (await groups.ListActiveAsync(ct)).OrderBy(g => g.WebSira).ToList();
        var eligible = (await vehicles.ListAsync(ct)).Where(v => !v.WebRezKapat).ToLookup(v => v.Grup);

        var cards = new List<FleetShowcaseCard>();
        foreach (var g in activeGroups)
        {
            var candidate = eligible[g.Ad].FirstOrDefault();
            if (candidate is null) continue; // uygun aracı olmayan grup vitrine GİRMEZ
            var meta = await photos.ListMetaAsync(candidate.Id, ct); // zaten Sira sıralı
            cards.Add(new FleetShowcaseCard(g.Id, g.Ad, g.Aciklama, g.KasaTuru, g.KoltukSayisi, g.KapiSayisi, g.BagajSayisi,
                meta.Count > 0 ? meta[0].Id : null));
        }
        return cards;
    }

    public async Task<FleetShowcaseDetail?> GetGroupDetailAsync(Guid groupId, CancellationToken ct = default)
    {
        var group = (await groups.ListActiveAsync(ct)).FirstOrDefault(g => g.Id == groupId);
        if (group is null) return null;
        var candidates = (await vehicles.ListAsync(ct)).Where(v => !v.WebRezKapat && v.Grup == group.Ad).ToList();

        var photoIds = new List<Guid>();
        foreach (var v in candidates)
            photoIds.AddRange((await photos.ListMetaAsync(v.Id, ct)).Select(m => m.Id));
        return new FleetShowcaseDetail(group.Id, group.Ad, group.Aciklama, group.KasaTuru,
            group.KoltukSayisi, group.KapiSayisi, group.BagajSayisi, photoIds);
    }

    public Task<FleetBranding> GetBrandingAsync(CancellationToken ct = default)
        => branding.GetAsync(tenant.TenantIdOrThrow(), ct);
}
