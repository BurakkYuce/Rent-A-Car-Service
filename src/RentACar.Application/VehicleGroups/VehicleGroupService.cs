using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.VehicleGroups;

/// <summary>
/// Araç grubu master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. Açılır liste okuması
/// (<see cref="ListActiveAsync"/>) yetkisizdir (araç kayıt formu çağırır). Tenant izolasyonu/audit
/// alt katmanda otomatik.
/// </summary>
public sealed class VehicleGroupService(IVehicleGroupRepository repository, ICurrentUser currentUser)
{
    private readonly IVehicleGroupRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<VehicleGroup>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Form açılır listesi kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public Task<IReadOnlyList<VehicleGroup>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<VehicleGroup?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleGroupInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç grubu zaten var.");

        var group = new VehicleGroup();
        Apply(group, n);
        await _repository.CreateAsync(group, ct);
        return group.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleGroupInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu araç grubu zaten var.");

        return await _repository.UpdateAsync(id, group =>
        {
            Apply(group, n);
            group.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(VehicleGroupInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Araç grubu kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Araç grubu kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Araç grubu adı zorunludur.");

        if (!string.IsNullOrEmpty(n.Sipp) && n.Sipp.Length != 4)
            throw new ValidationException("SIPP kodu 4 harf olmalıdır (ör. CDMD).");
        RequireNonNegativeInt(n.KoltukSayisi, "Koltuk sayısı");
        RequireNonNegativeInt(n.KapiSayisi, "Kapı sayısı");
        RequireNonNegativeInt(n.BagajSayisi, "Bagaj sayısı");
        RequireNonNegativeInt(n.GunlukKmLimiti, "Günlük KM limiti");
        RequireNonNegativeDec(n.Provizyon, "Provizyon");
        RequireNonNegativeDec(n.MuafiyetTutari, "Muafiyet tutarı");
        RequireNonNegativeDec(n.AsimKmUcreti, "Aşım KM ücreti");
        if (n.SurucuMinYas is < 16 or > 99)
            throw new ValidationException("Sürücü min. yaş 16 ile 99 arasında olmalıdır.");
        if (n.GencSurucuYas is < 16 or > 99)
            throw new ValidationException("Genç sürücü yaşı 16 ile 99 arasında olmalıdır.");
        if (n.EhliyetMinYil is < 0 or > 80)
            throw new ValidationException("Ehliyet min. yıl 0 ile 80 arasında olmalıdır.");
    }

    private static void RequireNonNegativeInt(int? v, string label)
    {
        if (v is < 0) throw new ValidationException($"{label} negatif olamaz.");
    }

    private static void RequireNonNegativeDec(decimal? v, string label)
    {
        if (v is < 0m) throw new ValidationException($"{label} negatif olamaz.");
    }

    private static VehicleGroupInput Normalize(VehicleGroupInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Sipp = string.IsNullOrWhiteSpace(input.Sipp) ? null : input.Sipp.Trim().ToUpperInvariant(),
        Segment = TrimOrNull(input.Segment),
        KasaTuru = TrimOrNull(input.KasaTuru),
        KoltukSayisi = input.KoltukSayisi,
        KapiSayisi = input.KapiSayisi,
        BagajSayisi = input.BagajSayisi,
        SurucuMinYas = input.SurucuMinYas,
        GencSurucuYas = input.GencSurucuYas,
        EhliyetMinYil = input.EhliyetMinYil,
        Provizyon = input.Provizyon,
        MuafiyetTutari = input.MuafiyetTutari,
        GunlukKmLimiti = input.GunlukKmLimiti,
        AsimKmUcreti = input.AsimKmUcreti,
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(VehicleGroup group, VehicleGroupInput n)
    {
        group.Kod = n.Kod;
        group.Ad = n.Ad;
        group.Aciklama = n.Aciklama;
        group.Sipp = n.Sipp;
        group.Segment = n.Segment;
        group.KasaTuru = n.KasaTuru;
        group.KoltukSayisi = n.KoltukSayisi;
        group.KapiSayisi = n.KapiSayisi;
        group.BagajSayisi = n.BagajSayisi;
        group.SurucuMinYas = n.SurucuMinYas;
        group.GencSurucuYas = n.GencSurucuYas;
        group.EhliyetMinYil = n.EhliyetMinYil;
        group.Provizyon = n.Provizyon;
        group.MuafiyetTutari = n.MuafiyetTutari;
        group.GunlukKmLimiti = n.GunlukKmLimiti;
        group.AsimKmUcreti = n.AsimKmUcreti;
        group.Aktif = n.Aktif;
    }
}
