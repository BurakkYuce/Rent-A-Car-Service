using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleSegments;

/// <summary>
/// Araç segment master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class VehicleSegmentService(IVehicleSegmentRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleSegmentRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<VehicleSegment>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<VehicleSegment>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<VehicleSegment?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleSegmentInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu segment zaten var.");

        var segment = new VehicleSegment();
        Apply(segment, n);
        await _repository.CreateAsync(segment, ct);
        return segment.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleSegmentInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu segment zaten var.");

        return await _repository.UpdateAsync(id, segment =>
        {
            Apply(segment, n);
            segment.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(VehicleSegmentInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Segment kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Segment kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Segment adı zorunludur.");
    }

    private static VehicleSegmentInput Normalize(VehicleSegmentInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(VehicleSegment segment, VehicleSegmentInput n)
    {
        segment.Kod = n.Kod;
        segment.Ad = n.Ad;
        segment.Aciklama = n.Aciklama;
        segment.Aktif = n.Aktif;
    }
}
