using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Legal;

/// <summary>
/// Hukuk dosyası master iş mantığı (roadmap C2): doğrulama + DosyaNo benzersizliği + CRUD. Yazma
/// operasyonel → <see cref="Permission.OperationsWrite"/>. Tenant izolasyonu/audit alt katmanda.
/// Tutar bilgilendirme amaçlı — deftere postlamaz.
/// </summary>
public sealed class HukukDosyaService(IHukukDosyaRepository repository, ICurrentUser currentUser)
{
    private readonly IHukukDosyaRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<HukukDosya?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(HukukDosyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.DosyaNoExistsAsync(n.DosyaNo!, excludeId: null, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");

        var row = new HukukDosya();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, HukukDosyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.DosyaNoExistsAsync(n.DosyaNo!, excludeId: id, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(HukukDosyaInput n)
    {
        if (string.IsNullOrWhiteSpace(n.DosyaNo)) throw new ValidationException("Dosya no zorunludur.");
        if (n.DosyaNo!.Length > 64) throw new ValidationException("Dosya no en çok 64 karakter olabilir.");
        if (n.Tutar < 0m) throw new ValidationException("Tutar negatif olamaz.");
    }

    private static HukukDosyaInput Normalize(HukukDosyaInput input) => new()
    {
        DosyaNo = (input.DosyaNo ?? string.Empty).Trim().ToUpperInvariant(),
        CariId = input.CariId,
        Tur = input.Tur,
        Avukat = TrimOrNull(input.Avukat),
        Tutar = input.Tutar,
        Durum = input.Durum,
        Tarih = input.Tarih,
        Aciklama = TrimOrNull(input.Aciklama),
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(HukukDosya row, HukukDosyaInput n)
    {
        row.DosyaNo = n.DosyaNo!;
        row.CariId = n.CariId;
        row.Tur = n.Tur;
        row.Avukat = n.Avukat;
        row.Tutar = n.Tutar;
        row.Durum = n.Durum;
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Aciklama = n.Aciklama;
        row.Aktif = n.Aktif;
    }
}
