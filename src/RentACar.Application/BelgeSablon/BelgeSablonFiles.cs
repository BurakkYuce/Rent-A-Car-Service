using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.BelgeSablon;

using Entity = RentACar.Domain.Entities.BelgeSablon;

public interface IDocumentTemplateRepository
{
    Task<IReadOnlyList<Entity>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Entity>> ListByTypeAsync(BelgeTuru type, bool activeOnly, CancellationToken ct = default);
    Task<Entity?> FindAsync(Guid id, CancellationToken ct = default);
    /// <summary>Belge türünün varsayılan (VarsayilanMi + Aktif) şablonu; yoksa null.</summary>
    Task<Entity?> FindDefaultAsync(BelgeTuru type, CancellationToken ct = default);
    Task<bool> NameExistsAsync(BelgeTuru type, string name, Guid? excludeId = null, CancellationToken ct = default);
    Task CreateAsync(Entity row, CancellationToken ct = default);
    Task<bool> UpdateAsync(Guid id, Action<Entity> apply, CancellationToken ct = default);
    /// <summary>F11.1b — satır kilidi + iyimser sürüm karşılaştırması; uyuşmazlık <c>EszamanliDegisiklikException</c>.</summary>
    Task<bool> UpdateAsync(Guid id, string? expectedVersion, Action<Entity> apply, CancellationToken ct = default);
    /// <summary>F11.1b — satır sürümü (Postgres <c>xmin</c>, opak). Yoksa <c>null</c>.</summary>
    Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>Türdeki DİĞER şablonların VarsayilanMi bayrağını temizler (tür başına tek varsayılan kuralı).</summary>
    Task ClearDefaultAsync(BelgeTuru type, Guid exceptId, CancellationToken ct = default);
}

/// <summary>Belge şablonu giriş modeli (admin formu).</summary>
public sealed class BelgeSablonInput
{
    public BelgeTuru BelgeTuru { get; set; }
    public string Ad { get; set; } = string.Empty;
    public bool VarsayilanMi { get; set; }
    public bool Aktif { get; set; } = true;
    public string? BelgeBasligi { get; set; }
    public string? HukukiMetinSol { get; set; }
    public string? HukukiMetinSag { get; set; }
    public string? EkKosullarVarsayilan { get; set; }
    public string? AltBilgi { get; set; }
    /// <summary>FAZ-80 — sözleşme PDF'inde fiziksel imza alanı basılsın mı (varsayılan true).</summary>
    public bool ImzaAlaniGoster { get; set; } = true;
}

/// <summary>
/// Belge şablonu master iş mantığı. Yazma ManageUsers (Ayarlar hassasiyeti — belge metni marka kimliği).
/// Tür içinde Ad benzersiz; tür başına en çok bir VarsayilanMi (create/update sonrası diğerleri temizlenir).
/// Yazdırma anındaki OKUMA bu servisi DEĞİL, BelgeSablonCozumleyici'yi kullanır (operatör yetkisiyle çalışır).
/// </summary>
public sealed class DocumentTemplateService(IDocumentTemplateRepository repository, ICurrentUser currentUser)
{
    public Task<IReadOnlyList<Entity>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(ct);

    public Task<IReadOnlyList<Entity>> ListByTypeAsync(BelgeTuru type, CancellationToken ct = default)
        => repository.ListByTypeAsync(type, activeOnly: false, ct);

    public async Task<Guid> CreateAsync(BelgeSablonInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await repository.NameExistsAsync(n.BelgeTuru, n.Ad, null, ct))
            throw new ValidationException($"'{n.Ad}' adlı şablon bu belge türünde zaten var.");
        var row = new Entity();
        Apply(row, n);
        await repository.CreateAsync(row, ct);
        if (row.VarsayilanMi) await repository.ClearDefaultAsync(row.BelgeTuru, row.Id, ct);
        return row.Id;
    }

    /// <summary>F11.1b — tekil kayıt (yönetim; ManageUsers).</summary>
    public Task<Entity?> GetAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return repository.FindAsync(id, ct);
    }

    /// <summary>F11.1b — satır sürümü (opak); yoksa <c>null</c>.</summary>
    public Task<string?> RowVersionAsync(Guid id, CancellationToken ct = default) => repository.RowVersionAsync(id, ct);

    public Task<bool> UpdateAsync(Guid id, BelgeSablonInput input, CancellationToken ct = default)
        => UpdateAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1b — <paramref name="expectedVersion"/> doluysa kilit altında sürüm karşılaştırmalı tam değiştirme.</summary>
    public async Task<bool> UpdateAsync(Guid id, BelgeSablonInput input, string? expectedVersion, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        var n = Normalize(input);
        Validate(n);
        if (await repository.NameExistsAsync(n.BelgeTuru, n.Ad, id, ct))
            throw new ValidationException($"'{n.Ad}' adlı şablon bu belge türünde zaten var.");
        void ApplyAll(Entity r) { Apply(r, n); r.UpdatedAtUtc = DateTimeOffset.UtcNow; }
        var ok = expectedVersion is null
            ? await repository.UpdateAsync(id, ApplyAll, ct)
            : await repository.UpdateAsync(id, expectedVersion, ApplyAll, ct);
        if (ok && n.VarsayilanMi) await repository.ClearDefaultAsync(n.BelgeTuru, id, ct);
        return ok;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(currentUser, Permission.ManageUsers);
        return repository.DeleteAsync(id, ct);
    }

    private static void Validate(BelgeSablonInput n)
    {
        if (!Enum.IsDefined(n.BelgeTuru)) throw new ValidationException("Geçersiz belge türü.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Şablon adı zorunludur.");
        if (n.Ad.Length > 128) throw new ValidationException("Şablon adı en çok 128 karakter olabilir.");
        if ((n.BelgeBasligi?.Length ?? 0) > 256) throw new ValidationException("Belge başlığı en çok 256 karakter olabilir.");
        if ((n.HukukiMetinSol?.Length ?? 0) > 4000 || (n.HukukiMetinSag?.Length ?? 0) > 4000
            || (n.EkKosullarVarsayilan?.Length ?? 0) > 4000)
            throw new ValidationException("Metin bölümü en çok 4000 karakter olabilir.");
        if ((n.AltBilgi?.Length ?? 0) > 512) throw new ValidationException("Alt bilgi en çok 512 karakter olabilir.");
    }

    private static BelgeSablonInput Normalize(BelgeSablonInput i) => new()
    {
        BelgeTuru = i.BelgeTuru,
        Ad = (i.Ad ?? string.Empty).Trim(),
        VarsayilanMi = i.VarsayilanMi,
        Aktif = i.Aktif,
        BelgeBasligi = Empty(i.BelgeBasligi),
        HukukiMetinSol = Empty(i.HukukiMetinSol),
        HukukiMetinSag = Empty(i.HukukiMetinSag),
        EkKosullarVarsayilan = Empty(i.EkKosullarVarsayilan),
        AltBilgi = Empty(i.AltBilgi),
        // DİKKAT: Normalize YENİ bir nesne kurar — buraya eklenmeyen her alan sessizce property
        // initializer değerine (burada true) düşer ve kullanıcının seçimi kaybolur.
        ImzaAlaniGoster = i.ImzaAlaniGoster
    };

    // Boş/whitespace bölüm → null (null = "bu bölümde varsayılanı bas" semantiği).
    private static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Apply(Entity r, BelgeSablonInput n)
    {
        r.BelgeTuru = n.BelgeTuru; r.Ad = n.Ad; r.VarsayilanMi = n.VarsayilanMi; r.Aktif = n.Aktif;
        r.BelgeBasligi = n.BelgeBasligi;
        r.HukukiMetinSol = n.HukukiMetinSol;
        r.HukukiMetinSag = n.HukukiMetinSag;
        r.EkKosullarVarsayilan = n.EkKosullarVarsayilan;
        r.AltBilgi = n.AltBilgi;
        r.ImzaAlaniGoster = n.ImzaAlaniGoster;
    }
}
