using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.DamageFiles;

/// <summary>
/// Hasar dosyası (BAF) onay akışı: Açık → Onayda → Onaylandi/Reddedildi → Kapali.
/// Geçiş guard'larıyla geçersiz atlama engellenir. Mali belge değildir (defter yazmaz).
/// </summary>
public sealed class DamageFileService(IDamageFileRepository repository)
{
    private readonly IDamageFileRepository _repository = repository;

    public Task<IReadOnlyList<DamageFile>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);
    public Task<DamageFile?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(DamageFileInput input, CancellationToken ct = default)
    {
        if (input.VehicleId == Guid.Empty) throw new ValidationException("Araç seçilmelidir.");
        if (input.TahminiTutar is < 0) throw new ValidationException("Tahmini tutar negatif olamaz.");

        var file = new DamageFile
        {
            VehicleId = input.VehicleId,
            RentalId = input.RentalId,
            CariId = input.CariId,
            AcilisTarihi = input.AcilisTarihi ?? DateTimeOffset.UtcNow,
            Aciklama = input.Aciklama,
            TahminiTutar = input.TahminiTutar,
            Durum = HasarDurum.Acik
        };
        await _repository.CreateAsync(file, ct);
        return file.Id;
    }

    /// <summary>Açık → Onayda (onaya gönder).</summary>
    public Task<bool> OnayaGonderAsync(Guid id, CancellationToken ct = default)
        => Transition(id, HasarDurum.Onayda, from: [HasarDurum.Acik], note: null, ct);

    /// <summary>Onayda → Onaylandi.</summary>
    public Task<bool> OnaylaAsync(Guid id, string? not = null, CancellationToken ct = default)
        => Transition(id, HasarDurum.Onaylandi, from: [HasarDurum.Onayda], note: not, ct);

    /// <summary>Onayda → Reddedildi.</summary>
    public Task<bool> ReddetAsync(Guid id, string? not = null, CancellationToken ct = default)
        => Transition(id, HasarDurum.Reddedildi, from: [HasarDurum.Onayda], note: not, ct);

    /// <summary>Onaylandi/Reddedildi → Kapali (dosyayı kapat).</summary>
    public Task<bool> KapatAsync(Guid id, CancellationToken ct = default)
        => Transition(id, HasarDurum.Kapali, from: [HasarDurum.Onaylandi, HasarDurum.Reddedildi], note: null, ct);

    private Task<bool> Transition(
        Guid id, HasarDurum to, HasarDurum[] from, string? note, CancellationToken ct)
        => _repository.UpdateAsync(id, f =>
        {
            if (Array.IndexOf(from, f.Durum) < 0)
                throw new ValidationException($"'{f.Durum}' durumundan '{to}' durumuna geçilemez.");
            f.Durum = to;
            if (note is not null) f.OnayNotu = note;
            f.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
}
