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
        RequireNonNegativeInt(n.KucukBagaj, "Küçük bagaj");
        RequireNonNegativeInt(n.BuyukBagaj, "Büyük bagaj");
        RequireNonNegativeInt(n.GunlukKmLimiti, "Günlük KM limiti");
        RequireNonNegativeInt(n.AylikMaxKm, "Aylık max KM");
        RequireNonNegativeInt(n.WebSira, "Web sıra");
        RequireNonNegativeInt(n.UpgradeSira, "Upgrade sıra");
        RequireNonNegativeDec(n.Provizyon, "Provizyon");
        RequireNonNegativeDec(n.Provizyon2, "Provizyon 2");
        RequireNonNegativeDec(n.MuafiyetTutari, "Muafiyet tutarı");
        RequireNonNegativeDec(n.Muafiyet2, "Muafiyet 2");
        RequireNonNegativeDec(n.AsimKmUcreti, "Aşım KM ücreti");
        RequireNonNegativeDec(n.YakitFiyati, "Yakıt fiyatı");
        if (n.SurucuMinYas is < 16 or > 99)
            throw new ValidationException("Sürücü min. yaş 16 ile 99 arasında olmalıdır.");
        if (n.GencSurucuYas is < 16 or > 99)
            throw new ValidationException("Genç sürücü yaşı 16 ile 99 arasında olmalıdır.");
        if (n.EhliyetMinYil is < 0 or > 80)
            throw new ValidationException("Ehliyet min. yıl 0 ile 80 arasında olmalıdır.");
        if (n.GencEhliyetMinYil is < 0 or > 80)
            throw new ValidationException("Genç ehliyet min. yıl 0 ile 80 arasında olmalıdır.");
        if (n.SonraOdeOran is < 0m or > 100m)
            throw new ValidationException("Sonra öde oranı 0 ile 100 arasında olmalıdır (%).");
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
        Marka = TrimOrNull(input.Marka),
        Tipi = TrimOrNull(input.Tipi),
        KoltukSayisi = input.KoltukSayisi,
        KapiSayisi = input.KapiSayisi,
        BagajSayisi = input.BagajSayisi,
        KucukBagaj = input.KucukBagaj,
        BuyukBagaj = input.BuyukBagaj,
        SurucuMinYas = input.SurucuMinYas,
        GencSurucuYas = input.GencSurucuYas,
        EhliyetMinYil = input.EhliyetMinYil,
        GencEhliyetMinYil = input.GencEhliyetMinYil,
        Provizyon = input.Provizyon,
        Provizyon2 = input.Provizyon2,
        MuafiyetTutari = input.MuafiyetTutari,
        Muafiyet2 = input.Muafiyet2,
        GunlukKmLimiti = input.GunlukKmLimiti,
        AylikMaxKm = input.AylikMaxKm,
        AsimKmUcreti = input.AsimKmUcreti,
        YakitFiyati = input.YakitFiyati,
        SonraOdeOran = input.SonraOdeOran,
        KrediKartiSart = input.KrediKartiSart,
        WebSira = input.WebSira,
        UpgradeSira = input.UpgradeSira,
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
        group.Marka = n.Marka;
        group.Tipi = n.Tipi;
        group.KoltukSayisi = n.KoltukSayisi;
        group.KapiSayisi = n.KapiSayisi;
        group.BagajSayisi = n.BagajSayisi;
        group.KucukBagaj = n.KucukBagaj;
        group.BuyukBagaj = n.BuyukBagaj;
        group.SurucuMinYas = n.SurucuMinYas;
        group.GencSurucuYas = n.GencSurucuYas;
        group.EhliyetMinYil = n.EhliyetMinYil;
        group.GencEhliyetMinYil = n.GencEhliyetMinYil;
        group.Provizyon = n.Provizyon;
        group.Provizyon2 = n.Provizyon2;
        group.MuafiyetTutari = n.MuafiyetTutari;
        group.Muafiyet2 = n.Muafiyet2;
        group.GunlukKmLimiti = n.GunlukKmLimiti;
        group.AylikMaxKm = n.AylikMaxKm;
        group.AsimKmUcreti = n.AsimKmUcreti;
        group.YakitFiyati = n.YakitFiyati;
        group.SonraOdeOran = n.SonraOdeOran;
        group.KrediKartiSart = n.KrediKartiSart;
        group.WebSira = n.WebSira;
        group.UpgradeSira = n.UpgradeSira;
        group.Aktif = n.Aktif;
    }
}
