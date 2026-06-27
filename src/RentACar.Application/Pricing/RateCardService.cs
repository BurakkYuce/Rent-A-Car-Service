using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Pricing;

/// <summary>
/// Tarife (rate card) iş mantığı: doğrulama + kod benzersizliği + CRUD + fiyat LOOKUP.
/// Yazma <see cref="Permission.OperationsWrite"/> (fiyatlandırma operasyonel yapılandırma);
/// lookup yetkisizdir (rezervasyon/teklif akışı çağırır). Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class RateCardService(IRateCardRepository repository, ICurrentUser currentUser)
{
    private readonly IRateCardRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RateCard>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<RateCard?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    /// <summary>
    /// Fiyat lookup: grup + gün sayısı + tarih için EN UYGUN aktif tarifeyi seç. Kural:
    /// kapsayan adaylar arasından en YÜKSEK MinGun (en dar/özel kademe); eşitlikte en SON
    /// başlayan dönem (GecerliBas, null = en eski). Eşleşme yoksa null. Yetki gerektirmez.
    /// </summary>
    public async Task<RateCard?> GetRateAsync(string grup, int gun, DateTimeOffset tarih, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(grup) || gun < 1) return null;
        var adaylar = await _repository.ListByGroupAsync(grup.Trim(), ct);
        return adaylar
            .Where(r => r.Covers(gun, tarih))
            .OrderByDescending(r => r.MinGun)
            .ThenByDescending(r => r.GecerliBas ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    public async Task<Guid> CreateAsync(RateCardInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife zaten var.");

        var rc = new RateCard();
        Apply(rc, n);
        await _repository.CreateAsync(rc, ct);
        return rc.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, RateCardInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu tarife zaten var.");

        return await _repository.UpdateAsync(id, rc =>
        {
            Apply(rc, n);
            rc.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(RateCardInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Tarife kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Tarife kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Tarife adı zorunludur.");
        if (string.IsNullOrWhiteSpace(n.Grup)) throw new ValidationException("Araç grubu zorunludur.");
        if (n.MinGun < 1) throw new ValidationException("Min gün en az 1 olmalıdır.");
        if (n.MaxGun < n.MinGun) throw new ValidationException("Max gün, min günden küçük olamaz.");
        if (n.GunlukUcret < 0) throw new ValidationException("Günlük ücret negatif olamaz.");
        if (n.GecerliBas is { } b && n.GecerliBit is { } e && e < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
    }

    private static RateCardInput Normalize(RateCardInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Grup = (input.Grup ?? string.Empty).Trim(),
        MinGun = input.MinGun,
        MaxGun = input.MaxGun,
        GunlukUcret = input.GunlukUcret,
        Doviz = string.IsNullOrWhiteSpace(input.Doviz) ? "TRY" : input.Doviz.Trim().ToUpperInvariant(),
        GecerliBas = input.GecerliBas,
        GecerliBit = input.GecerliBit,
        Aktif = input.Aktif
    };

    private static void Apply(RateCard rc, RateCardInput n)
    {
        rc.Kod = n.Kod;
        rc.Ad = n.Ad;
        rc.Grup = n.Grup;
        rc.MinGun = n.MinGun;
        rc.MaxGun = n.MaxGun;
        rc.GunlukUcret = n.GunlukUcret;
        rc.Doviz = n.Doviz;
        rc.GecerliBas = n.GecerliBas;
        rc.GecerliBit = n.GecerliBit;
        rc.Aktif = n.Aktif;
    }
}
