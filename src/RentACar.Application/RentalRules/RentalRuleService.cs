using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.RentalRules;

/// <summary>
/// Kiralama kuralı master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma → OperationsWrite.
/// Okuma (<see cref="ListActiveAsync"/>) yetkisiz (fiyat motoru/form çağırır). Tenant izolasyonu/audit
/// alt katmanda otomatik. Saf kural-tanım — deftere kayıt postlamaz.
/// </summary>
public sealed class RentalRuleService(IRentalRuleRepository repository, ICurrentUser currentUser)
{
    private readonly IRentalRuleRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<RentalRule>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<RentalRule>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<RentalRule?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(RentalRuleInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu kiralama kuralı zaten var.");

        var row = new RentalRule();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, RentalRuleInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu kiralama kuralı zaten var.");

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

    private static void Validate(RentalRuleInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Kural kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Kural kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Kural adı zorunludur.");
        if (n.MinGun is < 0) throw new ValidationException("Min gün negatif olamaz.");
        if (n.MaxGun is < 0) throw new ValidationException("Max gün negatif olamaz.");
        if (n.MinGun is { } mn && n.MaxGun is { } mx && mx < mn)
            throw new ValidationException("Max gün, min günden küçük olamaz.");
        if (n.HediyeGun is < 0) throw new ValidationException("Hediye gün negatif olamaz.");
        if (n.Iskonto is < 0m or > 100m) throw new ValidationException("İskonto oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.SonraOdeOran is < 0m or > 100m) throw new ValidationException("Sonra öde oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.GecerlilikBas is { } b && n.GecerlilikBit is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
    }

    private static RentalRuleInput Normalize(RentalRuleInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Kanal = TrimOrNull(input.Kanal),
        Sube = TrimOrNull(input.Sube),
        AracGrupKod = string.IsNullOrWhiteSpace(input.AracGrupKod) ? null : input.AracGrupKod.Trim().ToUpperInvariant(),
        MinGun = input.MinGun,
        MaxGun = input.MaxGun,
        Iskonto = input.Iskonto,
        SonraOdeOran = input.SonraOdeOran,
        HediyeGun = input.HediyeGun,
        KampanyaMi = input.KampanyaMi,
        KampanyaKodu = TrimOrNull(input.KampanyaKodu),
        GecerlilikBas = input.GecerlilikBas,
        GecerlilikBit = input.GecerlilikBit,
        SartMetni = TrimOrNull(input.SartMetni),
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(RentalRule row, RentalRuleInput n)
    {
        row.Kod = n.Kod;
        row.Ad = n.Ad;
        row.Aciklama = n.Aciklama;
        row.Kanal = n.Kanal;
        row.Sube = n.Sube;
        row.AracGrupKod = n.AracGrupKod;
        row.MinGun = n.MinGun;
        row.MaxGun = n.MaxGun;
        row.Iskonto = n.Iskonto;
        row.SonraOdeOran = n.SonraOdeOran;
        row.HediyeGun = n.HediyeGun;
        row.KampanyaMi = n.KampanyaMi;
        row.KampanyaKodu = n.KampanyaKodu;
        row.GecerlilikBas = n.GecerlilikBas;
        row.GecerlilikBit = n.GecerlilikBit;
        row.SartMetni = n.SartMetni;
        row.Aktif = n.Aktif;
    }
}
