using Microsoft.EntityFrameworkCore;
using RentACar.Application.Brands;
using RentACar.Application.CancelReasons;
using RentACar.Application.Countries;
using RentACar.Application.CustomerGroups;
using RentACar.Application.Departments;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Tanim;

/// <summary>
/// F11.1a — F11 screens of the first alphabetical half (inventory path order, settings/web site/blog excluded;
/// audit <c>/denetim</c> and screen permissions <c>/yetki</c> are F11.1b's system endpoints) on the generic contract
/// (<see cref="DefinitionEndpoints"/>). Permissions equal the Blazor page policies; routes equal the Blazor page routes.
/// </summary>
public static partial class TanimApi
{
    private const string Tag = "Tanımlar";

    public static void MapTanimApi(this RouteGroupBuilder v1)
    {
        MapSimpleDefinitions(v1);   // own-service definitions (Aksesuar, Banka, Döviz, Özel kod, Gider türü, Hesap, Drop, Doluluk)
        v1.MapDocumentApi();        // /dokumanlar, /firma-belgeleri
        v1.MapCalendarApi();        // /takvim-abonelik
        v1.MapBranchApi();          // /subeler (+ hizmetler, birleştir)

        // ---- Kod + Ad + Aktif masters (MasterTanimService family)
        v1.MapDefinition(MasterDefinitions.For<Brand, BrandService>("/markalar", Tag, "marka",
            (s, b, ct) => s.CreateAsync(new BrandInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new BrandInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct),
            inUse: BrandInUseAsync));
        v1.MapDefinition(MasterDefinitions.For<CancelReason, CancelReasonService>("/iptal-sebepleri", Tag, "iptal sebebi",
            (s, b, ct) => s.CreateAsync(new CancelReasonInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new CancelReasonInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
        v1.MapDefinition(MasterDefinitions.For<Country, CountryService>("/ulkeler", Tag, "ülke",
            (s, b, ct) => s.CreateAsync(new CountryInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new CountryInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
        v1.MapDefinition(MasterDefinitions.For<CustomerGroup, CustomerGroupService>("/musteri-gruplari", Tag, "müşteri grubu",
            (s, b, ct) => s.CreateAsync(new CustomerGroupInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new CustomerGroupInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
        v1.MapDefinition(MasterDefinitions.For<Department, DepartmentService>("/departmanlar", Tag, "departman",
            (s, b, ct) => s.CreateAsync(new DepartmentInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, ct),
            (s, id, b, v, ct) => s.UpdateAsync(id, new DepartmentInput { Kod = b.Kod ?? "", Ad = b.Ad ?? "", Aktif = b.Aktif }, v, ct)));
    }

    /// <summary>
    /// A brand is stored BY NAME (free-text combo) on vehicles, vehicle types, vehicle groups and vehicle orders.
    /// Deleting a name still in use would orphan those records' brand in the pick lists; deactivate instead.
    /// </summary>
    private static async Task<string?> BrandInUseAsync(IServiceProvider sp, Brand b, CancellationToken ct)
    {
        await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ct);
        var n = await db.Vehicles.CountAsync(x => x.Marka == b.Ad, ct)
                + await db.VehicleTypes.CountAsync(x => x.Marka == b.Ad, ct)
                + await db.VehicleGroups.CountAsync(x => x.Marka == b.Ad, ct)
                + await db.AracSiparisleri.CountAsync(x => x.Marka == b.Ad, ct);
        return n > 0 ? DefinitionMessages.InUse("marka", "araç/tip/grup/sipariş", n) : null;
    }
}
