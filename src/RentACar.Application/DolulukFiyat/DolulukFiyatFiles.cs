using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.DolulukFiyat;

public interface IDolulukFiyatKuralRepository
{
    Task<IReadOnlyList<DolulukFiyatKural>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DolulukFiyatKural>> ListActiveAsync(CancellationToken ct = default);
    Task<DolulukFiyatKural?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> KodExistsAsync(string kod, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(DolulukFiyatKural row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<DolulukFiyatKural> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Grup doluluk yüzdesi sağlayıcısı (FAZ 3.A7). Pencere gün sayısı BİTİŞ-HARİÇ gün farkıdır
/// (ComputeGun ile hizalı: 5 günlük kira penceresi = 5 araç-gün/araç); grupta araç yoksa null
/// (0 araçlı grupta %0/%100 anlamsız — surge tetiklenmez).</summary>
public interface IOccupancyProvider
{
    Task<decimal?> GetGrupDolulukYuzdeAsync(
        string grupKod, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Doluluk fiyat kuralı giriş modeli.</summary>
public sealed class DolulukFiyatKuralInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? AracGrupKod { get; set; }
    public int EsikYuzde { get; set; }
    public decimal CarpanYuzde { get; set; }
    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>Doluluk fiyat kuralı master iş mantığı (FAZ 3.A7). Yazma OperationsWrite; Esik 1..100,
/// Carpan 0..50 (DB CHECK ile çift savunma).</summary>
public sealed class DolulukFiyatKuralService(IDolulukFiyatKuralRepository repository, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<DolulukFiyatKural>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);

    public async Task<Guid> CreateAsync(DolulukFiyatKuralInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await repository.KodExistsAsync(n.Kod, null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu doluluk kuralı zaten var.");
        var row = new DolulukFiyatKural();
        Apply(row, n);
        await repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, DolulukFiyatKuralInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await repository.KodExistsAsync(n.Kod, id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu doluluk kuralı zaten var.");
        return await repository.UpdateAsync(id, r => { Apply(r, n); r.UpdatedAtUtc = DateTimeOffset.UtcNow; }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.DeleteAsync(id, ct);
    }

    private static void Validate(DolulukFiyatKuralInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Kural kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Kural kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Kural adı zorunludur.");
        if (n.EsikYuzde is < 1 or > 100) throw new ValidationException("Doluluk eşiği %1 ile %100 arasında olmalıdır.");
        if (n.CarpanYuzde is < 0m or > 50m) throw new ValidationException("Fiyat çarpanı %0 ile %50 arasında olmalıdır (sert tavan).");
        if (n.GecerlilikBas is { } b && n.GecerlilikBit is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
    }

    private static DolulukFiyatKuralInput Normalize(DolulukFiyatKuralInput i) => new()
    {
        Kod = (i.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (i.Ad ?? string.Empty).Trim(),
        AracGrupKod = string.IsNullOrWhiteSpace(i.AracGrupKod) ? null : i.AracGrupKod.Trim().ToUpperInvariant(),
        EsikYuzde = i.EsikYuzde,
        CarpanYuzde = i.CarpanYuzde,
        GecerlilikBas = i.GecerlilikBas,
        GecerlilikBit = i.GecerlilikBit,
        Aktif = i.Aktif
    };

    private static void Apply(DolulukFiyatKural r, DolulukFiyatKuralInput n)
    {
        r.Kod = n.Kod; r.Ad = n.Ad; r.AracGrupKod = n.AracGrupKod;
        r.EsikYuzde = n.EsikYuzde; r.CarpanYuzde = n.CarpanYuzde;
        r.GecerlilikBas = n.GecerlilikBas; r.GecerlilikBit = n.GecerlilikBit;
        r.Aktif = n.Aktif;
    }
}
