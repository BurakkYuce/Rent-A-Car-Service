using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.HesapKodlari;

/// <summary>Muhasebe hesap-kodu kalıcılığı (roadmap N1).</summary>
public interface IHesapKoduRepository
{
    Task<IReadOnlyList<HesapKodu>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<HesapKodu>> ListActiveAsync(CancellationToken ct = default);
    Task<HesapKodu?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(HesapKodu row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<HesapKodu> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Hesap kodu oluştur/güncelle giriş modeli.</summary>
public sealed class HesapKoduInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>Muhasebe hesap-kodu master iş mantığı (roadmap N1). Yazma OperationsWrite.</summary>
public sealed class HesapKoduService(IHesapKoduRepository repository, ICurrentUser currentUser)
{
    private readonly IHesapKoduRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<HesapKodu>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<IReadOnlyList<HesapKodu>> ListActiveAsync(CancellationToken ct = default) => _repository.ListActiveAsync(ct);
    public Task<HesapKodu?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(HesapKoduInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var (kod, ad, aciklama) = Normalize(input);
        var row = new HesapKodu { Kod = kod, Ad = ad, Aciklama = aciklama, Aktif = input.Aktif };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, HesapKoduInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var (kod, ad, aciklama) = Normalize(input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.Kod = kod; r.Ad = ad; r.Aciklama = aciklama; r.Aktif = input.Aktif;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static (string Kod, string Ad, string? Aciklama) Normalize(HesapKoduInput i)
    {
        var kod = (i.Kod ?? "").Trim().ToUpperInvariant();
        var ad = (i.Ad ?? "").Trim();
        if (string.IsNullOrWhiteSpace(kod)) throw new ValidationException("Hesap kodu zorunludur.");
        if (kod.Length > 32) throw new ValidationException("Hesap kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(ad)) throw new ValidationException("Hesap adı zorunludur.");
        return (kod, ad, string.IsNullOrWhiteSpace(i.Aciklama) ? null : i.Aciklama.Trim());
    }
}
