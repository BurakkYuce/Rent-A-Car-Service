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
public sealed class VehicleService(IVehicleRepository repository, ICurrentUser currentUser, IBranchRepository branches, ITenantCache cache)
{
    private readonly IVehicleRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IBranchRepository _branches = branches;
    private readonly ITenantCache _cache = cache;
    public const string CacheKey = "vehicles"; // dropdown kaynağı (tam tenant listesi); RentalService de invalidate eder (araç Durum değişince)

    /// <summary>Serbest-metin şubeyi tenant içi Branch FK'sine çözer (roadmap F1); eşleşmezse null (metin korunur).</summary>
    private async Task<Guid?> ResolveSubeAsync(string? sube, CancellationToken ct)
        => string.IsNullOrWhiteSpace(sube) ? null : (await _branches.FindByAdAsync(sube.Trim(), ct))?.Id;

    /// <summary>Dropdown kaynağı: TAM tenant listesi cache'lenir, şube kapsamı bellek-içi filtrelenir
    /// (operatör kendi şubesini görür). Yazımda invalidate. Durum diğer yollarca değişirse TTL (10dk) tazeler.</summary>
    public async Task<IReadOnlyList<Vehicle>> ListAsync(CancellationToken ct = default)
    {
        var all = await _cache.GetOrCreateAsync(CacheKey, () => _repository.ListAsync(null, ct), ct);
        var scope = BranchScope.Effective(_currentUser);
        return scope is null ? all : all.Where(v => v.Sube == scope).ToList();
    }

    /// <summary>Liste ekranı: arama/filtre + sayfalama. Rol bazlı şube kapsamı zorlanır.</summary>
    public Task<Common.PagedResult<Vehicle>> SearchAsync(VehicleFilter filter, CancellationToken ct = default)
    {
        var scope = BranchScope.Effective(_currentUser);
        if (scope is not null) filter.Sube = scope; // operatör kendi şubesi dışına çıkamaz
        if (filter.Page < 1) filter.Page = 1;
        if (filter.PageSize is < 1 or > 200) filter.PageSize = 20;
        return _repository.SearchAsync(filter, ct);
    }

    public async Task<Vehicle?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var v = await _repository.FindAsync(id, ct);
        if (v is not null) BranchScope.RequireInScope(_currentUser, v.Sube); // adversarial M3
        return v;
    }

    public async Task<Guid> CreateAsync(VehicleInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
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
        _cache.Invalidate(CacheKey);
        return vehicle.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, VehicleInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        var plaka = Normalize(input.Plaka);
        Validate(plaka, input);

        if (await _repository.PlakaExistsAsync(plaka, excludeId: id, ct))
            throw new DuplicatePlakaException(plaka);

        var subeId = await ResolveSubeAsync(input.Sube, ct);
        var ok = await _repository.UpdateAsync(id, v =>
        {
            BranchScope.RequireInScope(_currentUser, v.Sube); // adversarial M3 (mevcut şube — reassign ÖNCESİ)
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
        _cache.Invalidate(CacheKey);
        return ok;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite); // adversarial M4
        BranchScope.RequireInScope(_currentUser, (await _repository.FindAsync(id, ct))?.Sube); // adversarial M3
        var ok = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(CacheKey);
        return ok;
    }

    private static void Validate(string plaka, VehicleInput input)
    {
        if (string.IsNullOrWhiteSpace(plaka))
            throw new ValidationException("Plaka zorunludur.");
        if (input.Km < 0)
            throw new ValidationException("KM negatif olamaz.");
        var maxModelYili = DateTimeOffset.UtcNow.Year + 1; // yeni model araç en fazla gelecek yıl olabilir
        if (input.ModelYili is < 1950 || input.ModelYili > maxModelYili)
            throw new ValidationException($"Model yılı 1950 ile {maxModelYili} arasında olmalıdır.");
        var sipp = input.Sipp?.Trim();
        if (!string.IsNullOrEmpty(sipp) && sipp.Length != 4)
            throw new ValidationException("SIPP kodu 4 harf olmalıdır (ör. CDMD).");
        // Karne/KPI para alanları — negatif bedel anlamsız ve aşağı akışta (amortisman/ROI) bozucu.
        if (input.AlimBedeli is < 0m)
            throw new ValidationException("Alım bedeli negatif olamaz.");
        if (input.IkinciElDeger is < 0m)
            throw new ValidationException("İkinci el değeri negatif olamaz.");
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
        // roadmap G1
        v.HgsNo = Trim(input.HgsNo);
        v.OgsNo = Trim(input.OgsNo);
        v.KasaTipi = Trim(input.KasaTipi);
        v.DetayTipi = Trim(input.DetayTipi);
        v.AlimFaturaNo = Trim(input.AlimFaturaNo);
        v.AlimYapilanFirma = Trim(input.AlimYapilanFirma);
        v.KiraKmLimiti = input.KiraKmLimiti;
        // roadmap K2 — operasyon bayrakları + bakım/lastik
        v.WebRezKapat = input.WebRezKapat;
        v.OfisRezKapat = input.OfisRezKapat;
        v.ZIzni = input.ZIzni;
        v.Utts = input.Utts;
        v.KarLastigi = input.KarLastigi;
        v.YedekAnahtar = input.YedekAnahtar;
        v.Temizlik = input.Temizlik;
        v.Rehin = input.Rehin;
        v.SonBakimTarih = input.SonBakimTarih;
        v.SonBakimKm = input.SonBakimKm;
        v.LastikDurumu = Trim(input.LastikDurumu);
    }

    private static string? Trim(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>SIPP/ACRISS kodu: trim + büyük harf (boş → null).</summary>
    private static string? NormalizeSipp(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
}
