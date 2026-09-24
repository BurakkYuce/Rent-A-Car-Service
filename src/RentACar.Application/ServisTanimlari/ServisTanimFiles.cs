using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.ServisTanimlari;

/// <summary>Periyodik bakım tanım kalıcılığı (roadmap N1).</summary>
public interface IServisTanimRepository
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
    Task<IReadOnlyList<FiloKombinasyon>> FiloKombinasyonlariAsync(CancellationToken ct = default);
}

/// <summary>Filodaki bir araç kombinasyonu ve o kombinasyondaki araç adedi.</summary>
public sealed record FiloKombinasyon(string? Marka, string? Tip, string? Yakit, string? Vites, int AracSayisi)
{
    /// <summary>Karşılaştırma anahtarı — boş alanlar da anlamlıdır (bilgi girilmemiş araç grubu).</summary>
    public string Anahtar => ServisTanimKombinasyon.Anahtar(Marka, Tip, Yakit, Vites);

    public string Etiket => string.Join(" · ",
        new[] { Marka, Tip, Yakit, Vites }.Where(x => !string.IsNullOrWhiteSpace(x)))
        is { Length: > 0 } s ? s : "(bilgisiz)";
}

/// <summary>Öneri satırı: filoda VAR ama tanımı OLMAYAN kombinasyon.</summary>
public sealed record ServisTanimOneri(FiloKombinasyon Kombinasyon, string OnerilenKod);

/// <summary>Kombinasyon anahtarı — servis, repo ve test AYNI kuralı kullansın diye tek yerde.</summary>
public static class ServisTanimKombinasyon
{
    /// <summary>Büyük/küçük harf ve boşluk duyarsız; Türkçe karakterler korunur.</summary>
    public static string Anahtar(string? marka, string? tip, string? yakit, string? vites)
        => string.Join("|", new[] { marka, tip, yakit, vites }
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
public sealed class ServisTanimService(IServisTanimRepository repository, ICurrentUser currentUser,
    IRowVersionStore? rowVersions = null)
{
    private readonly IServisTanimRepository _repository = repository;
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
    public async Task<IReadOnlyList<ServisTanimOneri>> OneriAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var filo = await _repository.FiloKombinasyonlariAsync(ct);
        var mevcut = await _repository.ListAsync(ct);

        // Eşleşme yalnız KOMBİNASYON kolonları üzerinden; eski (kombinasyonsuz) tanımlar hiçbir
        // kombinasyonu "kapsamış" saymaz — aksi hâlde tek bir eski satır tüm önerileri susturur.
        var kapsanan = mevcut
            .Where(t => t.Marka != null || t.Tip != null || t.Yakit != null || t.Vites != null)
            .Select(t => ServisTanimKombinasyon.Anahtar(t.Marka, t.Tip, t.Yakit, t.Vites))
            .ToHashSet(StringComparer.Ordinal);

        var kullanilanKod = mevcut.Select(t => t.Kod).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sonuc = new List<ServisTanimOneri>();
        foreach (var k in filo.Where(k => !kapsanan.Contains(k.Anahtar)))
            sonuc.Add(new ServisTanimOneri(k, TekilKod(k, kullanilanKod)));
        return sonuc;
    }

    /// <summary>Kombinasyondan kod türetir; çakışırsa sonuna sayı ekler (üretilen kod da rezerve edilir).</summary>
    private static string TekilKod(FiloKombinasyon k, HashSet<string> kullanilan)
    {
        var ham = string.Concat(new[] { k.Marka, k.Tip, k.Yakit, k.Vites }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new string(x!.Trim().Where(char.IsLetterOrDigit).Take(4).ToArray())));
        var taban = (string.IsNullOrWhiteSpace(ham) ? "SRV" : ham).ToUpperInvariant();
        if (taban.Length > 28) taban = taban[..28];

        var aday = taban;
        for (var i = 2; kullanilan.Contains(aday); i++) aday = $"{taban}{i}";
        kullanilan.Add(aday);
        return aday;
    }

    private static (string Kod, string AracTipi, string? Aciklama, string? Marka, string? Tip, string? Yakit, string? Vites)
        Normalize(ServisTanimInput i)
    {
        var kod = (i.Kod ?? "").Trim().ToUpperInvariant();
        var tip = (i.AracTipi ?? "").Trim();
        if (string.IsNullOrWhiteSpace(kod)) throw new ValidationException("Kod zorunludur.");
        if (kod.Length > 32) throw new ValidationException("Kod en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(tip)) throw new ValidationException("Araç tipi zorunludur.");
        if (i.BakimKm < 0) throw new ValidationException("Bakım KM negatif olamaz.");
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return (kod, tip, T(i.Aciklama), T(i.Marka), T(i.Tip), T(i.Yakit), T(i.Vites));
    }
}
