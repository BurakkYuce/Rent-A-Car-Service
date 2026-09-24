using System.Text.Json;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Ekran yetki override yönetimi + çözümü (roadmap E3) + yetki-grubu/şablon (PR-D). Override CRUD'u yetki
/// yönetimidir → ManageUsers (yalnız Admin). Çözüm (<see cref="EnsureScreenAccessAsync"/>/<see cref="IsScreenAllowedAsync"/>)
/// ekranların OPT-IN çağırdığı katman: matris floor'u korur, override varsa deny-by-default sıkılaştırır. PermissionGuard
/// (mevcut floor) DEĞİŞMEZ — bu additive bir katman.
/// </summary>
public sealed class ScreenPermissionService(
    IScreenPermissionRepository repository, ICurrentUser currentUser, IYetkiGrupRepository grupRepository)
{
    private readonly IScreenPermissionRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IYetkiGrupRepository _grup = grupRepository;

    // ---- Yönetim (ManageUsers) ----
    public async Task<IReadOnlyList<Domain.Entities.ScreenPermission>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _repository.ListAsync(ct);
    }

    public async Task SetAsync(string ekranKodu, IEnumerable<UserRole> roller, bool aktif = true, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var kod = Normalize(ekranKodu);
        if (kod.Length == 0) throw new ValidationException("Ekran kodu zorunludur.");
        var roles = roller.Distinct().ToArray();
        await RequireAdminIfAdminAccessChangesAsync(kod, aktif ? roles : null, ct);
        var csv = string.Join(",", roles.Select(r => r.ToString()));
        await _repository.UpsertAsync(kod, s =>
        {
            s.EkranKodu = kod;
            s.AllowedRolesCsv = csv;
            s.Aktif = aktif;
            s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> RemoveAsync(string ekranKodu, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await RequireAdminIfAdminAccessChangesAsync(Normalize(ekranKodu), null, ct);
        return await _repository.DeleteAsync(Normalize(ekranKodu), ct);
    }

    /// <summary>
    /// Yetki şablonu/kopyala (roadmap M2): <paramref name="kaynak"/> rolünün ekran erişimini <paramref name="hedef"/>
    /// role klonlar — kaynağın bulunduğu (ve hedefin henüz olmadığı) her ekran override'ına hedef rol EKLENİR
    /// (mevcut roller korunur; SADECE ekleme — kimsenin erişimi kaldırılmaz). Güncellenen ekran sayısını döner.
    /// </summary>
    public async Task<int> KopyalaRolAsync(UserRole kaynak, UserRole hedef, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        if (kaynak == hedef) throw new ValidationException("Kaynak ve hedef rol farklı olmalıdır.");
        // Admin'e ekran eklemek Admin'in erişimine dokunmaktır → yalnız Admin rolü (#304 L3).
        if (hedef == UserRole.Admin) RequireAdminRole();

        var sayac = 0;
        foreach (var s in await _repository.ListAsync(ct))
        {
            var roller = ParseRoles(s.AllowedRolesCsv);
            if (!roller.Contains(kaynak) || roller.Contains(hedef)) continue; // kaynakta yok / hedefte zaten var
            var csv = string.Join(",", roller.Append(hedef).Distinct().Select(r => r.ToString()));
            await _repository.UpsertAsync(s.EkranKodu, x =>
            {
                x.AllowedRolesCsv = csv;
                x.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }, ct);
            sayac++;
        }
        return sayac;
    }

    // ---- Yetki grubu / şablon (PR-D, ManageUsers): ekran-izni profillerini kaydet/uygula/sil ----

    /// <summary>Tenant'ın kayıtlı yetki-grubu şablonları.</summary>
    public async Task<IReadOnlyList<Domain.Entities.YetkiGrup>> ListGruplarAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _grup.ListAsync(ct);
    }

    /// <summary>Mevcut ekran-izni yapılandırmasını (tüm ScreenPermission'lar) isimli bir şablon olarak kaydet
    /// (anlık görüntü). Aynı adla varsa üzerine yazılır. Sonra <see cref="UygulaGrupAsync"/> ile geri yüklenir.</summary>
    public async Task SnapshotGrupAsync(string ad, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        if (Normalize(ad).Length == 0) throw new ValidationException("Şablon adı zorunludur.");
        var kalemler = (await _repository.ListAsync(ct))
            .Select(s => new YetkiGrupKalem(s.EkranKodu, ParseRoles(s.AllowedRolesCsv).Select(r => r.ToString()).ToArray()))
            .ToList();
        var json = JsonSerializer.Serialize(kalemler);
        await _grup.UpsertAsync(ad.Trim(), g =>
        {
            g.Ad = ad.Trim();
            g.KalemlerJson = json;
            g.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Şablonu UYGULA: kalemlerini ScreenPermission override'larına yazar (bulk SetAsync). GÜVENLİK:
    /// yalnız ekran-override'ı yazar — rol-matrisi floor'u DEĞİŞMEZ; uygulanan kısıt PermissionResolver'da yine
    /// floor'la kesişir (grant floor'u AŞAMAZ). Uygulanan kalem sayısını döner.</summary>
    public async Task<int> UygulaGrupAsync(string ad, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var g = await _grup.FindByAdAsync(ad.Trim(), ct)
            ?? throw new ValidationException($"Şablon bulunamadı: {ad}.");
        var kalemler = (JsonSerializer.Deserialize<List<YetkiGrupKalem>>(g.KalemlerJson) ?? [])
            .Select(k => (k.Ekran, Roller: k.Roller
                .Select(r => Enum.TryParse<UserRole>(r, ignoreCase: true, out var ur) && Enum.IsDefined(ur) ? (UserRole?)ur : null)
                .Where(r => r is not null).Select(r => r!.Value).Distinct().ToArray()))
            .ToList();
        // #304 L3: Admin erişimini değiştiren kalem varsa HİÇBİR kalem yazılmadan reddedilir (yarım uygulama yok).
        foreach (var k in kalemler)
            await RequireAdminIfAdminAccessChangesAsync(Normalize(k.Ekran), k.Roller, ct);
        var sayac = 0;
        foreach (var k in kalemler)
        {
            await SetAsync(k.Ekran, k.Roller, ct: ct); // ManageUsers guard + Normalize; override yazar (floor'u değiştirmez)
            sayac++;
        }
        return sayac;
    }

    public async Task<bool> SilGrupAsync(string ad, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _grup.DeleteAsync(ad.Trim(), ct);
    }

    // ---- Çözüm (opt-in ekran gating; yetki gerektirmez — çağıran ekranın guard'ı) ----
    public async Task<bool> IsScreenAllowedAsync(string ekranKodu, Permission permission, CancellationToken ct = default)
    {
        var ov = await _repository.FindByKodAsync(Normalize(ekranKodu), ct);
        var roller = ov is { Aktif: true } ? ParseRoles(ov.AllowedRolesCsv) : null;
        return PermissionResolver.IsAllowed(_currentUser.Role, permission, roller);
    }

    /// <summary>Erişim yoksa YetkiYokException (PermissionGuard deseni). Opt-in ekranlar çağırır.</summary>
    public async Task EnsureScreenAccessAsync(string ekranKodu, Permission permission, CancellationToken ct = default)
    {
        if (!await IsScreenAllowedAsync(ekranKodu, permission, ct))
            throw new YetkiYokException($"Bu ekran için yetkiniz yok ({Normalize(ekranKodu)}).");
    }

    /// <summary>
    /// #304 L3 — Admin'in bir ekrana erişimini değiştiren (override'dan Admin'i çıkaran ya da ekleyen, Admin'i dışlayan
    /// override'ı silen/pasifleştiren) yazım yalnız Admin rolüne açıktır. ManageUsers istisnası almış Yönetici kendi
    /// üstündeki rolü kilitleyemez. <paramref name="newRoles"/> null = override etkisiz (silinmiş/pasif; Admin erişimli).
    /// </summary>
    private async Task RequireAdminIfAdminAccessChangesAsync(string code, IReadOnlyCollection<UserRole>? newRoles, CancellationToken ct)
    {
        if (_currentUser.Role == UserRole.Admin) return;
        var current = await _repository.FindByKodAsync(code, ct);
        var adminBefore = current is not { Aktif: true } || ParseRoles(current.AllowedRolesCsv).Contains(UserRole.Admin);
        var adminAfter = newRoles is null || newRoles.Contains(UserRole.Admin);
        if (adminBefore != adminAfter) RequireAdminRole();
    }

    private void RequireAdminRole()
    {
        if (_currentUser.Role != UserRole.Admin)
            throw new YetkiYokException("Admin rolünün ekran erişimini yalnız Admin değiştirebilir.");
    }

    private static string Normalize(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();

    private static IReadOnlyCollection<UserRole> ParseRoles(string csv)
    {
        var set = new HashSet<UserRole>();
        // ignoreCase + IsDefined: elle/DB CSV'de "admin" veya tanımsız "999" sessizce yanlış davranmasın
        // (adversarial L1/L2 — fail-safe sertleştirme).
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<UserRole>(part, ignoreCase: true, out var r) && Enum.IsDefined(r)) set.Add(r);
        return set;
    }
}
