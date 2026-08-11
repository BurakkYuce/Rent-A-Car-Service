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
        await KampanyaKoduBenzersizAsync(n.KampanyaKodu, excludeId: null, ct);

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
        await KampanyaKoduBenzersizAsync(n.KampanyaKodu, excludeId: id, ct);

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

    /// <summary>FAZ 3.A5 adversarial B4: aynı KampanyaKodu iki kuralda olursa KodluKuralSec keyfi/yanlış
    /// seçer — kod tenant içinde TEK kurala ait olmalı (case-insensitive; boş kod serbest).</summary>
    private async Task KampanyaKoduBenzersizAsync(string? kod, Guid? excludeId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kod)) return;
        var hepsi = await _repository.ListAsync(ct);
        if (hepsi.Any(r => r.Id != excludeId && !string.IsNullOrWhiteSpace(r.KampanyaKodu) &&
                string.Equals(r.KampanyaKodu.Trim(), kod.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException($"'{kod}' kampanya kodu başka bir kuralda kullanılıyor (kod tek kurala ait olmalı).");
    }

    private static void Validate(RentalRuleInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Kural kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Kural kodu en çok 32 karakter olabilir.");
        if (n.KampanyaKodu is { Length: > 64 }) throw new ValidationException("Kampanya kodu en çok 64 karakter olabilir.");
        if (n.MusteriSegment is { Length: > 64 }) throw new ValidationException("Müşteri segmenti en çok 64 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Kural adı zorunludur.");
        if (n.MinGun is < 0) throw new ValidationException("Min gün negatif olamaz.");
        if (n.MaxGun is < 0) throw new ValidationException("Max gün negatif olamaz.");
        if (n.MinGun is { } mn && n.MaxGun is { } mx && mx < mn)
            throw new ValidationException("Max gün, min günden küçük olamaz.");
        if (n.HediyeGun is < 0) throw new ValidationException("Hediye gün negatif olamaz.");
        if (n.Iskonto is < 0m or > 100m) throw new ValidationException("İskonto oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.HaftaSonuFarkOran is < 0m or > 200m) throw new ValidationException("Hafta sonu fark oranı 0 ile 200 arasında olmalıdır (%).");
        if (n.SonraOdeOran is < 0m or > 100m) throw new ValidationException("Sonra öde oranı 0 ile 100 arasında olmalıdır (%).");
        if (n.GecerlilikBas is { } b && n.GecerlilikBit is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
        // FAZ-46 — talep aralığı GEÇERLİLİK aralığından ayrı bir kavram; kendi tutarlılığı sınanır.
        if (n.TalepBas is { } tb && n.TalepBit is { } tt && tt < tb)
            throw new ValidationException("Talep bitişi başlangıçtan önce olamaz.");
    }

    /// <summary>
    /// FAZ-46 — "0,6" gibi haftanın-günü listesini normalize eder: 0-6 dışı ya da sayı olmayan
    /// değer GÜRÜLTÜLÜ reddedilir (sessizce atmak, kullanıcının kurduğunu sandığı kısıtı yok
    /// ederdi). Tekrarlar ayıklanır, sıralanır; boş sonuç null döner (kısıt yok).
    /// Saf fonksiyon — ekran ve servis AYNI kuralı kullansın diye public.
    /// </summary>
    public static string? HaftaGunNormalize(string? ham)
    {
        if (string.IsNullOrWhiteSpace(ham)) return null;
        var gunler = new SortedSet<int>();
        foreach (var parca in ham.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(parca, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var g) || g is < 0 or > 6)
                throw new ValidationException($"Geçersiz hafta günü: '{parca}'. 0 (Pazar) ile 6 (Cumartesi) arası olmalıdır.");
            gunler.Add(g);
        }
        return gunler.Count == 0 ? null : string.Join(',', gunler);
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
        HaftaSonuFarkOran = input.HaftaSonuFarkOran,
        SonraOdeOran = input.SonraOdeOran,
        HediyeGun = input.HediyeGun,
        KampanyaMi = input.KampanyaMi,
        KampanyaKodu = TrimOrNull(input.KampanyaKodu),
        MusteriSegment = TrimOrNull(input.MusteriSegment),
        GecerlilikBas = input.GecerlilikBas,
        GecerlilikBit = input.GecerlilikBit,
        SartMetni = TrimOrNull(input.SartMetni),
        // FAZ-46 — Normalize YENİ nesne kurar: buraya eklenmeyen alan SESSİZCE kaybolur
        // (repoda bilinen tuzak). Yeni alan eklerken Apply ile birlikte İKİSİNE de yaz.
        TalepBas = input.TalepBas,
        TalepBit = input.TalepBit,
        PromosyonTuru = input.PromosyonTuru,
        KuponGecerlilik = input.KuponGecerlilik,
        HesaplamaTipi = input.HesaplamaTipi,
        HizliIslem = input.HizliIslem,
        HaftaGunKisiti = HaftaGunNormalize(input.HaftaGunKisiti),
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
        row.HaftaSonuFarkOran = n.HaftaSonuFarkOran;
        row.SonraOdeOran = n.SonraOdeOran;
        row.HediyeGun = n.HediyeGun;
        row.KampanyaMi = n.KampanyaMi;
        row.KampanyaKodu = n.KampanyaKodu;
        row.MusteriSegment = n.MusteriSegment;
        row.GecerlilikBas = n.GecerlilikBas;
        row.GecerlilikBit = n.GecerlilikBit;
        row.SartMetni = n.SartMetni;
        row.TalepBas = n.TalepBas;                    // FAZ-46
        row.TalepBit = n.TalepBit;
        row.PromosyonTuru = n.PromosyonTuru;
        row.KuponGecerlilik = n.KuponGecerlilik;
        row.HesaplamaTipi = n.HesaplamaTipi;
        row.HizliIslem = n.HizliIslem;
        row.HaftaGunKisiti = n.HaftaGunKisiti;
        row.Aktif = n.Aktif;
    }
}
