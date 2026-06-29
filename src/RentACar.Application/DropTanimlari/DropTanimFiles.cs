using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.DropTanimlari;

/// <summary>Drop matris kalıcılığı (roadmap N2).</summary>
public interface IDropTanimRepository
{
    Task<IReadOnlyList<DropTanim>> ListAsync(CancellationToken ct = default);
    Task<DropTanim?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(DropTanim row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<DropTanim> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Drop tanım oluştur/güncelle giriş modeli.</summary>
public sealed class DropTanimInput
{
    public string Lokasyon { get; set; } = string.Empty;
    public string Sube { get; set; } = string.Empty;
    public string? KarsilamaSekli { get; set; }
    public string? CalismaSekli { get; set; }
    public string? OzelIletisim { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>Lokasyon-şube drop matris master iş mantığı (roadmap N2). Yazma OperationsWrite.</summary>
public sealed class DropTanimService(IDropTanimRepository repository, ICurrentUser currentUser)
{
    private readonly IDropTanimRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<DropTanim>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<DropTanim?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(DropTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        var row = new DropTanim
        {
            Lokasyon = n.Lokasyon, Sube = n.Sube,
            KarsilamaSekli = n.KarsilamaSekli, CalismaSekli = n.CalismaSekli, OzelIletisim = n.OzelIletisim,
            Aktif = input.Aktif
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, DropTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.Lokasyon = n.Lokasyon; r.Sube = n.Sube;
            r.KarsilamaSekli = n.KarsilamaSekli; r.CalismaSekli = n.CalismaSekli; r.OzelIletisim = n.OzelIletisim;
            r.Aktif = input.Aktif; r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static (string Lokasyon, string Sube, string? KarsilamaSekli, string? CalismaSekli, string? OzelIletisim) Normalize(DropTanimInput i)
    {
        var lok = (i.Lokasyon ?? "").Trim();
        var sube = (i.Sube ?? "").Trim();
        if (string.IsNullOrWhiteSpace(lok)) throw new ValidationException("Lokasyon zorunludur.");
        if (string.IsNullOrWhiteSpace(sube)) throw new ValidationException("Şube zorunludur.");
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return (lok, sube, T(i.KarsilamaSekli), T(i.CalismaSekli), T(i.OzelIletisim));
    }
}
