using RentACar.Application.Authorization;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Locations;
using RentACar.Domain.Common;

namespace RentACar.Application.Crm;

/// <summary>
/// CRM kayıtlarının (anket, şikayet, assistans) ŞUBE KAPSAMI — TEK kural, SERVİS katmanında (r317 M1: Blazor form uçları
/// servisi kapsamsız çağırıyordu; A şubesi operatörü B'nin kaydını değiştirip siliyordu). Hem <c>/api/ui</c> hem Blazor yolu
/// buradan geçer; Web'deki <c>CrmScope</c> yalnız toplu liste süzmesini yapar ve aynı <see cref="Visible"/> kuralını kullanır.
/// <para>Kayıtların kendi şube kolonu yok; kapsam üst kayıttan gelir:</para>
/// <list type="number">
/// <item>Kira sözleşmesine bağlıysa → kiranın kapsamı (<c>CikisSubeId</c>/<c>CikisOfisi</c>).</item>
/// <item>Değilse ve çıkış ofisi yazılıysa → ofisin (Location) şubesi ya da ofis metni.</item>
/// <item>İkisi de yoksa şubesiz kayıttır → herkes görür.</item>
/// </list>
/// </summary>
public sealed class CrmScopeGuard(ICurrentUser currentUser, IBookingRepository bookings, ILocationRepository locations)
{
    public const string OutOfScopeMessage = "Bu kayıt şube kapsamınız dışında.";

    /// <summary>
    /// Görünürlük kuralı (liste ve tekil kayıt ortak). <paramref name="rentalInScope"/>: kira BULUNDUYSA kapsamda mı
    /// (bulunamadıysa null); <paramref name="officeInScope"/>: ofis yazılıysa kapsamda mı.
    /// </summary>
    public static bool Visible(Guid? rentalId, bool? rentalInScope, string? office, bool? officeInScope)
    {
        if (rentalId is { } id && id != Guid.Empty && rentalInScope is { } r) return r;
        if (!string.IsNullOrWhiteSpace(office)) return officeInScope == true;
        // Kira kimliği var ama kira yok (silinmiş) ve ofis yok → hangi şubeye ait bilinmiyor: kapsamlıya kapalı.
        return rentalId is null || rentalId == Guid.Empty;
    }

    private bool Restricted(out BranchScope.BranchFilter filter)
    {
        filter = BranchScope.EffectiveFilter(currentUser);
        return !filter.Unrestricted;
    }

    private async Task<bool> OfficeInScopeAsync(BranchScope.BranchFilter filter, string office, CancellationToken ct)
        => BranchScope.InScope(filter, (await locations.FindByNameAsync(office, ct))?.SubeId, office);

    /// <summary>
    /// Güvenlik F4 — liste süzmesi (harici JWT API gibi Web'deki toplu <c>CrmScope</c>'tan geçmeyen yüzeyler): her kayıt
    /// <see cref="Visible"/> kuralından geçer (kira kapsamı, yoksa ofis kapsamı; şubesiz kayıt herkese). Kapsamsız
    /// kullanıcıda liste aynen döner. Kira ve ofis kararları istek içinde önbelleğe alınır.
    /// </summary>
    public async Task<IReadOnlyList<T>> FilterAsync<T>(
        IReadOnlyList<T> rows, Func<T, (Guid? RentalId, string? Office)> key, CancellationToken ct = default)
    {
        if (!Restricted(out var filter)) return rows;
        var rentals = new Dictionary<Guid, bool?>();
        var offices = new Dictionary<string, bool>(StringComparer.Ordinal);
        var result = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            var (rentalId, office) = key(row);
            bool? rentalIn = null;
            if (rentalId is { } id && id != Guid.Empty)
            {
                if (!rentals.TryGetValue(id, out rentalIn))
                {
                    rentalIn = await bookings.FindRentalAsync(id, ct) is { } r ? BranchScope.InScope(filter, r.CikisSubeId, r.CikisOfisi) : null;
                    rentals[id] = rentalIn;
                }
            }
            var o = office?.Trim();
            bool? officeIn = null;
            if (!string.IsNullOrEmpty(o))
            {
                if (!offices.TryGetValue(o, out var known)) offices[o] = known = await OfficeInScopeAsync(filter, o, ct);
                officeIn = known;
            }
            if (Visible(rentalId, rentalIn, o, officeIn)) result.Add(row);
        }
        return result;
    }

    /// <summary>Mevcut kayıt (okuma/güncelleme/silme) kapsamda olmalı; değilse 403 — durum kontrolünden ÖNCE çağrılır.</summary>
    public async Task RequireRecordAsync(Guid? rentalId, string? office, CancellationToken ct = default)
    {
        if (!Restricted(out var filter)) return;
        bool? rentalIn = null;
        if (rentalId is { } id && id != Guid.Empty && await bookings.FindRentalAsync(id, ct) is { } rental)
            rentalIn = BranchScope.InScope(filter, rental.CikisSubeId, rental.CikisOfisi);
        var o = office?.Trim();
        bool? officeIn = string.IsNullOrEmpty(o) ? null : await OfficeInScopeAsync(filter, o, ct);
        if (!Visible(rentalId, rentalIn, o, officeIn)) throw new NoPermissionException(OutOfScopeMessage);
    }

    /// <summary>
    /// Yazma HEDEFİ: bağlanan kira var olmalı (yok/başka kiracı → 400 <c>errors[rentalId]</c>) ve kapsamda olmalı (403);
    /// yazılan çıkış ofisi kapsamda olmalı (403). <paramref name="creating"/>: r317 L1 — şube kapsamlı kullanıcı
    /// oluşturmada şubesiz (kirasız VE ofissiz) kayıt açamaz (400 <c>errors[rentalId]</c>); aksi hâlde kayıt tüm şubelere
    /// açılır ve silip yeniden oluşturmak güncelleme kemerini (<see cref="RequireBranchKept"/>) atlatırdı. Kapsamsız
    /// kullanıcı şubesiz kayıt açabilir.
    /// </summary>
    public async Task RequireTargetAsync(Guid? rentalId, string? office, bool creating, CancellationToken ct = default)
    {
        var restricted = Restricted(out var filter);
        var hasRental = rentalId is { } id && id != Guid.Empty;
        if (hasRental)
        {
            var rental = await bookings.FindRentalAsync(rentalId!.Value, ct)
                ?? throw new ValidationException("Kira sözleşmesi bulunamadı.", "rentalId");
            if (restricted && !BranchScope.InScope(filter, rental.CikisSubeId, rental.CikisOfisi))
                throw new NoPermissionException("Seçilen kira sözleşmesi şube kapsamınız dışında.");
        }
        var o = office?.Trim();
        if (!string.IsNullOrEmpty(o) && restricted && !await OfficeInScopeAsync(filter, o, ct))
            throw new NoPermissionException("Seçilen çıkış ofisi şube kapsamınız dışında.");
        if (creating && restricted && !hasRental && string.IsNullOrEmpty(o))
            throw new ValidationException(
                "Şubeye bağlı kullanıcı kaydı bir kira sözleşmesine ya da şubesinin çıkış ofisine bağlamalıdır.", "rentalId");
    }

    /// <summary>
    /// (#295 L3) Güncellemede şubeye bağlı kayıt (kira ya da ofis) şube kapsamlı kullanıcı tarafından şubesiz yapılamaz —
    /// şubesiz kayıt herkese görünür. Kapsamsız kullanıcı boşaltabilir; zaten şubesiz kayıt şubesiz kalabilir.
    /// </summary>
    public void RequireBranchKept(Guid? currentRentalId, string? currentOffice, Guid? rentalId, string? office)
    {
        if (!Restricted(out _)) return;
        var hadBranch = currentRentalId is { } c && c != Guid.Empty || !string.IsNullOrWhiteSpace(currentOffice);
        var hasBranch = rentalId is { } r && r != Guid.Empty || !string.IsNullOrWhiteSpace(office);
        if (hadBranch && !hasBranch)
            throw new NoPermissionException(
                "Şube kapsamlı kullanıcı kaydın kira ve çıkış ofisi bağını birlikte kaldıramaz; kayıt tüm şubelere açılırdı.");
    }

    /// <summary>Güncelleme sırası (tek yer): mevcut kayıt kapsamda → bağ korunuyor → yeni hedef geçerli ve kapsamda.</summary>
    public async Task RequireUpdateAsync(Guid? currentRentalId, string? currentOffice, Guid? rentalId, string? office,
        CancellationToken ct = default)
    {
        await RequireRecordAsync(currentRentalId, currentOffice, ct);
        RequireBranchKept(currentRentalId, currentOffice, rentalId, office);
        await RequireTargetAsync(rentalId, office, creating: false, ct);
    }
}
