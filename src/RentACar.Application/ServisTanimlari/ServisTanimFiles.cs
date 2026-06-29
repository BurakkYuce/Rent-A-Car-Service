using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ServisTanimlari;

/// <summary>Periyodik bakım tanım kalıcılığı (roadmap N1).</summary>
public interface IServisTanimRepository
{
    Task<IReadOnlyList<ServisTanim>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ServisTanim>> ListActiveAsync(CancellationToken ct = default);
    Task<ServisTanim?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(ServisTanim row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<ServisTanim> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Servis tanım oluştur/güncelle giriş modeli.</summary>
public sealed class ServisTanimInput
{
    public string Kod { get; set; } = string.Empty;
    public string AracTipi { get; set; } = string.Empty;
    public int BakimKm { get; set; }
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>Periyodik bakım tanım master iş mantığı (roadmap N1). Yazma OperationsWrite.</summary>
public sealed class ServisTanimService(IServisTanimRepository repository, ICurrentUser currentUser)
{
    private readonly IServisTanimRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<ServisTanim>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<IReadOnlyList<ServisTanim>> ListActiveAsync(CancellationToken ct = default) => _repository.ListActiveAsync(ct);
    public Task<ServisTanim?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ServisTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        var row = new ServisTanim { Kod = n.Kod, AracTipi = n.AracTipi, BakimKm = input.BakimKm, Aciklama = n.Aciklama, Aktif = input.Aktif };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, ServisTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.Kod = n.Kod; r.AracTipi = n.AracTipi; r.BakimKm = input.BakimKm; r.Aciklama = n.Aciklama; r.Aktif = input.Aktif;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static (string Kod, string AracTipi, string? Aciklama) Normalize(ServisTanimInput i)
    {
        var kod = (i.Kod ?? "").Trim().ToUpperInvariant();
        var tip = (i.AracTipi ?? "").Trim();
        if (string.IsNullOrWhiteSpace(kod)) throw new ValidationException("Kod zorunludur.");
        if (kod.Length > 32) throw new ValidationException("Kod en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(tip)) throw new ValidationException("Araç tipi zorunludur.");
        if (i.BakimKm < 0) throw new ValidationException("Bakım KM negatif olamaz.");
        return (kod, tip, string.IsNullOrWhiteSpace(i.Aciklama) ? null : i.Aciklama.Trim());
    }
}
