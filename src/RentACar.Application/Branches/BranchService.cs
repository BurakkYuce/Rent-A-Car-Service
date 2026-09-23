using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Branches;

/// <summary>
/// Şube master iş mantığı: doğrulama + kod benzersizliği + CRUD. Şube yönetimi yönetsel
/// yapılandırmadır → <see cref="Permission.ManageUsers"/> (Admin) ile korunur. Tenant
/// izolasyonu ve audit alt katmanda otomatik.
/// </summary>
public sealed class BranchService(
    IBranchRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly IBranchRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;

    public Task<IReadOnlyList<Branch>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>Açılır liste kaynağı (yalnız aktif). Okuma — yetki gerektirmez.</summary>
    public Task<IReadOnlyList<Branch>> ListActiveAsync(CancellationToken ct = default)
        => _repository.ListActiveAsync(ct);

    public Task<Branch?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(BranchInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu şube zaten var.");

        var branch = new Branch();
        Apply(branch, n);
        await _repository.CreateAsync(branch, ct);
        return branch.Id;
    }

    public Task<bool> UpdateAsync(Guid id, BranchInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (stale version → 409 <c>cakisma</c>).</summary>
    public Task<bool> UpdateAsync(Guid id, BranchInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion, ct);

    /// <summary>F11.1a — opaque row version.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row.</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => _repository.GetVersionsAsync(ct);

    private async Task<bool> UpdateCoreAsync(Guid id, BranchInput input, string? expectedVersion, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu şube zaten var.");

        void Update(Branch b)
        {
            Apply(b, n);
            b.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        return expectedVersion is null
            ? await _repository.UpdateAsync(id, Update, ct)
            : await _repository.UpdateAsync(id, expectedVersion, Update, ct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        return _repository.DeleteAsync(id, ct);
    }

    private static void Validate(BranchInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod))
            throw new ValidationException("Şube kodu zorunludur.");
        if (n.Kod.Length > 32)
            throw new ValidationException("Şube kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad))
            throw new ValidationException("Şube adı zorunludur.");
    }

    private static BranchInput Normalize(BranchInput input) => new()
    {
        // Kod büyük harfe normalize edilir (benzersizlik tutarlılığı + tipik kullanım).
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Adres = Trim(input.Adres),
        Telefon = Trim(input.Telefon),
        Eposta = Trim(input.Eposta),
        Il = Trim(input.Il),
        Ilce = Trim(input.Ilce),
        Yetkili = Trim(input.Yetkili),
        CalismaSaatleri = Trim(input.CalismaSaatleri),
        KomisyonOran = input.KomisyonOran,
        EvrakNoOnek = Trim(input.EvrakNoOnek),
        // FAZ-23 — Normalize YENİ nesne kurar: eklenmeyen alan sessizce kaybolur.
        WebIsim = Trim(input.WebIsim),
        FirmaUnvani = Trim(input.FirmaUnvani),
        WebRezOncesiSaat = input.WebRezOncesiSaat,
        Enlem = input.Enlem,
        Boylam = input.Boylam,
        HizmetKomisyonOran = input.HizmetKomisyonOran,
        RezervasyonRengi = Trim(input.RezervasyonRengi),
        AlisSubesiDegilMi = input.AlisSubesiDegilMi,
        WebSira = input.WebSira,
        WebOtoparkId = Trim(input.WebOtoparkId),
        BayiCariKod = Trim(input.BayiCariKod),
        BayiOfisId = Trim(input.BayiOfisId),
        KomisyonHesabi = Trim(input.KomisyonHesabi),
        OnlineRezId = Trim(input.OnlineRezId),
        SozlesmeNoFormati = Trim(input.SozlesmeNoFormati),
        NakitHesapId = input.NakitHesapId,
        BankaHesapId = input.BankaHesapId,
        EntegrasyonKodu = Trim(input.EntegrasyonKodu),
        ResimDosyasi = Trim(input.ResimDosyasi),
        HaftalikCalismaSaatleri = Trim(input.HaftalikCalismaSaatleri),
        Aktif = input.Aktif
    };

    // ---- FAZ-23: şubeye özel ücretsiz hizmet ----

    public Task<IReadOnlyList<SubeUcretsizHizmet>> ListHizmetlerAsync(Guid subeId, CancellationToken ct = default)
        => _repository.ListHizmetlerAsync(subeId, ct);

    public async Task<Guid> AddHizmetAsync(SubeUcretsizHizmetInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (input.SubeId == Guid.Empty) throw new ValidationException("Şube seçilmelidir.");
        if (string.IsNullOrWhiteSpace(input.HizmetAdi)) throw new ValidationException("Hizmet adı zorunludur.");
        if (await _repository.FindAsync(input.SubeId, ct) is null)
            throw new ValidationException("Şube bulunamadı.");

        var row = new SubeUcretsizHizmet
        {
            SubeId = input.SubeId,
            HizmetAdi = input.HizmetAdi.Trim(),
            Aciklama = string.IsNullOrWhiteSpace(input.Aciklama) ? null : input.Aciklama.Trim()
        };
        await _repository.AddHizmetAsync(row, ct);
        return row.Id;
    }

    public Task<bool> RemoveHizmetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        return _repository.RemoveHizmetAsync(id, ct);
    }

    // ---- FAZ-23: şube birleştirme ----

    /// <summary>
    /// Birleştirme ÖNİZLEMESİ — hangi tabloda kaç kayıt taşınacak. YAZMA YAPMAZ.
    /// Toplu UPDATE geri alınamadığı için kullanıcı onaydan önce etkiyi görmeli.
    /// </summary>
    public async Task<SubeBirlestirOnizleme?> BirlestirOnizleAsync(
        Guid kaynakId, Guid hedefId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        var kaynak = await _repository.FindAsync(kaynakId, ct);
        var hedef = await _repository.FindAsync(hedefId, ct);
        if (kaynak is null || hedef is null) return null;
        return new SubeBirlestirOnizleme(kaynak.Ad, hedef.Ad, await _repository.BirlestirSayimAsync(kaynakId, ct));
    }

    /// <summary>
    /// Kaynak şubenin TÜM referanslarını hedefe taşır, kaynağı PASİFE çeker (silmez).
    ///
    /// <para>Yetki <see cref="Permission.ManageUsers"/> (Admin): tek çağrıda çok sayıda kaydı
    /// değiştiren, geri alınamayan bir işlem — operasyon yetkisi yetmez.</para>
    /// </summary>
    public async Task<int> BirlestirAsync(Guid kaynakId, Guid hedefId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        if (kaynakId == Guid.Empty || hedefId == Guid.Empty)
            throw new ValidationException("Kaynak ve hedef şube seçilmelidir.");
        // Kendine birleştirme: tüm referansları kendine yazıp şubeyi PASİFE çekerdi — sessiz felaket.
        if (kaynakId == hedefId)
            throw new ValidationException("Kaynak ve hedef şube farklı olmalıdır.");

        var kaynak = await _repository.FindAsync(kaynakId, ct)
            ?? throw new ValidationException("Kaynak şube bulunamadı.");
        var hedef = await _repository.FindAsync(hedefId, ct)
            ?? throw new ValidationException("Hedef şube bulunamadı.");
        if (!hedef.Aktif)
            throw new ValidationException("Hedef şube pasif — önce aktifleştirin (pasif şubeye taşımak kayıtları görünmez yapar).");

        var tasinan = await _repository.BirlestirAsync(kaynakId, hedefId, ct);

        // Birleştirme HAM SQL ile yazıyor (toplu UPDATE) → araç listesi cache'i BAYAT kalır ve
        // kullanıcı 60 saniye boyunca araçları hâlâ eski şubede görür. Cache açıkça temizlenir.
        if (tasinan > 0) _cache.Invalidate(Vehicles.VehicleService.CacheKey);
        return tasinan;
    }

    private static void Apply(Branch b, BranchInput n)
    {
        b.Kod = n.Kod;
        b.Ad = n.Ad;
        b.Adres = n.Adres;
        b.Telefon = n.Telefon;
        b.Eposta = n.Eposta;
        b.Il = n.Il;
        b.Ilce = n.Ilce;
        b.Yetkili = n.Yetkili;
        b.CalismaSaatleri = n.CalismaSaatleri;
        b.KomisyonOran = n.KomisyonOran;
        b.EvrakNoOnek = n.EvrakNoOnek;
        b.WebIsim = n.WebIsim;
        b.FirmaUnvani = n.FirmaUnvani;
        b.WebRezOncesiSaat = n.WebRezOncesiSaat;
        b.Enlem = n.Enlem;
        b.Boylam = n.Boylam;
        b.HizmetKomisyonOran = n.HizmetKomisyonOran;
        b.RezervasyonRengi = n.RezervasyonRengi;
        b.AlisSubesiDegilMi = n.AlisSubesiDegilMi;
        b.WebSira = n.WebSira;
        b.WebOtoparkId = n.WebOtoparkId;
        b.BayiCariKod = n.BayiCariKod;
        b.BayiOfisId = n.BayiOfisId;
        b.KomisyonHesabi = n.KomisyonHesabi;
        b.OnlineRezId = n.OnlineRezId;
        b.SozlesmeNoFormati = n.SozlesmeNoFormati;
        b.NakitHesapId = n.NakitHesapId;
        b.BankaHesapId = n.BankaHesapId;
        b.EntegrasyonKodu = n.EntegrasyonKodu;
        b.ResimDosyasi = n.ResimDosyasi;
        b.HaftalikCalismaSaatleri = n.HaftalikCalismaSaatleri;
        b.Aktif = n.Aktif;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
