using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

/// <summary>
/// Müşteri şikayeti iş mantığı (roadmap C3): CRUD + Konu doğrulama + durum/çözüm takibi. Yazma →
/// OperationsWrite. Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class SikayetService(ISikayetRepository repository, ICurrentUser currentUser)
{
    private readonly ISikayetRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Sikayet>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<Sikayet?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(SikayetInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        var row = new Sikayet();
        Apply(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, SikayetInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, input);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(SikayetInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Konu)) throw new ValidationException("Konu zorunludur.");
        if (n.Konu!.Trim().Length > 256) throw new ValidationException("Konu en çok 256 karakter olabilir.");
    }

    private static void Apply(Sikayet row, SikayetInput n)
    {
        row.CariId = n.CariId;
        row.Konu = n.Konu!.Trim();
        row.Detay = string.IsNullOrWhiteSpace(n.Detay) ? null : n.Detay.Trim();
        row.Durum = n.Durum;
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Cozum = string.IsNullOrWhiteSpace(n.Cozum) ? null : n.Cozum.Trim();
    }
}
