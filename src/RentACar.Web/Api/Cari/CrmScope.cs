using Microsoft.EntityFrameworkCore;
using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
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
            officeInScope[office!] = BranchScope.InScope(filter, (await locations.FindByAdAsync(office!, ct))?.SubeId, office);

        return (rentalId, office) =>
        {
            if (rentalId is { } id && rentalInScope.TryGetValue(id, out var r)) return r;
            var o = office?.Trim();
            if (!string.IsNullOrEmpty(o)) return officeInScope.TryGetValue(o, out var ok) && ok;
            // Kira kimliği var ama kira yok (FK'sız şikayet, silinmiş kira) ve ofis yok → şubesiz kayıt gibi değil:
            // kapsamlı kullanıcıya gösterilmez (hangi şubeye ait olduğu bilinmiyor).
            return rentalId is null;
        };
    }

    /// <summary>Tekil kayıt kapsamı (okuma/güncelleme/silme): kapsam dışı → 403.</summary>
    public static async Task RequireAsync(
        ICurrentUser user, IDbContextFactory<AppDbContext> dbf, ILocationRepository locations,
        Guid? rentalId, string? office, CancellationToken ct)
    {
        var inScope = await BuildAsync(user, dbf, locations, [(rentalId, office)], ct);
        if (!inScope(rentalId, office)) throw new YetkiYokException("Bu kayıt şube kapsamınız dışında.");
    }

    /// <summary>
    /// (#295 L3) Yazma HEDEFİ, güncellemede: şubeye bağlı kayıt (kira ya da ofis) şube kapsamlı kullanıcı tarafından
    /// "şubesiz" yapılamaz — şubesiz kayıt herkese görünür (<see cref="BuildAsync"/>), yani boş hedef kaydı tüm şubelere
    /// açardı. Kapsamsız kullanıcı (firma geneli) boşaltabilir; zaten şubesiz kayıt şubesiz kalabilir. Mevcut kaydın
    /// kapsamı çağıran tarafından <see cref="RequireAsync"/> ile ÖNCE doğrulanır.
    /// </summary>
    public static void RequireBranchKept(
        ICurrentUser user, Guid? currentRentalId, string? currentOffice, Guid? rentalId, string? office)
    {
        if (BranchScope.EffectiveFilter(user).Unrestricted) return;
        var hadBranch = currentRentalId is { } c && c != Guid.Empty || !string.IsNullOrWhiteSpace(currentOffice);
        var hasBranch = rentalId is { } r && r != Guid.Empty || !string.IsNullOrWhiteSpace(office);
        if (hadBranch && !hasBranch)
            throw new YetkiYokException(
                "Şube kapsamlı kullanıcı kaydın kira ve çıkış ofisi bağını birlikte kaldıramaz; kayıt tüm şubelere açılırdı.");
    }

    /// <summary>
    /// Yazma HEDEFİ (giriş noktasında): bağlanan kira var olmalı ve kapsamda olmalı (<see cref="RentalService.GetAsync"/>
    /// kapsam dışında 403 atar; yok/başka kiracı → 400 <c>errors[rentalId]</c>); yazılan çıkış ofisi kapsamda olmalı.
    /// </summary>
    public static async Task RequireTargetAsync(
        ICurrentUser user, RentalService rentals, ILocationRepository locations, Guid? rentalId, string? office, CancellationToken ct)
    {
        if (rentalId is { } id && id != Guid.Empty && await rentals.GetAsync(id, ct) is null)
            throw new ValidationException("Kira sözleşmesi bulunamadı.", "rentalId");
        var o = office?.Trim();
        if (string.IsNullOrEmpty(o)) return;
        var filter = BranchScope.EffectiveFilter(user);
        if (!filter.Unrestricted && !BranchScope.InScope(filter, (await locations.FindByAdAsync(o, ct))?.SubeId, o))
            throw new YetkiYokException("Seçilen çıkış ofisi şube kapsamınız dışında.");
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
