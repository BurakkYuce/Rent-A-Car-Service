using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ReservationSources;

/// <summary>
/// Rezervasyon kaynağı master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// → <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class ReservationSourceService(IReservationSourceRepository repository, ICurrentUser currentUser)
{
    private readonly IReservationSourceRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<ReservationSource>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<ReservationSource>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<ReservationSource?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ReservationSourceInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu rezervasyon kaynağı zaten var.");

        var source = new ReservationSource();
        Apply(source, n);
        await _repository.CreateAsync(source, ct);
        return source.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, ReservationSourceInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu rezervasyon kaynağı zaten var.");

        return await _repository.UpdateAsync(id, source =>
        {
            Apply(source, n);
            source.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(ReservationSourceInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Rezervasyon kaynağı kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Rezervasyon kaynağı kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Rezervasyon kaynağı adı zorunludur.");
    }

    private static ReservationSourceInput Normalize(ReservationSourceInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(ReservationSource source, ReservationSourceInput n)
    {
        source.Kod = n.Kod;
        source.Ad = n.Ad;
        source.Aktif = n.Aktif;
    }
}
