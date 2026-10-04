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
public sealed class LegalCaseService(ILegalCaseRepository repository, ICurrentUser currentUser)
{
    private readonly ILegalCaseRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;

    // ---- Güvenlik F4: şube kapsamı (LegalScope — TEK kural; SPA, export ve harici API aynı yoldan) ----

    private async Task<Func<Guid?, bool>> VisibilityAsync(IEnumerable<Guid?> customerIds, CancellationToken ct)
    {
        var filter = BranchScope.EffectiveFilter(_currentUser);
        if (filter.Unrestricted) return static _ => true;
        var ids = customerIds.Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var branches = await _repository.CustomerBranchesAsync(ids, ct);
        return customerId => LegalScope.Visible(filter, customerId, branches);
    }

    /// <summary>Kayıt var VE kapsamdaysa kayıt; aksi null (çağıran 404 verir — başka şubenin dosyası sızmaz).</summary>
    private async Task<HukukDosya?> VisibleAsync(Guid id, CancellationToken ct)
    {
        if (await _repository.FindAsync(id, ct) is not { } row) return null;
        return (await VisibilityAsync([row.CariId], ct))(row.CariId) ? row : null;
    }

    /// <summary>Yazma hedefi: bağlanan cari kapsam dışındaysa 403 (başka şubeye dosya açılamaz/taşınamaz).</summary>
    private async Task RequireTargetAsync(Guid? customerId, CancellationToken ct)
    {
        if (!(await VisibilityAsync([customerId], ct))(customerId))
            throw new NoPermissionException("Seçilen cari şube kapsamınız dışında.");
    }

    public async Task<IReadOnlyList<HukukDosya>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _repository.ListAsync(ct);
        var visible = await VisibilityAsync(rows.Select(r => r.CariId), ct);
        return rows.Where(r => visible(r.CariId)).ToList();
    }

    /// <summary>FAZ-41 — filtreli liste (müşteri adı çözülmüş), şube kapsamına süzülmüş (export da bu yoldan).</summary>
    public async Task<IReadOnlyList<HukukDosyaSatirDto>> SearchAsync(
        HukukDosyaFilter? filter = null, CancellationToken ct = default)
    {
        var rows = await _repository.SearchAsync(filter, ct);
        var visible = await VisibilityAsync(rows.Select(r => r.Dosya.CariId), ct);
        return rows.Where(r => visible(r.Dosya.CariId)).ToList();
    }

    /// <summary>Kapsam dışı dosya "yok"tur (null → 404).</summary>
    public Task<HukukDosya?> GetAsync(Guid id, CancellationToken ct = default) => VisibleAsync(id, ct);

    public async Task<Guid> CreateAsync(HukukDosyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        await RequireTargetAsync(n.CariId, ct);
        if (await _repository.FileNoExistsAsync(n.DosyaNo!, excludeId: null, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");

        var row = new HukukDosya();
        Apply(row, n);
        await _repository.CreateAsync(row, ct);
        return row.Id;
    }

    public async Task<bool> UpdateAsync(Guid id, HukukDosyaInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (await VisibleAsync(id, ct) is null) return false; // kapsam doğrulamadan ÖNCE
        var n = Normalize(input);
        Validate(n);
        await RequireTargetAsync(n.CariId, ct);
        if (await _repository.FileNoExistsAsync(n.DosyaNo!, excludeId: id, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");

        return await _repository.UpdateAsync(id, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F7.1 — tam değiştirme, iyimser eşzamanlılıkla (satır kilidi altında sürüm; farklı → 409).</summary>
    public async Task<bool> UpdateAsync(Guid id, HukukDosyaInput input, string expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (await VisibleAsync(id, ct) is null) return false; // kapsam doğrulamadan/sürümden ÖNCE
        var n = Normalize(input);
        Validate(n);
        await RequireTargetAsync(n.CariId, ct);
        if (await _repository.FileNoExistsAsync(n.DosyaNo!, excludeId: id, ct))
            throw new ValidationException($"'{n.DosyaNo}' dosya no zaten var.");
        return await _repository.UpdateAsync(id, expectedVersion, row =>
        {
            Apply(row, n);
            row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }, ct);
    }

    /// <summary>F7.1 — satır sürümü (PUT'un <c>surum</c>'u); yok/başka kiracı → null.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsDelete); // kabul C-HUKUK: operatör siler DEĞİL
        if (await VisibleAsync(id, ct) is null) return false; // kapsam dışı → 404
        return await _repository.DeleteAsync(id, ct);
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
