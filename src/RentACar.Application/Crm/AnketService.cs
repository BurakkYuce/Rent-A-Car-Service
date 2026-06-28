using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Crm;

/// <summary>
/// Müşteri anketi iş mantığı (roadmap C3): CRUD + Puan doğrulama (0-10). Yazma → OperationsWrite.
/// Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class AnketService(IAnketRepository repository, ICurrentUser currentUser)
{
    private readonly IAnketRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Anket>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<Anket?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(AnketInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        Validate(input);
        var row = new Anket();
        Apply(row, input);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, AnketInput input, CancellationToken ct = default)
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

    private static void Validate(AnketInput n)
    {
        if (n.Puan is < 0 or > 10) throw new ValidationException("Puan 0 ile 10 arasında olmalıdır.");
    }

    private static void Apply(Anket row, AnketInput n)
    {
        row.CariId = n.CariId;
        row.Puan = n.Puan;
        row.Yorum = string.IsNullOrWhiteSpace(n.Yorum) ? null : n.Yorum.Trim();
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Kaynak = string.IsNullOrWhiteSpace(n.Kaynak) ? null : n.Kaynak.Trim();
    }
}
