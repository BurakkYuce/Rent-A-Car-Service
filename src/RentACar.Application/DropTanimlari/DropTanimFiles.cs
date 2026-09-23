using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.DropTanimlari;

/// <summary>Drop matris kalıcılığı (roadmap N2).</summary>
public interface IDropTanimRepository : IVersionedRepository<DropTanim>
{
    Task<IReadOnlyList<DropTanim>> ListAsync(CancellationToken ct = default);
    /// <summary>FAZ-22 filtreli liste (dönüş lokasyonu / çıkış lokasyonu / şube / durum).</summary>
    Task<IReadOnlyList<DropTanim>> SearchAsync(DropTanimFilter filtre, CancellationToken ct = default);
    Task<DropTanim?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(DropTanim row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<DropTanim> apply, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>Drop tanım liste filtresi (FAZ-22). Boş alan = kısıt yok.</summary>
public sealed class DropTanimFilter
{
    /// <summary>Dönüş lokasyonu (kolon adı geriye uyum için hâlâ "Lokasyon").</summary>
    public string? DonusLokasyon { get; set; }
    public string? CikisLokasyon { get; set; }
    public string? Sube { get; set; }
    /// <summary>null = hepsi; true/false = yalnız aktif/pasif.</summary>
    public bool? Aktif { get; set; }
}

/// <summary>Drop tanım oluştur/güncelle giriş modeli.</summary>
public sealed class DropTanimInput
{
    public string Lokasyon { get; set; } = string.Empty;
    public string Sube { get; set; } = string.Empty;
    public string? KarsilamaSekli { get; set; }
    public string? CalismaSekli { get; set; }
    public string? OzelIletisim { get; set; }
    /// <summary>Drop ücreti — NET, tek seferlik (FAZ 3.A3b).</summary>
    public decimal? Ucret { get; set; }

    // ---- FAZ-22 ----
    public string? CikisLokasyon { get; set; }
    public int? MinGun { get; set; }
    public int? ManSuresi { get; set; }
    /// <summary>Bilgi alanı — hesaba GİRMEZ (bkz. DropTanim.Drop2).</summary>
    public decimal? Drop2 { get; set; }

    public bool Aktif { get; set; } = true;
}

/// <summary>Lokasyon-şube drop matris master iş mantığı (roadmap N2). Yazma OperationsWrite.</summary>
public sealed class DropTanimService(IDropTanimRepository repository, ICurrentUser currentUser)
{
    private readonly IDropTanimRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<DropTanim>> ListAsync(CancellationToken ct = default) => _repository.ListAsync(ct);

    /// <summary>FAZ-22 filtreli liste. Filtre yalnız GÖRÜNÜMÜ daraltır; ücret motoru bundan etkilenmez.</summary>
    public Task<IReadOnlyList<DropTanim>> SearchAsync(DropTanimFilter? filtre = null, CancellationToken ct = default)
        => _repository.SearchAsync(filtre ?? new DropTanimFilter(), ct);
    public Task<DropTanim?> GetAsync(Guid id, CancellationToken ct = default) => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(DropTanimInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Sayilar(input);
        var row = new DropTanim
        {
            Lokasyon = n.Lokasyon, Sube = n.Sube,
            KarsilamaSekli = n.KarsilamaSekli, CalismaSekli = n.CalismaSekli, OzelIletisim = n.OzelIletisim,
            Ucret = input.Ucret,
            CikisLokasyon = n.CikisLokasyon, MinGun = input.MinGun, ManSuresi = input.ManSuresi, Drop2 = input.Drop2,
            Aktif = input.Aktif
        };
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public Task<bool> UpdateAsync(Guid id, DropTanimInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (stale version → 409 <c>cakisma</c>).</summary>
    public Task<bool> UpdateAsync(Guid id, DropTanimInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion, ct);

    /// <summary>F11.1a — opaque row version.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row.</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => _repository.GetVersionsAsync(ct);

    private async Task<bool> UpdateCoreAsync(Guid id, DropTanimInput input, string? expectedVersion, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Sayilar(input);
        void Update(DropTanim r)
        {
            r.Lokasyon = n.Lokasyon; r.Sube = n.Sube;
            r.KarsilamaSekli = n.KarsilamaSekli; r.CalismaSekli = n.CalismaSekli; r.OzelIletisim = n.OzelIletisim;
            r.Ucret = input.Ucret;
            // Normalize kopya kurucudur — yeni alan BURAYA da yazılmalı (BelgeSablon dersi).
            r.CikisLokasyon = n.CikisLokasyon; r.MinGun = input.MinGun;
            r.ManSuresi = input.ManSuresi; r.Drop2 = input.Drop2;
            r.Aktif = input.Aktif; r.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        return expectedVersion is null
            ? await _repository.UpdateAsync(id, Update, ct)
            : await _repository.UpdateAsync(id, expectedVersion, Update, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.DeleteAsync(id, ct);
    }

    /// <summary>Sayısal alan doğrulaması — negatif ücret/gün/süre reddedilir.</summary>
    private static void Sayilar(DropTanimInput i)
    {
        if (i.Ucret is < 0m) throw new ValidationException("Drop ücreti negatif olamaz.");
        if (i.Drop2 is < 0m) throw new ValidationException("Drop 2 negatif olamaz.");
        if (i.MinGun is < 1) throw new ValidationException("Asgari gün en az 1 olmalıdır.");
        if (i.ManSuresi is < 0) throw new ValidationException("Karşılama süresi negatif olamaz.");
    }

    private static (string Lokasyon, string Sube, string? KarsilamaSekli, string? CalismaSekli,
        string? OzelIletisim, string? CikisLokasyon) Normalize(DropTanimInput i)
    {
        var lok = (i.Lokasyon ?? "").Trim();
        var sube = (i.Sube ?? "").Trim();
        if (string.IsNullOrWhiteSpace(lok)) throw new ValidationException("Lokasyon zorunludur.");
        if (string.IsNullOrWhiteSpace(sube)) throw new ValidationException("Şube zorunludur.");
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        return (lok, sube, T(i.KarsilamaSekli), T(i.CalismaSekli), T(i.OzelIletisim), T(i.CikisLokasyon));
    }
}
