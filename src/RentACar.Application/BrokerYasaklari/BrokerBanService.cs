using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.BrokerYasaklari;

/// <summary>
/// Broker/kaynak satış yasağı master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma →
/// OperationsWrite. Okuma (<see cref="ListActiveAsync"/>) yetkisiz (rez/kira akışı çağırır). Tenant
/// izolasyonu/audit alt katmanda otomatik. Saf kural-tanım — deftere kayıt postlamaz.
/// </summary>
public sealed class BrokerBanService(IBrokerBanRepository repository, ICurrentUser currentUser,
    IRowVersionStore? rowVersions = null)
{
    private readonly IBrokerBanRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<BrokerYasak>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<IReadOnlyList<BrokerYasak>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<BrokerYasak?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(BrokerYasakInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu broker yasağı zaten var.");

        var row = new BrokerYasak();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, BrokerYasakInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu broker yasağı zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F9.1 — opaque row version for the full-replacement PUT of <c>/api/ui</c>.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersionStoreGuard.Require(rowVersions).GetVersionAsync<BrokerYasak>(id, ct);

    /// <summary>F9.1 — same rules as <see cref="UpdateAsync"/> under a row lock with a version check.</summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, BrokerYasakInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu broker yasağı zaten var.");
        return await RowVersionStoreGuard.Require(rowVersions).UpdateAsync<BrokerYasak>(id, expectedVersion, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, $"'{n.Kod}' kodlu broker yasağı zaten var.", ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(BrokerYasakInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Yasak kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Yasak kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Yasak adı zorunludur.");
        // MinGun bir KISIT ise anlamlı olmalı: 0/negatif "0 gün altı yasak" hiçbir şeyi engellemez (L1).
        if (n.MinGun is int mg && mg < 1) throw new ValidationException("Min gün en az 1 olmalıdır (0/negatif kısıt anlamsız).");
        if (n.GecerlilikBas is { } b && n.GecerlilikBit is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
        if (n.MinGun is null && !n.TumSatisKapali)
            throw new ValidationException("En az bir kısıt tanımlanmalı: Min Gün veya 'Tüm satış kapalı'.");
    }

    private static BrokerYasakInput Normalize(BrokerYasakInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Aciklama = TrimOrNull(input.Aciklama),
        Kaynak = TrimOrNull(input.Kaynak),
        // FAZ-70: çoklu kapsam → CSV. Tekrarlar temizlenir, sıra korunur, kodlar büyük harfe alınır.
        AracGrupKod = Csv(input.AracGrupKod, uppercase: true),
        Bolge = Csv(input.Bolge, uppercase: false),
        MinGun = input.MinGun,
        TumSatisKapali = input.TumSatisKapali,
        GecerlilikBas = input.GecerlilikBas,
        GecerlilikBit = input.GecerlilikBit,
        Aktif = input.Aktif
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// FAZ-70 — çoklu kapsam değerini normalize eder: virgülle ayrılmış girdiden boşları atar,
    /// kırpar, tekrarları (harf duyarsız) temizler, tek bir CSV'ye birleştirir.
    /// Boş/whitespace → null ("bu boyutta kısıt yok" = tümü).
    /// </summary>
    public static string? Csv(string? input, bool uppercase)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        // Tekrar temizliği de Türkçe-duyarlı olmalı: "İzmir" ve "izmir" AYNI değerdir ve
        // OrdinalIgnoreCase bunu göremez → liste iki kez aynı şehri taşırdı.
        var parts = new List<string>();
        foreach (var raw in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var x = uppercase ? raw.ToUpperInvariant() : raw;
            if (!parts.Any(v => TurkishText.EqualsIgnoreTurkishCase(v, x))) parts.Add(x);
        }
        return parts.Count == 0 ? null : string.Join(",", parts);
    }

    /// <summary>
    /// FAZ-70 — CSV kapsam alanı verilen değeri içeriyor mu (harf duyarsız).
    /// Boş/null CSV → <c>false</c>: "kısıt tanımlı değil" ile "her şeyi kapsıyor" AYNI ŞEY DEĞİLDİR;
    /// kapsam boşsa o boyut zaten filtrelenmez, bu metoda hiç sorulmaz.
    ///
    /// <para><b>Bu metodun bu sürümde ÇAĞIRANI YOKTUR — bilinçli.</b> `BrokerYasak` bugün saf bir
    /// tanım tablosu; rezervasyon/kira akışına bağlanması ayrı ve daha büyük bir iştir. Metot,
    /// yukarıdaki <see cref="Csv"/> ile yazılan biçimin OKUMA sözleşmesini sabitler ve testlidir;
    /// bağlama fazı geldiğinde biçim yeniden yorumlanmak zorunda kalmaz.</para>
    /// </summary>
    public static bool IsCovered(string? csv, string? value)
    {
        if (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(value)) return false;
        // TÜRKÇE-DUYARLI karşılaştırma şart: kapsam değerleri şehir/grup adları olabiliyor ve
        // OrdinalIgnoreCase "İzmir" ile "izmir"i EŞİT SAYMAZ (İ = U+0130). Repo genelinde aynı
        // sorun için TurkishText kullanılıyor (araç grubu eşleştirmesi de öyle).
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => TurkishText.EqualsIgnoreTurkishCase(x, value.Trim()));
    }

    private static void Apply(BrokerYasak row, BrokerYasakInput n)
    {
        row.Kod = n.Kod;
        row.Ad = n.Ad;
        row.Aciklama = n.Aciklama;
        row.Kaynak = n.Kaynak;
        row.AracGrupKod = n.AracGrupKod;
        row.Bolge = n.Bolge;
        row.MinGun = n.MinGun;
        row.TumSatisKapali = n.TumSatisKapali;
        row.GecerlilikBas = n.GecerlilikBas;
        row.GecerlilikBit = n.GecerlilikBit;
        row.Aktif = n.Aktif;
    }
}
