using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Crm;
using RentACar.Application.Locations;
using RentACar.Domain.Common;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Api.Cari;

/// <summary>
/// F7.1 — CRM kayıtlarının (anket, şikayet, assistans) ŞUBE KAPSAMI. Bu kayıtların kendi şube kolonu yok; kapsam
/// üst kayıttan gelir ("alt kayıt uçları üst kaydın kapsamından geçer"):
/// <list type="number">
/// <item>Kira sözleşmesine bağlıysa → kiranın kapsamı (<c>CikisSubeId</c>/<c>CikisOfisi</c>, <see cref="BranchScope.InScope"/>).</item>
/// <item>Değilse ve çıkış ofisi yazılıysa → ofisin (Location) şubesi ya da ofis metni.</item>
/// <item>İkisi de yoksa şubesiz kayıttır → herkes görür (Blazor davranışı).</item>
/// </list>
/// Blazor ekranları bu süzgeci uygulamıyordu (operatör tüm şubelerin şikayetini görüyordu); yeni yüzey o açığı taşımaz.
/// Kapsam dışı tekil kayıt 403 <c>yetki_yok</c>; listede satır hiç görünmez.
/// </summary>
internal static class CrmScope
{
    /// <summary>Kayıt kümesi için kapsam yüklemi (tek sorgu kira + ofis başına bir lokasyon araması).</summary>
    public static async Task<Func<Guid?, string?, bool>> BuildAsync(
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        IEnumerable<(Guid? RentalId, string? Office)> records, CancellationToken ct)
    {
        var filter = BranchScope.EffectiveFilter(user);
        if (filter.Unrestricted) return static (_, _) => true;

        var list = records.ToList();
        var rentalIds = list.Where(r => r.RentalId is not null).Select(r => r.RentalId!.Value).Distinct().ToList();
        Dictionary<Guid, bool> rentalInScope = [];
        if (rentalIds.Count > 0)
        {
            await using var db = await dbf.CreateDbContextAsync(ct);
            var rows = await db.Rentals.AsNoTracking().Where(r => rentalIds.Contains(r.Id))
                .Select(r => new { r.Id, r.CikisSubeId, r.CikisOfisi }).ToListAsync(ct);
            rentalInScope = rows.ToDictionary(r => r.Id, r => BranchScope.InScope(filter, r.CikisSubeId, r.CikisOfisi));
        }

        Dictionary<string, bool> officeInScope = new(StringComparer.Ordinal);
        foreach (var office in list.Select(r => r.Office?.Trim()).Where(o => !string.IsNullOrEmpty(o)).Distinct())
            officeInScope[office!] = BranchScope.InScope(filter, (await locations.FindByNameAsync(office!, ct))?.SubeId, office);

        // Görünürlük kuralı TEK yerde (CrmScopeGuard.Visible; yazma yolları servis katmanında aynı kuralla korunur).
        return (rentalId, office) =>
        {
            bool? rentalIn = rentalId is { } id && rentalInScope.TryGetValue(id, out var r) ? r : null;
            var o = office?.Trim();
            bool? officeIn = string.IsNullOrEmpty(o) ? null : officeInScope.TryGetValue(o, out var ok) && ok;
            return CrmScopeGuard.Visible(rentalId, rentalIn, o, officeIn);
        };
    }

    /// <summary>Tekil kayıt kapsamı (okuma; güncelleme/silmede durumdan ÖNCE): kapsam dışı → 403. Yazma hedefi, bağ
    /// korunması ve şubesiz oluşturma kuralları servis katmanında (<see cref="CrmScopeGuard"/>; Blazor yoluyla ortak).</summary>
    public static async Task RequireAsync(
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        Guid? rentalId, string? office, CancellationToken ct)
    {
        var inScope = await BuildAsync(user, dbf, locations, [(rentalId, office)], ct);
        if (!inScope(rentalId, office)) throw new NoPermissionException(CrmScopeGuard.OutOfScopeMessage);
    }

    /// <summary>Bağlanan cari bu kiracıda olmalı (RLS kapsamlı okuma; başka kiracının kimliği "yok"tur).</summary>
    public static async Task RequireCustomerAsync(IDbContextFactory<AppDbContext> dbf, Guid? customerId, string field, CancellationToken ct)
    {
        if (customerId is not { } id) return;
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Customers.AsNoTracking().AnyAsync(c => c.Id == id, ct))
            throw new ValidationException("Seçilen cari bulunamadı.", field);
    }

    /// <summary>Bağlanan personel bu kiracıda olmalı.</summary>
    public static async Task RequireStaffAsync(IDbContextFactory<AppDbContext> dbf, Guid? staffId, string field, CancellationToken ct)
    {
        if (staffId is not { } id) return;
        await using var db = await dbf.CreateDbContextAsync(ct);
        if (!await db.Personeller.AsNoTracking().AnyAsync(p => p.Id == id, ct))
            throw new ValidationException("Seçilen personel bulunamadı.", field);
    }
}
