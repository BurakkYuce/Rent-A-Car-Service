using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Locations;

/// <summary>
/// Ofis/Lokasyon master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel
/// yapılandırmadır → <see cref="Permission.OperationsWrite"/>. Açılır liste okuması
/// (<see cref="ListActiveAsync"/>) yetkisizdir (rezervasyon/teklif/kira formu çağırır).
/// Tenant izolasyonu/audit alt katmanda otomatik.
/// </summary>
public sealed class LocationService(ILocationRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly ILocationRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;
    private const string CK = "locations";

    public Task<IReadOnlyList<Location>> ListAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(CK, () => _repository.ListAsync(ct), ct);

    /// <summary>Form açılır listesi kaynağı (yalnız aktif). Yetki gerektirmez.</summary>
    public async Task<IReadOnlyList<Location>> ListActiveAsync(CancellationToken ct = default)
        => (await ListAsync(ct)).Where(x => x.Aktif).ToList();

    public Task<Location?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(LocationInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        // F11.1b güvenlik (H1 devamı): şube kapsamı SERVİSTE — Blazor ve /api/ui tek kural; operatör yalnız kendi
        // şubesine ofis açar.
        BranchScope.RequireInScope(_currentUser, recordBranchId: null, n.Sube);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ofis zaten var.");

        var loc = new Location();
        Apply(loc, n);
        await _repository.CreateAsync(loc, ct);
        _cache.Invalidate(CK);
        return loc.Id;
    }

    public Task<bool> UpdateAsync(Guid id, LocationInput input, CancellationToken ct = default)
        => UpdateAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1b — satır sürümü (opak); yoksa <c>null</c>.</summary>
    public Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default) => _repository.RowVersionAsync(id, ct);

    /// <summary>F11.1b — <paramref name="expectedVersion"/> doluysa kilit altında sürüm karşılaştırmalı tam değiştirme.</summary>
    public async Task<bool> UpdateAsync(Guid id, LocationInput input, string? expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        // F11.1b güvenlik (H1 devamı): önce MEVCUT ofisin şubesi (doğrulamadan önce), sonra hedef şube — operatör başka
        // şubenin ofisini düzenleyemez ya da bir ofisi başka şubeye taşıyamaz (Blazor + API tek kural).
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        BranchScope.RequireInScope(_currentUser, current.SubeId, current.Sube);
        BranchScope.RequireInScope(_currentUser, recordBranchId: null, n.Sube);
        Validate(n);
        if (await _repository.CodeExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu ofis zaten var.");

        void ApplyAll(Location loc)
        {
            Apply(loc, n);
            loc.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        var ok = expectedVersion is null
            ? await _repository.UpdateAsync(id, ApplyAll, ct)
            : await _repository.UpdateAsync(id, expectedVersion, ApplyAll, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        // F11.1b güvenlik (H1 devamı): başka şubenin ofisi silinemez.
        if (await _repository.FindAsync(id, ct) is not { } current) return false;
        BranchScope.RequireInScope(_currentUser, current.SubeId, current.Sube);
        var ok = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(CK);
        return ok;
    }

    private static void Validate(LocationInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Ofis kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Ofis kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ofis adı zorunludur.");
    }

    private static LocationInput Normalize(LocationInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Adres = Trim(input.Adres),
        Telefon = Trim(input.Telefon),
        Eposta = Trim(input.Eposta),
        CalismaSaatleri = Trim(input.CalismaSaatleri),
        TeslimUcreti = input.TeslimUcreti,
        Sube = Trim(input.Sube),
        // FAZ-22: Normalize KOPYA KURUCUDUR — yeni alan eklendiğinde BURAYA da yazılmalı, yoksa
        // kullanıcının girdiği değer derleme hatası vermeden sessizce düşer (BelgeSablon dersi).
        IngilizceAd = Trim(input.IngilizceAd),
        BulusmaNoktasi = Trim(input.BulusmaNoktasi),
        Iata = Trim(input.Iata)?.ToUpperInvariant(),
        WebdeGizle = input.WebdeGizle,
        LokasyonTuru = Trim(input.LokasyonTuru),
        BinaNo = Trim(input.BinaNo),
        Tarif = Trim(input.Tarif),
        Ulke = Trim(input.Ulke),
        PostaKodu = Trim(input.PostaKodu),
        MapsKonumu = Trim(input.MapsKonumu),
        EkAciklama = Trim(input.EkAciklama),
        WebSira = input.WebSira,
        DropKarsilamaTuru = Trim(input.DropKarsilamaTuru),
        DropCalismaSekli = Trim(input.DropCalismaSekli),
        OzelMail = Trim(input.OzelMail),
        OzelTelefon = Trim(input.OzelTelefon),
        HaftalikCalismaSaatleri = NormalizeWeek(input.HaftalikCalismaSaatleri),
        Aktif = input.Aktif
    };

    /// <summary>
    /// Haftalık saatleri DAİMA 7 satıra (Pzt=1…Paz=7) tamamlar ve sıralar. Eksik/çift gün gönderen
    /// bir form kaydı bozmasın diye şekil BURADA garanti edilir; okuyucu 7 satır olduğunu varsayabilir.
    /// Gün numarası aralık dışıysa satır atılır (sessiz kabul yerine görünür kayıp yok — 7 satır sabit).
    /// </summary>
    public static List<RentACar.Domain.Entities.GunSaat> NormalizeWeek(
        IEnumerable<RentACar.Domain.Entities.GunSaat>? input)
    {
        var incoming = (input ?? []).Where(g => g.Gun is >= 1 and <= 7)
            .GroupBy(g => g.Gun).ToDictionary(g => g.Key, g => g.Last());
        var result = new List<RentACar.Domain.Entities.GunSaat>(7);
        for (var day = 1; day <= 7; day++)
        {
            if (incoming.TryGetValue(day, out var g))
                result.Add(new RentACar.Domain.Entities.GunSaat
                {
                    Gun = day,
                    Acilis = Trim(g.Acilis),
                    Kapanis = Trim(g.Kapanis),
                    // Saat girilmemişse gün KAPALI sayılır — "boş açılış" bir çalışma saati değildir.
                    Kapali = g.Kapali || (string.IsNullOrWhiteSpace(g.Acilis) && string.IsNullOrWhiteSpace(g.Kapanis))
                });
            else
                result.Add(new RentACar.Domain.Entities.GunSaat { Gun = day, Kapali = true });
        }
        return result;
    }

    private static void Apply(Location loc, LocationInput n)
    {
        loc.Kod = n.Kod;
        loc.Ad = n.Ad;
        loc.Adres = n.Adres;
        loc.Telefon = n.Telefon;
        loc.Eposta = n.Eposta;
        loc.CalismaSaatleri = n.CalismaSaatleri;
        loc.TeslimUcreti = n.TeslimUcreti;
        loc.Sube = n.Sube;
        loc.IngilizceAd = n.IngilizceAd;
        loc.BulusmaNoktasi = n.BulusmaNoktasi;
        loc.Iata = n.Iata;
        loc.WebdeGizle = n.WebdeGizle;
        loc.LokasyonTuru = n.LokasyonTuru;
        loc.BinaNo = n.BinaNo;
        loc.Tarif = n.Tarif;
        loc.Ulke = n.Ulke;
        loc.PostaKodu = n.PostaKodu;
        loc.MapsKonumu = n.MapsKonumu;
        loc.EkAciklama = n.EkAciklama;
        loc.WebSira = n.WebSira;
        loc.DropKarsilamaTuru = n.DropKarsilamaTuru;
        loc.DropCalismaSekli = n.DropCalismaSekli;
        loc.OzelMail = n.OzelMail;
        loc.OzelTelefon = n.OzelTelefon;
        // Yeni liste ATANIR (mevcut listeye eklenmez) — ValueComparer içerik karşılaştırdığı için
        // referans değişimi sorun değil, ama eski satırların kalması sessiz birikme yapardı.
        loc.HaftalikCalismaSaatleri = n.HaftalikCalismaSaatleri;
        loc.Aktif = n.Aktif;
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
