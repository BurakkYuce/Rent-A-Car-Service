using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.Authorization;

/// <summary>
/// Ekran yetki override yönetimi + çözümü (roadmap E3). Override CRUD'u yetki yönetimidir → ManageUsers
/// (yalnız Admin). Çözüm (<see cref="EnsureScreenAccessAsync"/>/<see cref="IsScreenAllowedAsync"/>) ekranların
/// OPT-IN çağırdığı katman: matris floor'u korur, override varsa deny-by-default sıkılaştırır. PermissionGuard
/// (mevcut floor) DEĞİŞMEZ — bu additive bir katman.
/// </summary>
public sealed class ScreenPermissionService(IScreenPermissionRepository repository, ICurrentUser currentUser)
{
    private readonly IScreenPermissionRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

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
        var csv = string.Join(",", roller.Distinct().Select(r => r.ToString()));
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

    // ---- Çözüm (opt-in ekran gating; yetki gerektirmez — çağıran ekranın guard'ı) ----
    public async Task<bool> IsScreenAllowedAsync(string ekranKodu, Permission permission, CancellationToken ct = default)
    {
        var ov = await _repository.FindByKodAsync(Normalize(ekranKodu), ct);
        var roller = ov is { Aktif: true } ? ParseRoles(ov.AllowedRolesCsv) : null;
        return PermissionResolver.IsAllowed(_currentUser.Role, permission, roller);
    }

    /// <summary>Erişim yoksa ValidationException (PermissionGuard deseni). Opt-in ekranlar çağırır.</summary>
    public async Task EnsureScreenAccessAsync(string ekranKodu, Permission permission, CancellationToken ct = default)
    {
        if (!await IsScreenAllowedAsync(ekranKodu, permission, ct))
            throw new ValidationException($"Bu ekran için yetkiniz yok ({Normalize(ekranKodu)}).");
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
