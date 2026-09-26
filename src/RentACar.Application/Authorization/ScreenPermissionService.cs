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
    IScreenPermissionRepository repository, ICurrentUser currentUser, IPermissionGroupRepository groupRepository)
{
    private readonly IScreenPermissionRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPermissionGroupRepository _group = groupRepository;

    // ---- Yönetim (ManageUsers) ----
    public async Task<IReadOnlyList<Domain.Entities.ScreenPermission>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _repository.ListAsync(ct);
    }

    public async Task SetAsync(string screenCode, IEnumerable<UserRole> roller, bool active = true, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var code = Normalize(screenCode);
        if (code.Length == 0) throw new ValidationException("Ekran kodu zorunludur.");
        var roles = roller.Distinct().ToArray();
        await RequireAdminIfAdminAccessChangesAsync(code, roles, ct, active: active);
        var csv = string.Join(",", roles.Select(r => r.ToString()));
        await _repository.UpsertAsync(code, s =>
        {
            s.EkranKodu = code;
            s.AllowedRolesCsv = csv;
            s.Aktif = active;
            s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> RemoveAsync(string screenCode, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await RequireAdminIfAdminAccessChangesAsync(Normalize(screenCode), null, ct);
        return await _repository.DeleteAsync(Normalize(screenCode), ct);
    }

    /// <summary>
    /// Yetki şablonu/kopyala (roadmap M2): <paramref name="source"/> rolünün ekran erişimini <paramref name="target"/>
    /// role klonlar — kaynağın bulunduğu (ve hedefin henüz olmadığı) her ekran override'ına hedef rol EKLENİR
    /// (mevcut roller korunur; SADECE ekleme — kimsenin erişimi kaldırılmaz). Güncellenen ekran sayısını döner.
    /// </summary>
    public async Task<int> CopyRoleAsync(UserRole source, UserRole target, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        if (source == target) throw new ValidationException("Kaynak ve hedef rol farklı olmalıdır.");
        // Admin'e ekran eklemek Admin'in erişimine dokunmaktır → yalnız Admin rolü (#304 L3).
        if (target == UserRole.Admin) RequireAdminRole();

        var counter = 0;
        foreach (var s in await _repository.ListAsync(ct))
        {
            var roller = ParseRoles(s.AllowedRolesCsv);
            if (!roller.Contains(source) || roller.Contains(target)) continue; // kaynakta yok / hedefte zaten var
            var csv = string.Join(",", roller.Append(target).Distinct().Select(r => r.ToString()));
            await _repository.UpsertAsync(s.EkranKodu, x =>
            {
                x.AllowedRolesCsv = csv;
                x.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }, ct);
            counter++;
        }
        return counter;
    }

    // ---- Yetki grubu / şablon (PR-D, ManageUsers): ekran-izni profillerini kaydet/uygula/sil ----

    /// <summary>Tenant'ın kayıtlı yetki-grubu şablonları.</summary>
    public async Task<IReadOnlyList<Domain.Entities.YetkiGrup>> ListGroupsAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _group.ListAsync(ct);
    }

    /// <summary>Mevcut ekran-izni yapılandırmasını (tüm ScreenPermission'lar) isimli bir şablon olarak kaydet
    /// (anlık görüntü). Aynı adla varsa üzerine yazılır. Sonra <see cref="ApplyGroupAsync"/> ile geri yüklenir.</summary>
    public async Task SnapshotGroupAsync(string name, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        if (Normalize(name).Length == 0) throw new ValidationException("Şablon adı zorunludur.");
        var items = (await _repository.ListAsync(ct))
            .Select(s => new YetkiGrupKalem(s.EkranKodu, ParseRoles(s.AllowedRolesCsv).Select(r => r.ToString()).ToArray()))
            .ToList();
        var json = JsonSerializer.Serialize(items);
        await _group.UpsertAsync(name.Trim(), g =>
        {
            g.Ad = name.Trim();
            g.KalemlerJson = json;
            g.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>Şablonu UYGULA: kalemlerini ScreenPermission override'larına yazar (bulk SetAsync). GÜVENLİK:
    /// yalnız ekran-override'ı yazar — rol-matrisi floor'u DEĞİŞMEZ; uygulanan kısıt PermissionResolver'da yine
    /// floor'la kesişir (grant floor'u AŞAMAZ). Uygulanan kalem sayısını döner.</summary>
    public async Task<int> ApplyGroupAsync(string name, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var g = await _group.FindByNameAsync(name.Trim(), ct)
            ?? throw new ValidationException($"Şablon bulunamadı: {name}.");
        var items = (JsonSerializer.Deserialize<List<YetkiGrupKalem>>(g.KalemlerJson) ?? [])
            .Select(k => (k.Ekran, Roller: k.Roller
                .Select(r => Enum.TryParse<UserRole>(r, ignoreCase: true, out var ur) && Enum.IsDefined(ur) ? (UserRole?)ur : null)
                .Where(r => r is not null).Select(r => r!.Value).Distinct().ToArray()))
            .ToList();
        // #304 L3: Admin erişimini değiştiren kalem varsa HİÇBİR kalem yazılmadan reddedilir (yarım uygulama yok).
        foreach (var k in items)
            await RequireAdminIfAdminAccessChangesAsync(Normalize(k.Ekran), k.Roller, ct);
        var counter = 0;
        foreach (var k in items)
        {
            await SetAsync(k.Ekran, k.Roller, ct: ct); // ManageUsers guard + Normalize; override yazar (floor'u değiştirmez)
            counter++;
        }
        return counter;
    }

    public async Task<bool> DeleteGroupAsync(string name, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return await _group.DeleteAsync(name.Trim(), ct);
    }

    // ---- Çözüm (opt-in ekran gating; yetki gerektirmez — çağıran ekranın guard'ı) ----
    public async Task<bool> IsScreenAllowedAsync(string screenCode, Permission permission, CancellationToken ct = default)
    {
        var ov = await _repository.FindByCodeAsync(Normalize(screenCode), ct);
        var roller = ov is { Aktif: true } ? ParseRoles(ov.AllowedRolesCsv) : null;
        return PermissionResolver.IsAllowed(_currentUser.Role, permission, roller);
    }

    /// <summary>Erişim yoksa YetkiYokException (PermissionGuard deseni). Opt-in ekranlar çağırır.</summary>
    public async Task EnsureScreenAccessAsync(string screenCode, Permission permission, CancellationToken ct = default)
    {
        if (!await IsScreenAllowedAsync(screenCode, permission, ct))
            throw new NoPermissionException($"Bu ekran için yetkiniz yok ({Normalize(screenCode)}).");
    }

    /// <summary>
    /// #304 L3 — Admin'in bir ekrana erişimini değiştiren (override'dan Admin'i çıkaran ya da ekleyen, Admin'i dışlayan
    /// override'ı silen/pasifleştiren) yazım yalnız Admin rolüne açıktır. ManageUsers istisnası almış Yönetici kendi
    /// üstündeki rolü kilitleyemez. <paramref name="newRoles"/> null = kayıt siliniyor.
    /// <para>2026-09-25 (#304 L3 devamı): PASİF kayıt da sayılır. Admin'i dışlayan pasif kayıt bugün etkisizdir ama
    /// Admin'in erişimini bekleyen bir kilittir: şablon anlık görüntüsüne girer ve sonradan bir Admin uygulayınca
    /// (ya da aktifleştirince) Admin kendini kilitler. Bu yüzden iki şey karşılaştırılır: ETKİN erişim (aktif
    /// override'a göre) ve SAKLI rol listesindeki Admin (kaydın aktifliğinden bağımsız). Biri değişiyorsa yalnız Admin.</para>
    /// </summary>
    private async Task RequireAdminIfAdminAccessChangesAsync(string code, IReadOnlyCollection<UserRole>? newRoles,
        CancellationToken ct, bool active = true)
    {
        if (_currentUser.Role == UserRole.Admin) return;
        var current = await _repository.FindByCodeAsync(code, ct);
        var storedBefore = current is null || ParseRoles(current.AllowedRolesCsv).Contains(UserRole.Admin);
        var storedAfter = newRoles is null || newRoles.Contains(UserRole.Admin);
        var effectiveBefore = current is not { Aktif: true } || storedBefore;
        var effectiveAfter = newRoles is null || !active || storedAfter;
        if (effectiveBefore != effectiveAfter || storedBefore != storedAfter) RequireAdminRole();
    }

    private void RequireAdminRole()
    {
        if (_currentUser.Role != UserRole.Admin)
            throw new NoPermissionException("Admin rolünün ekran erişimini yalnız Admin değiştirebilir.");
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
