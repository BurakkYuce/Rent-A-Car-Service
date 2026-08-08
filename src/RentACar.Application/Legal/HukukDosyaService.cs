using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.Legal;

/// <summary>
/// Hukuk dosyası master iş mantığı (roadmap C2): doğrulama + DosyaNo benzersizliği + CRUD. Yazma
/// operasyonel → <see cref="Permission.OperationsWrite"/>. Tenant izolasyonu/audit alt katmanda.
///
/// <para><b>PARA ÇİTİ:</b> <c>Tutar</c> ve <c>Tahsilat</c> BİLGİ ALANIDIR — bu servis hiçbir
/// defter kaydı (<c>AccountLedgerEntry</c>) yazmaz, cari bakiyeyi değiştirmez. Gerçek tahsilat
/// Kasa/Banka ekranından cari üzerine girilir. (KARARLAR.md genel politikası; kırılgan regresyon
/// testi <c>HukukTests.Tahsilat_deftere_yazmaz</c> bunu kalıcı olarak kilitler.)</para>
/// </summary>
public sealed class HukukDosyaService(IHukukDosyaRepository repository, ICurrentUser currentUser)
{
    private readonly IHukukDosyaRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>FAZ-41 — filtreli liste (müşteri adı çözülmüş). Salt-okur; ekran zaten rol kapılı.</summary>
    public Task<IReadOnlyList<HukukDosyaSatirDto>> SearchAsync(
        HukukDosyaFilter? filter = null, CancellationToken ct = default)
        => _repository.SearchAsync(filter, ct);

    public Task<HukukDosya?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(HukukDosyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.DosyaNoExistsAsync(n.DosyaNo!, excludeId: null, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");

        var row = new HukukDosya();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, HukukDosyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.DosyaNoExistsAsync(n.DosyaNo!, excludeId: id, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");

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

    private static void Validate(HukukDosyaInput n)
    {
        if (string.IsNullOrWhiteSpace(n.DosyaNo)) throw new ValidationException("Dosya no zorunludur.");
        if (n.DosyaNo!.Length > 64) throw new ValidationException("Dosya no en çok 64 karakter olabilir.");
        if (n.Tutar < 0m) throw new ValidationException("Tutar negatif olamaz.");
        // Tahsilat ÜST sınırı bilinçli YOK: faiz/masrafla dosya tutarının üstünde tahsilat meşrudur
        // (Kalan o zaman negatife döner). Negatif tahsilat ise işaret hatasıdır → red.
        if (n.Tahsilat is < 0m) throw new ValidationException("Tahsilat negatif olamaz.");
        if (n.FaturaNoTemp is { Length: > 64 }) throw new ValidationException("Fatura no en çok 64 karakter olabilir.");
        if (n.Avukat2Ad is { Length: > 128 }) throw new ValidationException("2. avukat adı en çok 128 karakter olabilir.");
        if (n.AvukatTel is { Length: > 32 } || n.Avukat2Tel is { Length: > 32 })
            throw new ValidationException("Telefon en çok 32 karakter olabilir.");
        if (n.AvukatMail is { Length: > 256 } || n.Avukat2Mail is { Length: > 256 })
            throw new ValidationException("E-posta en çok 256 karakter olabilir.");
    }

    private static HukukDosyaInput Normalize(HukukDosyaInput input) => new()
    {
        DosyaNo = (input.DosyaNo ?? string.Empty).Trim().ToUpperInvariant(),
        CariId = input.CariId,
        Tur = input.Tur,
        Avukat = TrimOrNull(input.Avukat),
        Tutar = input.Tutar,
        Durum = input.Durum,
        Tarih = input.Tarih,
        Aciklama = TrimOrNull(input.Aciklama),
        Aktif = input.Aktif,
        FaturaNoTemp = TrimOrNull(input.FaturaNoTemp),
        AvukatTel = TrimOrNull(input.AvukatTel),
        AvukatMail = TrimOrNull(input.AvukatMail),
        Avukat2Ad = TrimOrNull(input.Avukat2Ad),
        Avukat2Tel = TrimOrNull(input.Avukat2Tel),
        Avukat2Mail = TrimOrNull(input.Avukat2Mail),
        Tahsilat = input.Tahsilat
    };

    private static string? TrimOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(HukukDosya row, HukukDosyaInput n)
    {
        row.DosyaNo = n.DosyaNo!;
        row.CariId = n.CariId;
        row.Tur = n.Tur;
        row.Avukat = n.Avukat;
        row.Tutar = n.Tutar;
        row.Durum = n.Durum;
        row.Tarih = n.Tarih ?? DateTimeOffset.UtcNow;
        row.Aciklama = n.Aciklama;
        row.Aktif = n.Aktif;
        row.FaturaNoTemp = n.FaturaNoTemp;
        row.AvukatTel = n.AvukatTel;
        row.AvukatMail = n.AvukatMail;
        row.Avukat2Ad = n.Avukat2Ad;
        row.Avukat2Tel = n.Avukat2Tel;
        row.Avukat2Mail = n.Avukat2Mail;
        // BİLGİ ALANI — buradan sonra hiçbir defter/bakiye yolu tetiklenmez (bilinçli).
        row.Tahsilat = n.Tahsilat;
    }
}
