using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Vehicles;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.FiloPlan;

/// <summary>Filo plan hedefi kalıcılığı (FAZ-19).</summary>
public interface IFleetPlanRepository
{
    Task<IReadOnlyList<FiloPlanHedefi>> ListAsync(CancellationToken ct = default);
    Task<FiloPlanHedefi?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(FiloPlanHedefi row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<FiloPlanHedefi> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>F6.1b — satır kilidi + (doluysa) xmin sürüm karşılaştırması altında güncelleme.</summary>
    Task<bool> UpdateLockedAsync(Guid id, string? expectedVersion, Action<FiloPlanHedefi> apply, CancellationToken ct = default);

    /// <summary>F6.1b — satır sürümü (xmin); yoksa null.</summary>
    Task<string?> VersionAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Filo plan hedefi giriş modeli.</summary>
public sealed class FiloPlanInput
{
    public string? AracGrupAdi { get; set; }
    public string? Sipp { get; set; }
    public string? Donem { get; set; }
    public int HedefAdet { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>
/// Plan satırı + GERÇEKLEŞEN sayım.
///
/// <para><paramref name="Gerceklesen"/> KİRALANABİLİR filoyu sayar (Satıldı ve Pasif hariç) —
/// kapasite planı elde fiilen kullanılabilen araca bakar. Satılmış aracı saymak hedefi
/// tutuyormuş gibi gösterirdi. <paramref name="ToplamKayitli"/> ise aynı boyuttaki TÜM araçlardır
/// (satılmış/pasif dahil) — iki sayı yan yana durur ki kullanıcı farkı görebilsin.</para>
/// </summary>
public sealed record FiloPlanSatir(
    FiloPlanHedefi Hedef, int Gerceklesen, int ToplamKayitli)
{
    /// <summary>Hedef − gerçekleşen. Pozitif = EKSİK (alınması gereken), negatif = fazla.</summary>
    public int Fark => Hedef.HedefAdet - Gerceklesen;

    public string Durum => Fark switch
    {
        > 0 => "Eksik",
        < 0 => "Fazla",
        _ => "Tamam"
    };
}

/// <summary>
/// Filo plan hedefi iş mantığı (FAZ-19). Yazma <see cref="Permission.OperationsWrite"/>,
/// okuma ViewReports VEYA OperationsWrite (planı GİREN rol girdiğini görebilmeli — FAZ-45 dersi).
/// </summary>
public sealed class FleetPlanService(
    IFleetPlanRepository repository, IVehicleRepository vehicles, ICurrentUser currentUser)
{
    private readonly IFleetPlanRepository _repository = repository;
    private readonly IVehicleRepository _vehicles = vehicles;
    private readonly ICurrentUser _currentUser = currentUser;

    /// <summary>Hedefler + gerçekleşen sayım. Sayım TEK araç okumasıyla yapılır (N+1 yok).</summary>
    public async Task<IReadOnlyList<FiloPlanSatir>> ListWithCountAsync(CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        var targets = await _repository.ListAsync(ct);
        if (targets.Count == 0) return [];

        var fleet = await _vehicles.ListAsync(branch: null, ct);
        return targets
            .Select(h => new FiloPlanSatir(
                h,
                fleet.Count(v => Matches(h, v) && IsRentable(v)),
                fleet.Count(v => Matches(h, v))))
            .OrderBy(x => x.Hedef.AracGrupAdi ?? "", StringComparer.CurrentCulture)
            .ThenBy(x => x.Hedef.Sipp ?? "", StringComparer.CurrentCulture)
            .ThenBy(x => x.Hedef.Donem ?? "", StringComparer.CurrentCulture)
            .ToList();
    }

    public async Task<FiloPlanHedefi?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        return await _repository.FindAsync(id, ct);
    }

    /// <summary>
    /// Hedef eşleşmesi: dolu olan boyutların HEPSİ tutmalı (AND). Boş boyut kısıt getirmez.
    /// Karşılaştırma Türkçe harf-duyarsız — grup kodları elle de yazılabiliyor ("eko"/"EKO").
    /// </summary>
    public static bool Matches(FiloPlanHedefi h, Vehicle v)
    {
        if (h.AracGrupAdi is { } g && !TurkishText.EqualsIgnoreTurkishCase(v.Grup, g)) return false;
        if (h.Sipp is { } s && !TurkishText.EqualsIgnoreTurkishCase(v.Sipp, s)) return false;
        return true;
    }

    /// <summary>Kiralanabilir filo: satılmış ve pasif araç kapasite değildir.</summary>
    public static bool IsRentable(Vehicle v)
        => v.Durum != VehicleStatus.Satildi && v.Durum != VehicleStatus.Pasif;

    public async Task<Guid> CreateAsync(FiloPlanInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        var row = new FiloPlanHedefi
        {
            AracGrupAdi = n.Grup, Sipp = n.Sipp, Donem = n.Donem,
            HedefAdet = input.HedefAdet, Aciklama = n.Aciklama
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, FiloPlanInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.AracGrupAdi = n.Grup; r.Sipp = n.Sipp; r.Donem = n.Donem;
            r.HedefAdet = input.HedefAdet; r.Aciklama = n.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>
    /// Hedefi <paramref name="delta"/> kadar değiştirir (Artır/Azalt aksiyonu). Sonuç NEGATİF
    /// OLAMAZ — 0'da durur; negatif hedef anlamsız olurdu ve "Fazla" sütununu şişirirdi.
    /// </summary>
    public async Task<bool> ChangeTargetAsync(Guid id, int delta, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (delta == 0) return false;
        // F6.1b: oku-değiştir-yaz satır kilidi altında — eşzamanlı iki "Artır" önce tek artış olarak kayboluyordu.
        return await _repository.UpdateLockedAsync(id, null, r =>
        {
            r.HedefAdet = Math.Max(0, r.HedefAdet + delta);
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F6.1b — kayıt sürümü (xmin).</summary>
    public async Task<string?> VersionAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.ViewReports, Permission.OperationsWrite);
        return await _repository.VersionAsync(id, ct);
    }

    /// <summary>F6.1b — <see cref="UpdateAsync"/>'in sürümlü, kilitli karşılığı (/api/ui PUT; bayat → 409 cakisma).</summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, FiloPlanInput input, string version, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        return await _repository.UpdateLockedAsync(id, version, r =>
        {
            r.AracGrupAdi = n.Grup; r.Sipp = n.Sipp; r.Donem = n.Donem;
            r.HedefAdet = input.HedefAdet; r.Aciklama = n.Aciklama;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return await _repository.DeleteAsync(id, ct);
    }

    private static (string? Grup, string? Sipp, string? Donem, string? Aciklama) Normalize(FiloPlanInput i)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var group = T(i.AracGrupAdi);
        var sipp = T(i.Sipp)?.ToUpperInvariant();
        // İkisi de boşsa hedef TÜM FİLO'yu sayardı — sessiz bir "her araç" kuralı yerine red.
        if (group is null && sipp is null)
            throw new ValidationException("Araç grubu veya SIPP kodundan en az biri zorunludur.");
        if (i.HedefAdet < 0) throw new ValidationException("Hedef adet negatif olamaz.");
        return (group, sipp, T(i.Donem), T(i.Aciklama));
    }
}
