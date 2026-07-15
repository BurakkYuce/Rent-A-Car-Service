using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Baflar;

/// <summary>
/// BAF (personel araç tahsis) iş mantığı (roadmap L5): tahsis oluştur (çıkış) + teslim al (dönüş km/yakıt) + iptal.
/// DEFTER POSTLAMAZ (zimmet kaydı) → salt takip; yazma OperationsWrite.
/// </summary>
public sealed class BafService(IBafRepository repository, ICurrentUser currentUser)
{
    private readonly IBafRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<Baf>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(BranchScope.EffectiveFilter(_currentUser), ct); // C3 (Baf FK'sız → metin dalı)

    public async Task<Baf?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var baf = await _repository.FindAsync(id, ct);
        if (baf is not null) BranchScope.RequireInScope(_currentUser, baf.Sube); // tekil kapsam (M3 deseni)
        return baf;
    }

    public async Task<Guid> CreateAsync(BafInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.PersonelId == Guid.Empty) throw new ValidationException("Personel seçilmelidir.");
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.CikisKm < 0) throw new ValidationException("Çıkış KM negatif olamaz.");

        var row = new Baf
        {
            PersonelId = input.PersonelId,
            VehicleId = input.VehicleId,
            CikisTarihi = input.CikisTarihi ?? DateTimeOffset.UtcNow,
            CikisKm = input.CikisKm,
            CikisYakit = input.CikisYakit,
            Sube = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim(),
            Durum = Domain.Enums.BafDurum.Acik,
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim()
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> TeslimAlAsync(Guid id, int donusKm, int? donusYakit, DateTimeOffset? donusTarihi = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var baf = await _repository.FindAsync(id, ct);
        if (baf is null) return false;
        BranchScope.RequireInScope(_currentUser, baf.Sube); // adversarial: tekil şube-kapsam
        if (baf.Durum != Domain.Enums.BafDurum.Acik) throw new ValidationException("Yalnız açık tahsis teslim alınabilir.");
        if (donusKm < baf.CikisKm) throw new ValidationException("Dönüş KM çıkış KM'den küçük olamaz.");
        return await _repository.TeslimAlAsync(id, donusKm, donusYakit, donusTarihi ?? DateTimeOffset.UtcNow, ct);
    }

    public async Task<bool> IptalAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var baf = await _repository.FindAsync(id, ct);
        if (baf is null) return false;
        BranchScope.RequireInScope(_currentUser, baf.Sube); // adversarial: tekil şube-kapsam
        return await _repository.IptalAsync(id, ct);
    }
}
