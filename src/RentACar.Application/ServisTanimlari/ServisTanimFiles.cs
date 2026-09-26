using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ServisTanimlari;

/// <summary>Periyodik bakım tanım kalıcılığı (roadmap N1).</summary>
public interface IServiceDefinitionRepository
{
    Task<IReadOnlyList<ServisTanim>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ServisTanim>> ListActiveAsync(CancellationToken ct = default);
    Task<ServisTanim?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(ServisTanim row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<ServisTanim> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// FAZ-14 C — filodaki GERÇEK (Marka, Tip, Yakıt, Vites) kombinasyonları + her birinin araç
    /// adedi. Kaynak <c>Vehicles</c>; tanım tablosuna bakmaz (eşleştirme serviste yapılır).
    /// </summary>
    Task<IReadOnlyList<FiloKombinasyon>> FleetCombinationsAsync(CancellationToken ct = default);
}

/// <summary>Filodaki bir araç kombinasyonu ve o kombinasyondaki araç adedi.</summary>
public sealed record FiloKombinasyon(string? Marka, string? Tip, string? Yakit, string? Vites, int AracSayisi)
{
    /// <summary>Karşılaştırma anahtarı — boş alanlar da anlamlıdır (bilgi girilmemiş araç grubu).</summary>
    public string Anahtar => ServiceDefinitionCombination.Key(Marka, Tip, Yakit, Vites);

    public string Etiket => string.Join(" · ",
        new[] { Marka, Tip, Yakit, Vites }.Where(x => !string.IsNullOrWhiteSpace(x)))
        is { Length: > 0 } s ? s : "(bilgisiz)";
}

/// <summary>Öneri satırı: filoda VAR ama tanımı OLMAYAN kombinasyon.</summary>
public sealed record ServisTanimOneri(FiloKombinasyon Kombinasyon, string OnerilenKod);

/// <summary>Kombinasyon anahtarı — servis, repo ve test AYNI kuralı kullansın diye tek yerde.</summary>
public static class ServiceDefinitionCombination
{
    /// <summary>Büyük/küçük harf ve boşluk duyarsız; Türkçe karakterler korunur.</summary>
    public static string Key(string? brand, string? tip, string? fuel, string? transmission)
        => string.Join("|", new[] { brand, tip, fuel, transmission }
            .Select(x => (x ?? "").Trim().ToUpperInvariant()));
}

/// <summary>Servis tanım oluştur/güncelle giriş modeli.</summary>
public sealed class ServisTanimInput
{
    public string Kod { get; set; } = string.Empty;
    public string AracTipi { get; set; } = string.Empty;
    public int BakimKm { get; set; }
    public string? Marka { get; set; }
    public string? Tip { get; set; }
    public string? Yakit { get; set; }
    public string? Vites { get; set; }
    public string? Aciklama { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>Periyodik bakım tanım master iş mantığı (roadmap N1). Yazma OperationsWrite.</summary>
public sealed class ServiceDefinitionService(IServiceDefinitionRepository repository, ICurrentUser currentUser,
    IRowVersionStore? rowVersions = null)
{
    private readonly IServiceDefinitionRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<ServisTanim>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);
    public Task<IReadOnlyList<ServisTanim>> ListActiveAsync(CancellationToken ct = default) => _repository.ListActiveAsync(ct);
    public Task<ServisTanim?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(ServisTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        var row = new ServisTanim
        {
            Kod = n.Kod, AracTipi = n.AracTipi, BakimKm = input.BakimKm, Aciklama = n.Aciklama, Aktif = input.Aktif,
            Marka = n.Marka, Tip = n.Tip, Yakit = n.Yakit, Vites = n.Vites
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, ServisTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        return await _repository.UpdateAsync(id, r =>
        {
            r.Kod = n.Kod; r.AracTipi = n.AracTipi; r.BakimKm = input.BakimKm; r.Aciklama = n.Aciklama; r.Aktif = input.Aktif;
            // Normalize KOPYA KURUCU DEĞİL: yeni alan eklendiğinde BURAYA da yazılmalı, yoksa
            // kullanıcının girdiği değer sessizce düşer (BelgeSablon dersi).
            r.Marka = n.Marka; r.Tip = n.Tip; r.Yakit = n.Yakit; r.Vites = n.Vites;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F9.1 — opaque row version for the full-replacement PUT of <c>/api/ui</c>.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default)
        => RowVersionStoreGuard.Require(rowVersions).GetVersionAsync<ServisTanim>(id, ct);

    /// <summary>F9.1 — same rules as <see cref="UpdateAsync"/> under a row lock with a version check.</summary>
    public async Task<bool> UpdateVersionedAsync(Guid id, ServisTanimInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        return await RowVersionStoreGuard.Require(rowVersions).UpdateAsync<ServisTanim>(id, expectedVersion, r =>
        {
            r.Kod = n.Kod; r.AracTipi = n.AracTipi; r.BakimKm = input.BakimKm; r.Aciklama = n.Aciklama; r.Aktif = input.Aktif;
            r.Marka = n.Marka; r.Tip = n.Tip; r.Yakit = n.Yakit; r.Vites = n.Vites;
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, $"'{n.Kod}' kodlu servis tanımı zaten var.", ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    /// <summary>
    /// FAZ-14 C — filoda VAR ama tanımı OLMAYAN kombinasyonlar. Kod önerisi kombinasyon
    /// harflerinden türetilir ve MEVCUT kodlarla çakışmayacak şekilde tekilleştirilir; kullanıcı
    /// yine de düzenleyebilir. ÖNERİ HİÇBİR ŞEY YAZMAZ — kabul ayrı bir adımdır.
    /// </summary>
    public async Task<IReadOnlyList<ServisTanimOneri>> SuggestionAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var fleet = await _repository.FleetCombinationsAsync(ct);
        var existing = await _repository.ListAsync(ct);

        // Eşleşme yalnız KOMBİNASYON kolonları üzerinden; eski (kombinasyonsuz) tanımlar hiçbir
        // kombinasyonu "kapsamış" saymaz — aksi hâlde tek bir eski satır tüm önerileri susturur.
        var covered = existing
            .Where(t => t.Marka != null || t.Tip != null || t.Yakit != null || t.Vites != null)
            .Select(t => ServiceDefinitionCombination.Key(t.Marka, t.Tip, t.Yakit, t.Vites))
            .ToHashSet(StringComparer.Ordinal);

        var usedCode = existing.Select(t => t.Kod).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<ServisTanimOneri>();
        foreach (var k in fleet.Where(k => !covered.Contains(k.Anahtar)))
            result.Add(new ServisTanimOneri(k, UniqueCode(k, usedCode)));
        return result;
    }

    /// <summary>Kombinasyondan kod türetir; çakışırsa sonuna sayı ekler (üretilen kod da rezerve edilir).</summary>
    private static string UniqueCode(FiloKombinasyon k, HashSet<string> used)
    {
        var raw = string.Concat(new[] { k.Marka, k.Tip, k.Yakit, k.Vites }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new string(x!.Trim().Where(char.IsLetterOrDigit).Take(4).ToArray())));
        var floor = (string.IsNullOrWhiteSpace(raw) ? "SRV" : raw).ToUpperInvariant();
        if (floor.Length > 28) floor = floor[..28];

        var candidate = floor;
        for (var i = 2; used.Contains(candidate); i++) candidate = $"{floor}{i}";
        used.Add(candidate);
        return candidate;
    }

    private static (string Kod, string AracTipi, string? Aciklama, string? Marka, string? Tip, string? Yakit, string? Vites)
        Normalize(ServisTanimInput i)
    {
        var code = (i.Kod ?? "").Trim().ToUpperInvariant();
        var tip = (i.AracTipi ?? "").Trim();
        if (string.IsNullOrWhiteSpace(code)) throw new ValidationException("Kod zorunludur.");
        if (code.Length > 32) throw new ValidationException("Kod en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(tip)) throw new ValidationException("Araç tipi zorunludur.");
        if (i.BakimKm < 0) throw new ValidationException("Bakım KM negatif olamaz.");
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return (code, tip, T(i.Aciklama), T(i.Marka), T(i.Tip), T(i.Yakit), T(i.Vites));
    }
}
