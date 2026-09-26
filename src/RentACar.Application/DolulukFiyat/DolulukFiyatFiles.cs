using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.DolulukFiyat;

public interface IOccupancyPriceRuleRepository : IVersionedRepository<DolulukFiyatKural>
{
    Task<IReadOnlyList<DolulukFiyatKural>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DolulukFiyatKural>> ListActiveAsync(CancellationToken ct = default);
    Task<DolulukFiyatKural?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(DolulukFiyatKural row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<DolulukFiyatKural> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Grup doluluk yüzdesi sağlayıcısı (FAZ 3.A7). Pencere gün sayısı BİTİŞ-HARİÇ gün farkıdır
/// (ComputeGun ile hizalı: 5 günlük kira penceresi = 5 araç-gün/araç); grupta araç yoksa null
/// (0 araçlı grupta %0/%100 anlamsız — surge tetiklenmez).</summary>
public interface IOccupancyProvider
{
    Task<decimal?> GetGroupOccupancyPercentAsync(
        string groupCode, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Doluluk fiyat kuralı giriş modeli.</summary>
public sealed class DolulukFiyatKuralInput
{
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public string? AracGrupKod { get; set; }
    public int EsikYuzde { get; set; }
    public decimal CarpanYuzde { get; set; }
    /// <summary>FAZ-73 — şube (yalnız <see cref="SadeceKendiSubeleri"/> ile birlikte anlamlı).</summary>
    public string? Sube { get; set; }
    /// <summary>FAZ-73 — true: çarpan yalnız <see cref="Sube"/> şubesinden çıkan kiralara uygulanır.</summary>
    public bool SadeceKendiSubeleri { get; set; }
    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }
    public bool Aktif { get; set; } = true;
}

/// <summary>
/// FAZ-73 — "aynı grup için 10 kademeyi tek formda gir" toplu-giriş girdisi (canlı
/// doluluk_algoritma.aspx). Ortak kapsam (grup/şube/geçerlilik/bayrak) bir kez verilir, kademeler
/// eşik+çarpan çifti olarak listelenir; kod ortak ÖN EKTEN türetilir (<c>{OnEk}-{Esik}</c>).
/// Entity/motor DEĞİŞMEZ — bu yalnız N adet tekil <c>CreateAsync</c>'in giriş kısayoludur.
/// </summary>
public sealed class DolulukTopluInput
{
    /// <summary>Kod ön eki — üretilen kod <c>{KodOnEk}-{EsikYuzde}</c> (ör. "YAZ-EKO-80").</summary>
    public string KodOnEk { get; set; } = string.Empty;
    /// <summary>Ad ön eki — üretilen ad <c>{AdOnEk} %{EsikYuzde}</c>.</summary>
    public string AdOnEk { get; set; } = string.Empty;
    public string? AracGrupKod { get; set; }
    public string? Sube { get; set; }
    public bool SadeceKendiSubeleri { get; set; }
    public DateTimeOffset? GecerlilikBas { get; set; }
    public DateTimeOffset? GecerlilikBit { get; set; }
    public bool Aktif { get; set; } = true;
    /// <summary>Kademeler: (eşik %, çarpan %). Boş satırlar (ikisi de null) çağıran tarafından atılır.</summary>
    public IReadOnlyList<DolulukKademeSatiri> Kademeler { get; set; } = [];
}

/// <summary>Toplu-giriş tek kademe satırı.</summary>
public sealed record DolulukKademeSatiri(int EsikYuzde, decimal CarpanYuzde);

/// <summary>Doluluk fiyat kuralı master iş mantığı (FAZ 3.A7). Yazma OperationsWrite; Esik 1..100,
/// Carpan 0..50 (DB CHECK ile çift savunma).</summary>
public sealed class OccupancyPriceRuleService(IOccupancyPriceRuleRepository repository, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<DolulukFiyatKural>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);

    public async Task<Guid> CreateAsync(DolulukFiyatKuralInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await repository.CodeExistsAsync(n.Kod, null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu doluluk kuralı zaten var.");
        var row = new DolulukFiyatKural();
        Apply(row, n);
        await repository.CreateAsync(row, ct);
        return row.Id;
    }

    public Task<bool> UpdateAsync(Guid id, DolulukFiyatKuralInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (stale version → 409 <c>cakisma</c>).</summary>
    public Task<bool> UpdateAsync(Guid id, DolulukFiyatKuralInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion, ct);

    /// <summary>F11.1a — single rule (the API detail/PUT path; tenant-scoped by RLS).</summary>
    public Task<DolulukFiyatKural?> GetAsync(Guid id, CancellationToken ct = default) => repository.FindAsync(id, ct);

    /// <summary>F11.1a — opaque row version.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row.</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => repository.GetVersionsAsync(ct);

    private async Task<bool> UpdateCoreAsync(Guid id, DolulukFiyatKuralInput input, string? expectedVersion, CancellationToken ct)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await repository.CodeExistsAsync(n.Kod, id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu doluluk kuralı zaten var.");
        void Update(DolulukFiyatKural r)
        {
            Apply(r, n);
            r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        return expectedVersion is null
            ? await repository.UpdateAsync(id, Update, ct)
            : await repository.UpdateAsync(id, expectedVersion, Update, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);
        return repository.DeleteAsync(id, ct);
    }

    /// <summary>
    /// FAZ-73 — toplu kademe girişi: ortak kapsam + N kademe → N adet tekil kural.
    ///
    /// <para><b>ÖNCE TÜMÜ DOĞRULANIR, SONRA YAZILIR:</b> satır satır yazıp ortada patlamak yarım bir
    /// kademe merdiveni bırakırdı (kullanıcı 10 kademe girdiğini sanırken motorda 4 kademe olurdu).
    /// Doğrulama; eşik/çarpan sınırları, form içi tekrar eden eşik ve mevcut kodla çakışma dahil.</para>
    ///
    /// <para>Yazma tekil <see cref="CreateAsync"/>'in aynı yolundan geçer (aynı normalize, aynı
    /// benzersizlik, aynı guard) — ikinci bir yazma semantiği doğmaz.</para>
    /// </summary>
    public async Task<IReadOnlyList<Guid>> BulkCreateAsync(DolulukTopluInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.OperationsWrite);

        var prefix = (input.KodOnEk ?? string.Empty).Trim().ToUpperInvariant();
        if (prefix.Length == 0) throw new ValidationException("Kod ön eki zorunludur.");
        var namePrefix = (input.AdOnEk ?? string.Empty).Trim();
        if (namePrefix.Length == 0) throw new ValidationException("Ad ön eki zorunludur.");
        if (input.Kademeler.Count == 0) throw new ValidationException("En az bir kademe (eşik + çarpan) girilmelidir.");

        var inputs = new List<DolulukFiyatKuralInput>();
        var seenThreshold = new HashSet<int>();
        foreach (var k in input.Kademeler)
        {
            if (!seenThreshold.Add(k.EsikYuzde))
                throw new ValidationException($"%{k.EsikYuzde} eşiği formda birden çok kez girilmiş; her kademe tek olmalı.");

            var inputItem = Normalize(new DolulukFiyatKuralInput
            {
                Kod = $"{prefix}-{k.EsikYuzde}",
                Ad = $"{namePrefix} %{k.EsikYuzde}",
                AracGrupKod = input.AracGrupKod,
                EsikYuzde = k.EsikYuzde,
                CarpanYuzde = k.CarpanYuzde,
                Sube = input.Sube,
                SadeceKendiSubeleri = input.SadeceKendiSubeleri,
                GecerlilikBas = input.GecerlilikBas,
                GecerlilikBit = input.GecerlilikBit,
                Aktif = input.Aktif
            });
            Validate(inputItem);
            if (inputItem.Kod.Length > 32)
                throw new ValidationException($"'{inputItem.Kod}' kodu 32 karakteri aşıyor; ön eki kısaltın.");
            if (await repository.CodeExistsAsync(inputItem.Kod, null, ct))
                throw new ValidationException($"'{inputItem.Kod}' kodlu doluluk kuralı zaten var; hiçbir kademe yazılmadı.");
            inputs.Add(inputItem);
        }

        var ids = new List<Guid>(inputs.Count);
        foreach (var inputItem in inputs)
        {
            var row = new DolulukFiyatKural();
            Apply(row, inputItem);
            await repository.CreateAsync(row, ct);   // DB unique index son savunma (yarış durumu)
            ids.Add(row.Id);
        }
        return ids;
    }

    private static void Validate(DolulukFiyatKuralInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Kural kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Kural kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Kural adı zorunludur.");
        if (n.EsikYuzde is < 1 or > 100) throw new ValidationException("Doluluk eşiği %1 ile %100 arasında olmalıdır.");
        if (n.CarpanYuzde is < 0m or > 50m) throw new ValidationException("Fiyat çarpanı %0 ile %50 arasında olmalıdır (sert tavan).");
        // FAZ-73: şubesiz "sadece kendi şubesi" kuralı HİÇBİR teklife uymaz (SurgeSubeUyar false döner)
        // → sessizce ölü bir kural olurdu. Kullanıcı yanılmasın diye giriş noktasında reddedilir.
        if (n.SadeceKendiSubeleri && string.IsNullOrWhiteSpace(n.Sube))
            throw new ValidationException("'Sadece kendi şubeleri' işaretliyse şube seçilmelidir.");
        if (n.GecerlilikBas is { } b && n.GecerlilikBit is { } t && t < b)
            throw new ValidationException("Geçerlilik bitişi başlangıçtan önce olamaz.");
    }

    private static DolulukFiyatKuralInput Normalize(DolulukFiyatKuralInput i) => new()
    {
        Kod = (i.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (i.Ad ?? string.Empty).Trim(),
        AracGrupKod = string.IsNullOrWhiteSpace(i.AracGrupKod) ? null : i.AracGrupKod.Trim().ToUpperInvariant(),
        EsikYuzde = i.EsikYuzde,
        CarpanYuzde = i.CarpanYuzde,
        Sube = string.IsNullOrWhiteSpace(i.Sube) ? null : i.Sube.Trim(),
        SadeceKendiSubeleri = i.SadeceKendiSubeleri,
        GecerlilikBas = i.GecerlilikBas,
        GecerlilikBit = i.GecerlilikBit,
        Aktif = i.Aktif
    };

    private static void Apply(DolulukFiyatKural r, DolulukFiyatKuralInput n)
    {
        r.Kod = n.Kod; r.Ad = n.Ad; r.AracGrupKod = n.AracGrupKod;
        r.EsikYuzde = n.EsikYuzde; r.CarpanYuzde = n.CarpanYuzde;
        r.Sube = n.Sube; r.SadeceKendiSubeleri = n.SadeceKendiSubeleri;
        r.GecerlilikBas = n.GecerlilikBas; r.GecerlilikBit = n.GecerlilikBit;
        r.Aktif = n.Aktif;
    }
}
