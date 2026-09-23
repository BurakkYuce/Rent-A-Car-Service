using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;

namespace RentACar.Application.FinancialAccounts;

/// <summary>
/// Kasa/Banka hesap master iş mantığı: doğrulama + kod benzersizliği + CRUD. Yazma operasyonel →
/// <see cref="Permission.OperationsWrite"/>. <see cref="ListActiveAsync"/> (form açılır liste
/// kaynağı) yetkisizdir. Tenant izolasyonu/audit alt katmanda otomatik. Döviz 3 harfe normalize.
/// </summary>
public sealed class FinancialAccountService(
    IFinancialAccountRepository repository, ICurrentUser currentUser, ITenantCache cache)
{
    private readonly IFinancialAccountRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly ITenantCache _cache = cache;

    private const string AktifCacheKey = "finansal-hesap:aktif";

    public Task<IReadOnlyList<FinancialAccount>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    /// <summary>
    /// Form açılır listesinin kaynağı. FAZ-50 adversarial M6 — <c>HesapSecici</c> bileşeni satır
    /// döngülerinin İÇİNDE kullanılıyor (kira listesi, pano, regülasyon, kredi); önbelleksiz hâlde
    /// 50 satırlık listede 50 ek sorgu demekti. Yazma yollarında açıkça geçersizleştirilir —
    /// TTL'e güvenmek "hesabı ekledim, listede yok" ile sonuçlanırdı.
    /// </summary>
    public Task<IReadOnlyList<FinancialAccount>> ListActiveAsync(CancellationToken ct = default)
        => _cache.GetOrCreateAsync(AktifCacheKey, () => _repository.ListActiveAsync(ct), ct);

    public Task<FinancialAccount?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public async Task<Guid> CreateAsync(FinancialAccountInput input, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: null, ct))
            throw new ValidationException($"'{n.Kod}' kodlu hesap zaten var.");

        var account = new FinancialAccount();
        Apply(account, n);
        await _repository.CreateAsync(account, ct);
        _cache.Invalidate(AktifCacheKey);
        return account.Id;
    }

    public Task<bool> UpdateAsync(Guid id, FinancialAccountInput input, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion: null, ct);

    /// <summary>F11.1a — full replacement with optimistic concurrency (stale version → 409 <c>cakisma</c>).</summary>
    public Task<bool> UpdateAsync(Guid id, FinancialAccountInput input, string expectedVersion, CancellationToken ct = default)
        => UpdateCoreAsync(id, input, expectedVersion, ct);

    /// <summary>F11.1a — opaque row version.</summary>
    public Task<string?> GetVersionAsync(Guid id, CancellationToken ct = default) => _repository.GetVersionAsync(id, ct);

    /// <summary>F11.1a — versions of every row.</summary>
    public Task<IReadOnlyDictionary<Guid, string>> GetVersionsAsync(CancellationToken ct = default) => _repository.GetVersionsAsync(ct);

    private async Task<bool> UpdateCoreAsync(Guid id, FinancialAccountInput input, string? expectedVersion, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        var n = Normalize(input);
        Validate(n);
        if (await _repository.KodExistsAsync(n.Kod, excludeId: id, ct))
            throw new ValidationException($"'{n.Kod}' kodlu hesap zaten var.");

        void Update(FinancialAccount account)
        {
            Apply(account, n);
            account.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        try
        {
            return expectedVersion is null
                ? await _repository.UpdateAsync(id, Update, ct)
                : await _repository.UpdateAsync(id, expectedVersion, Update, ct);
        }
        finally
        {
            _cache.Invalidate(AktifCacheKey);
        }
    }

    /// <summary>
    /// ADVERSARIAL M2 — defter hareketi OLAN hesap silinemez. Silinince bakiye yetim kalıyor,
    /// raporda "(silinmiş hesap)" satırı olarak asılı duruyordu ve aynı kodla açılan yeni hesap
    /// yanında sıfır bakiyeyle görünüyordu. Kullanılmış hesap PASİFE çekilir (mali iz korunur).
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.OperationsWrite);
        if (await _repository.HasLedgerHistoryAsync(id, ct))
            throw new ValidationException(
                "Bu hesapta defter hareketi var; silinemez. Kullanımdan kaldırmak için 'Aktif' işaretini kaldırın.");
        var silindi = await _repository.DeleteAsync(id, ct);
        _cache.Invalidate(AktifCacheKey);
        return silindi;
    }

    private static void Validate(FinancialAccountInput n)
    {
        if (string.IsNullOrWhiteSpace(n.Kod)) throw new ValidationException("Hesap kodu zorunludur.");
        if (n.Kod.Length > 32) throw new ValidationException("Hesap kodu en çok 32 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(n.Ad)) throw new ValidationException("Hesap adı zorunludur.");
        // ADVERSARIAL H1 — tür ZORUNLU ve Kasa/Banka'ya çözülebilir olmalı. Serbest metin
        // ("POS", boş) bırakıldığında aynı hesap iki ayrı defter türünde kullanılıp bakiyesi
        // ikiye bölünüyordu. Serbest metin YAZIMI korunuyor (ör. "Banka - Vadesiz") ama
        // Kasa/Banka'ya çözülmesi şart.
        if (HesapCozucu.TuruCoz(n.Tur) is null)
            throw new ValidationException("Hesap türü zorunludur ve Kasa ya da Banka olmalıdır.");
        if (n.Doviz is { Length: > 0 } d && d.Length != 3)
            throw new ValidationException("Döviz kodu 3 harf olmalıdır (ör. TRY, USD).");
    }

    private static FinancialAccountInput Normalize(FinancialAccountInput input) => new()
    {
        Kod = (input.Kod ?? string.Empty).Trim().ToUpperInvariant(),
        Ad = (input.Ad ?? string.Empty).Trim(),
        Tur = string.IsNullOrWhiteSpace(input.Tur) ? null : input.Tur.Trim(),
        Doviz = string.IsNullOrWhiteSpace(input.Doviz) ? null : input.Doviz.Trim().ToUpperInvariant(),
        Iban = string.IsNullOrWhiteSpace(input.Iban) ? null : input.Iban.Trim().ToUpperInvariant(),
        HesapNo = string.IsNullOrWhiteSpace(input.HesapNo) ? null : input.HesapNo.Trim(),
        Banka = string.IsNullOrWhiteSpace(input.Banka) ? null : input.Banka.Trim(),
        Sube = string.IsNullOrWhiteSpace(input.Sube) ? null : input.Sube.Trim(),
        // Normalize YENİ nesne kurar → eklenmeyen alan sessizce kaybolur.
        HediyeCek = input.HediyeCek,
        OzelKod = string.IsNullOrWhiteSpace(input.OzelKod) ? null : input.OzelKod.Trim().ToUpperInvariant(),
        UyariMailListesi = string.IsNullOrWhiteSpace(input.UyariMailListesi) ? null : input.UyariMailListesi.Trim(),
        Aktif = input.Aktif
    };

    private static void Apply(FinancialAccount account, FinancialAccountInput n)
    {
        account.Kod = n.Kod;
        account.Ad = n.Ad;
        account.Tur = n.Tur;
        account.Doviz = n.Doviz;
        account.Iban = n.Iban;
        account.HesapNo = n.HesapNo;
        account.Banka = n.Banka;
        account.Sube = n.Sube;
        account.HediyeCek = n.HediyeCek;
        account.OzelKod = n.OzelKod;
        account.UyariMailListesi = n.UyariMailListesi;
        account.Aktif = n.Aktif;
    }
}
