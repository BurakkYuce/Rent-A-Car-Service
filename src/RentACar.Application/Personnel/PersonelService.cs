using System.Globalization;
using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Personnel;

/// <summary>
/// Personel master iş mantığı (roadmap C1): doğrulama + Kod benzersizliği + CRUD. PII (TcKimlik, Maas)
/// <see cref="ISecretProtector"/> ile at-rest ŞİFRELENİR (D1 deseni). Hassas (TC/maaş) → tüm işlemler
/// <see cref="Permission.ManageUsers"/> (yalnız Admin). Güncellemede PII alanı BOŞ ise mevcut korunur.
/// Tenant izolasyonu/audit alt katmanda.
/// </summary>
public sealed class PersonelService(
    IPersonelRepository repository, ICurrentUser currentUser, ISecretProtector secrets, ScreenPermissionService screens)
{
    private readonly IPersonelRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ISecretProtector _secrets = secrets;
    private readonly ScreenPermissionService _screens = screens; // roadmap F3: ekran override (floor üstüne)

    public async Task<IReadOnlyList<Personel>> ListAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await _screens.EnsureScreenAccessAsync("personel", Permission.ManageUsers, ct);
        return await _repository.ListAsync(ct);
    }

    /// <summary>
    /// FAZ-40 filtreli liste. Süzme BELLEKTE ve Türkçe harf-duyarlı karşılaştırıcıyla yapılır:
    /// SQL lower()/ILIKE sonucu DB collation'ına bağlanıyor ve İ/ı'da yerelde geçip CI'da patlıyor
    /// (FAZ-22'de ölçüldü). Personel küçük bir master tablo, bellekte süzmek uygun.
    /// </summary>
    public async Task<IReadOnlyList<Personel>> SearchAsync(PersonelFilter? filtre = null, CancellationToken ct = default)
    {
        var hepsi = await ListAsync(ct);
        var f = filtre ?? new PersonelFilter();

        IEnumerable<Personel> q = hepsi;
        if (f.Aktif is bool a) q = q.Where(p => p.Aktif == a);
        if (!string.IsNullOrWhiteSpace(f.Sube))
            q = q.Where(p => TurkishText.EqualsIgnoreTurkishCase(p.Sube, f.Sube));
        if (!string.IsNullOrWhiteSpace(f.GorevTanimi))
            q = q.Where(p => TurkishText.EqualsIgnoreTurkishCase(p.GorevTanimi, f.GorevTanimi));
        if (!string.IsNullOrWhiteSpace(f.Ara))
        {
            var t = f.Ara.Trim();
            q = q.Where(p => Icerir(p.Kod, t) || Icerir(p.Ad, t) || Icerir(p.Soyad, t)
                          || Icerir($"{p.Ad} {p.Soyad}", t) || Icerir(p.CepTel, t) || Icerir(p.MailAdresi, t));
        }
        return q.OrderBy(p => p.Ad, StringComparer.CurrentCulture)
                .ThenBy(p => p.Soyad, StringComparer.CurrentCulture).ToList();
    }

    private static bool Icerir(string? kaynak, string terim)
        => kaynak is not null && kaynak.Contains(terim, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Seçim listesi (dropdown) — kira dönüşü "Teslim Alan" gibi OPERASYON ekranları için. PII TAŞIMAZ
    /// (yalnız Id/Ad/Soyad/Şube projeksiyonu; TcKimlikEnc/MaasEnc dışarı çıkmaz) → ManageUsers yerine
    /// OperationsWrite yeter. (Önceki gizli bug: dönüş formu ListAsync çağırıyordu → Operatör rolünde
    /// ManageUsers guard'ı sayfayı patlatıyordu; seed Admin olduğundan görünmüyordu.)
    ///
    /// <para>FAZ-74: izin GENİŞLETİLDİ (daraltılmadı) — FinanceWrite de yeterli. Maliyet teklifi
    /// ekranı Muhasebe rolüne açık; Muhasebe'de OperationsWrite YOK ve aynı bug'ın simetriği
    /// (Muhasebe kullanıcısında sayfanın patlaması) doğardı. Projeksiyon PII taşımadığı için
    /// finans rolüne açmak yeni bir sızıntı yüzeyi yaratmaz.</para>
    /// </summary>
    public async Task<IReadOnlyList<PersonelSecim>> ListForSelectAsync(CancellationToken ct = default)
    {
        PermissionGuard.RequireAny(_currentUser, Permission.OperationsWrite, Permission.FinanceWrite);
        var rows = await _repository.ListAsync(ct);
        return rows.Where(p => p.Aktif)
            .Select(p => new PersonelSecim(p.Id, p.Ad, p.Soyad, p.Sube))
            .ToList();
    }

    /// <summary>Detay (PII çözülmüş) — düzenleme formu için.</summary>
    public async Task<PersonelDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await _screens.EnsureScreenAccessAsync("personel", Permission.ManageUsers, ct);
        var r = await _repository.FindAsync(id, ct);
        if (r is null) return null;
        return new PersonelDetail(
            r.Id, r.Kod, r.Ad, r.Soyad, _secrets.Unprotect(r.TcKimlikEnc),
            r.IseGiris, r.IseCikis, r.SurucuBelgeNo, ParseMaas(_secrets.Unprotect(r.MaasEnc)), r.Sube, r.Aktif,
            Ham: r);
    }

    public async Task<Guid> CreateAsync(PersonelInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await _screens.EnsureScreenAccessAsync("personel", Permission.ManageUsers, ct);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod!, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' sicilli personel zaten var.");

        var row = new Personel();
        ApplyPlain(row, n);
        row.TcKimlikEnc = _secrets.Protect(n.TcKimlik);
        row.MaasEnc = _secrets.Protect(MaasToText(n.Maas));
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, PersonelInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await _screens.EnsureScreenAccessAsync("personel", Permission.ManageUsers, ct);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod!, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' sicilli personel zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            ApplyPlain(row, n);
            // PII: dolu ise şifrele+güncelle; boş ise mevcut cipher KORUNUR.
            if (!string.IsNullOrWhiteSpace(n.TcKimlik)) row.TcKimlikEnc = _secrets.Protect(n.TcKimlik);
            if (n.Maas is not null) row.MaasEnc = _secrets.Protect(MaasToText(n.Maas));
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.ManageUsers);
        await _screens.EnsureScreenAccessAsync("personel", Permission.ManageUsers, ct);
        return await _repository.DeleteAsync(id, ct);
    }

    private static void Validate(PersonelInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Sicil (kod) zorunludur.");
        if (n.Kod!.Length > 32) throw new ValidationException("Sicil en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Ad zorunludur.");
        if (string.IsNullOrWhiteSpace(n.Soyad)) throw new ValidationException("Soyad zorunludur.");
        if (n.Maas is < 0m) throw new ValidationException("Maaş negatif olamaz.");
        if (n.IseCikis is { } c && n.IseGiris is { } g && c < g)
            throw new ValidationException("İşten çıkış, işe giriş tarihinden önce olamaz.");
        // Doğum tarihi gelecekte olamaz (TarihPolitikasi ile aynı yön).
        if (n.DogumTarihi is { } d && d > DateTimeOffset.UtcNow)
            throw new ValidationException("Doğum tarihi gelecekte olamaz.");
    }

    private static PersonelInput Normalize(PersonelInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Soyad = (input.Soyad ?? string.Empty).Trim(),
        TcKimlik = TrimOrNull(input.TcKimlik),
        IseGiris = input.IseGiris,
        IseCikis = input.IseCikis,
        SurucuBelgeNo = TrimOrNull(input.SurucuBelgeNo),
        Maas = input.Maas,
        Sube = TrimOrNull(input.Sube),
        // FAZ-40: Normalize KOPYA KURUCUDUR — yeni alan buraya DA yazılmalı, yoksa kullanıcının
        // girdiği değer derleme hatası vermeden sessizce düşer (BelgeSablon dersi).
        GorevTanimi = TrimOrNull(input.GorevTanimi),
        Adres = TrimOrNull(input.Adres),
        EvTelefonu = TrimOrNull(input.EvTelefonu),
        IsTelefonu = TrimOrNull(input.IsTelefonu),
        CepTel = TrimOrNull(input.CepTel),
        MailAdresi = TrimOrNull(input.MailAdresi),
        Referans = TrimOrNull(input.Referans),
        Aciklama = TrimOrNull(input.Aciklama),
        SSinifi = TrimOrNull(input.SSinifi),
        SVerilisYeri = TrimOrNull(input.SVerilisYeri),
        DogumYeri = TrimOrNull(input.DogumYeri),
        BabaAdi = TrimOrNull(input.BabaAdi),
        AnaAdi = TrimOrNull(input.AnaAdi),
        Il = TrimOrNull(input.Il),
        Ilce = TrimOrNull(input.Ilce),
        Mahalle = TrimOrNull(input.Mahalle),
        CiltNo = TrimOrNull(input.CiltNo),
        AileSiraNo = TrimOrNull(input.AileSiraNo),
        SiraNo = TrimOrNull(input.SiraNo),
        KanGrubu = TrimOrNull(input.KanGrubu),
        RacTabletNo = TrimOrNull(input.RacTabletNo),
        SVerilisTarihi = input.SVerilisTarihi,
        DogumTarihi = input.DogumTarihi,
        Aktif = input.Aktif
    };

    private static void ApplyPlain(Personel row, PersonelInput n)
    {
        row.Kod = n.Kod!;
        row.Ad = n.Ad!;
        row.Soyad = n.Soyad!;
        row.IseGiris = n.IseGiris;
        row.IseCikis = n.IseCikis;
        row.SurucuBelgeNo = n.SurucuBelgeNo;
        row.Sube = n.Sube;
        row.GorevTanimi = n.GorevTanimi;
        row.Adres = n.Adres;
        row.EvTelefonu = n.EvTelefonu;
        row.IsTelefonu = n.IsTelefonu;
        row.CepTel = n.CepTel;
        row.MailAdresi = n.MailAdresi;
        row.Referans = n.Referans;
        row.Aciklama = n.Aciklama;
        row.SSinifi = n.SSinifi;
        row.SVerilisYeri = n.SVerilisYeri;
        row.DogumYeri = n.DogumYeri;
        row.BabaAdi = n.BabaAdi;
        row.AnaAdi = n.AnaAdi;
        row.Il = n.Il;
        row.Ilce = n.Ilce;
        row.Mahalle = n.Mahalle;
        row.CiltNo = n.CiltNo;
        row.AileSiraNo = n.AileSiraNo;
        row.SiraNo = n.SiraNo;
        row.KanGrubu = n.KanGrubu;
        row.RacTabletNo = n.RacTabletNo;
        row.SVerilisTarihi = n.SVerilisTarihi;
        row.DogumTarihi = n.DogumTarihi;
        row.Aktif = n.Aktif;
    }

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? MaasToText(decimal? m) => m?.ToString(CultureInfo.InvariantCulture);
    private static decimal? ParseMaas(string? s)
        => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
}
