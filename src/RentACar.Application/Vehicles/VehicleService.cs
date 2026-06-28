using RentACar.Application.Authorization;
using RentACar.Application.Branches;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Vehicles;

/// <summary>
/// Araç iş mantığı: doğrulama (plaka zorunlu + tenant içinde benzersiz) + CRUD.
/// Tenant izolasyonu ve audit alt katmanda (DbContext filter + RLS + interceptor) otomatik.
/// Liste, rol bazlı ŞUBE kapsamıyla filtrelenir (operatör yalnız kendi şubesi).
/// </summary>
public sealed class VehicleService(IVehicleRepository repository, ICurrentUser currentUser, IBranchRepository branches)
{
    private readonly IVehicleRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IBranchRepository _branches = branches;

    /// <summary>Serbest-metin şubeyi tenant içi Branch FK'sine çözer (roadmap F1); eşleşmezse null (metin korunur).</summary>
    private async Task<Guid?> ResolveSubeAsync(string? sube, CancellationToken ct)
        => string.IsNullOrWhiteSpace(sube) ? null : (await _branches.FindByAdAsync(sube.Trim(), ct))?.Id;

    public Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(BranchScope.Effective(_currentUser), ct);

    /// <summary>Liste ekranı: arama/filtre + sayfalama. Rol bazlı şube kapsamı zorlanır.</summary>
    public Task<Common.PagedResult<Vehicle>> SearchAsync(VehicleFilter filter, CancellationToken ct = default)
    {
        var scope = BranchScope.Effective(_currentUser);
        if (scope is not null) filter.Sube = scope; // operatör kendi şubesi dışına çıkamaz
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 200) filter.PageSize = 20;
        return _repository.SearchAsync(filter, ct);
    }

    public Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(VehicleInput input, CancellationToken ct = default)
    {
        var plaka = Normalize(input.Plaka);
        Validate(plaka, input);

        if (await _repository.PlakaExistsAsync(plaka, excludeId: null, ct))
            throw new DuplicatePlakaException(plaka);

        var subeId = await ResolveSubeAsync(input.Sube, ct);
        var vehicle = new Vehicle
        {
            Plaka = plaka,
            Marka = Trim(input.Marka),
            Tip = Trim(input.Tip),
            Grup = Trim(input.Grup),
            Segment = Trim(input.Segment),
            Sipp = NormalizeSipp(input.Sipp),
            Renk = Trim(input.Renk),
            ModelYili = input.ModelYili,
            Vites = input.Vites,
            SasiNo = Trim(input.SasiNo),
            MotorNo = Trim(input.MotorNo),
            Sube = Trim(input.Sube),
            SubeId = subeId,
            Durum = input.Durum,
            FiloDurum = input.FiloDurum,
            Km = input.Km,
            Yakit = input.Yakit
        };
        ApplyExtended(vehicle, input);

        // Yarış koşulunda DB benzersiz index son güvencedir → repo 23505'i çevirir.
        await _repository.CreateAsync(vehicle, ct);
        return vehicle.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleInput input, CancellationToken ct = default)
    {
        var plaka = Normalize(input.Plaka);
        Validate(plaka, input);

        if (await _repository.PlakaExistsAsync(plaka, excludeId: id, ct))
            throw new DuplicatePlakaException(plaka);

        var subeId = await ResolveSubeAsync(input.Sube, ct);
        return await _repository.UpdateAsync(id, v =>
        {
            v.Plaka = plaka;
            v.Marka = Trim(input.Marka);
            v.Tip = Trim(input.Tip);
            v.Grup = Trim(input.Grup);
            v.Segment = Trim(input.Segment);
            v.Sipp = NormalizeSipp(input.Sipp);
            v.Renk = Trim(input.Renk);
            v.ModelYili = input.ModelYili;
            v.Vites = input.Vites;
            v.SasiNo = Trim(input.SasiNo);
            v.MotorNo = Trim(input.MotorNo);
            v.Sube = Trim(input.Sube);
            v.SubeId = subeId;
            v.Durum = input.Durum;
            v.FiloDurum = input.FiloDurum;
            v.Km = input.Km;
            v.Yakit = input.Yakit;
            ApplyExtended(v, input);
            v.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        => _repository.DeleteAsync(id, ct);

    private static void Validate(string plaka, VehicleInput input)
    {
        if (string.IsNullOrWhiteSpace(plaka))
            throw new ValidationException("Plaka zorunludur.");
        if (input.Km < 0)
            throw new ValidationException("KM negatif olamaz.");
        if (input.ModelYili is < 1950 or > 2100)
            throw new ValidationException("Model yılı 1950 ile 2100 arasında olmalıdır.");
        var sipp = input.Sipp?.Trim();
        if (!string.IsNullOrEmpty(sipp) && sipp.Length != 4)
            throw new ValidationException("SIPP kodu 4 harf olmalıdır (ör. CDMD).");
    }

    private static string Normalize(string? plaka)
        => (plaka ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);

    /// <summary>Parite zenginleştirme alanlarını uygular (Create + Update ortak). Hepsi opsiyonel.</summary>
    private static void ApplyExtended(Vehicle v, VehicleInput input)
    {
        v.MotorGucu = input.MotorGucu;
        v.SilindirHacmi = input.SilindirHacmi;
        v.RuhsatNo = Trim(input.RuhsatNo);
        v.TescilTarihi = input.TescilTarihi;
        v.AracSahibi = Trim(input.AracSahibi);
        v.AlimBedeli = input.AlimBedeli;
        v.AlimTarihi = input.AlimTarihi;
        v.AlisVergisiz = input.AlisVergisiz;
        v.AlisOtv = input.AlisOtv;
        v.AlisKdv = input.AlisKdv;
        v.AylikMaliyet = input.AylikMaliyet;
        v.FiloYonetimMaliyeti = input.FiloYonetimMaliyeti;
        v.IkinciElDeger = input.IkinciElDeger;
        v.FiloGirisTarih = input.FiloGirisTarih;
        v.FiloCikisTarih = input.FiloCikisTarih;
        v.OzelKod1 = Trim(input.OzelKod1);
        v.OzelKod2 = Trim(input.OzelKod2);
        v.OzelKod3 = Trim(input.OzelKod3);
        v.OzelKod4 = Trim(input.OzelKod4);
        v.OzelKod5 = Trim(input.OzelKod5);
    }

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>SIPP/ACRISS kodu: trim + büyük harf (boş → null).</summary>
    private static string? NormalizeSipp(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
}
