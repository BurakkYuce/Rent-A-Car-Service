using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Application.Periods;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Depozito (nakit emanet) yaşam döngüsü (roadmap I3). DENGELİ çift-taraflı defter:
///   Al:     Borç Kasa/Banka / Alacak Depozito(cari)   → nakit ↑, yükümlülük ↑
///   İade:   Borç Depozito(cari) / Alacak Kasa/Banka    → yükümlülük ↓, nakit ↓ (tutulanı aşamaz)
///   Mahsup: Borç Depozito(cari) / Alacak Cari(cari)     → yükümlülüğü cari borcuna mahsup (tutulanı aşamaz)
/// Dönem-kilidi + opsiyonel idempotency (IslemAnahtari → deterministik SourceId; çift-submit yutulur).
/// </summary>
public sealed class DepositService(
    ILedgerPoster ledger, ICashRepository repository, ICurrentUser currentUser, IPeriodLockGuard periodLock,
    RentACar.Application.Kur.ExchangeRateResolver exchangeRateResolver, RentACar.Application.FinancialAccounts.AccountResolver accountResolver,
    RentACar.Application.Customers.ICustomerRepository customers)
{
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICashRepository _repository = repository;
    private readonly ICurrentUser _currentUser = currentUser;
    private readonly IPeriodLockGuard _lock = periodLock;
    private readonly RentACar.Application.Kur.ExchangeRateResolver _exchangeRateResolver = exchangeRateResolver;
    private readonly RentACar.Application.FinancialAccounts.AccountResolver _accountResolver = accountResolver;
    private readonly RentACar.Application.Customers.ICustomerRepository _customers = customers;

    public Task<decimal> GetBalanceAsync(Guid customerId, CancellationToken ct = default)
        => _repository.GetDepositBalanceAsync(customerId, ct);

    /// <summary>F8.1a — tutulan depozitolar (bakiyesi sıfır olmayan cariler; tek sorgu). Para bilgisi →
    /// <see cref="Permission.FinanceWrite"/> (Blazor <c>/depozito</c> ekranı gibi).</summary>
    public Task<Dictionary<Guid, decimal>> GetBalancesAsync(CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        return _repository.GetDepositBalancesAsync(ct);
    }

    /// <summary>Depozito al: Borç Kasa/Banka / Alacak Depozito(cari).
    /// FAZ-50: <paramref name="accountId"/> verilirse nakit bacağı o spesifik hesaba yazılır.</summary>
    public async Task<Guid> GetAsync(Guid customerId, decimal amount, LedgerAccountType account, string? currency = "TRY",
        decimal? exchangeRate = null, DateTimeOffset? date = null, Guid? operationKey = null,
        Guid? accountId = null, CancellationToken ct = default)
    {
        // Guard GİRİŞ noktasında: hesap çözümü PostAsync'ten önce çalışıyor, yetkisiz kullanıcı
        // hesap varlığını yoklayamamalı.
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        await CustomerExistsAsync(customerId, ct);
        // F4.4a adversarial L1: hesap-döviz çiti tahsilattaki gibi (FAZ-50 M4) — USD depozito TRY hesaba yazılmaz.
        return await PostAsync(customerId, amount, account, currency, exchangeRate, date, operationKey, "DepozitoAl", "Depozito al",
            debit: account, debitRef: await _accountResolver.ResolveAsync(accountId, account, ct, RentACar.Application.Kur.ExchangeRateService.NormalizeCode(currency)),
            credit: LedgerAccountType.Depozito, creditRef: customerId, shouldCheck: false, ct);
    }

    /// <summary>Depozito iade: Borç Depozito(cari) / Alacak Kasa/Banka. Tutulan depozitoyu aşamaz.
    /// FAZ-50: <paramref name="accountId"/> verilirse nakit bacağı o spesifik hesaba yazılır.</summary>
    public async Task<Guid> RefundAsync(Guid customerId, decimal amount, LedgerAccountType account, string? currency = "TRY",
        decimal? exchangeRate = null, DateTimeOffset? date = null, Guid? operationKey = null,
        Guid? accountId = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite); // bkz. AlAsync notu
        await CustomerExistsAsync(customerId, ct);
        return await PostAsync(customerId, amount, account, currency, exchangeRate, date, operationKey, "DepozitoIade", "Depozito iade",
            debit: LedgerAccountType.Depozito, debitRef: customerId,
            credit: account, creditRef: await _accountResolver.ResolveAsync(accountId, account, ct, RentACar.Application.Kur.ExchangeRateService.NormalizeCode(currency)),
            shouldCheck: true, ct);
    }

    /// <summary>Depozito mahsup (cari borcuna): Borç Depozito(cari) / Alacak Cari(cari). Tutulanı aşamaz.</summary>
    public async Task<Guid> OffsetAsync(Guid customerId, decimal amount, string? currency = "TRY",
        decimal? exchangeRate = null, DateTimeOffset? date = null, Guid? operationKey = null, CancellationToken ct = default)
    {
        // F8.1a: al/iade/irat ile simetri — guard giriş noktasında, cari kiracıda var olmalı (yetim AccountRef yok).
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        await CustomerExistsAsync(customerId, ct);
        return await PostAsync(customerId, amount, LedgerAccountType.Cari, currency, exchangeRate, date, operationKey, "DepozitoMahsup", "Depozito mahsup",
            debit: LedgerAccountType.Depozito, debitRef: customerId, credit: LedgerAccountType.Cari, creditRef: customerId, shouldCheck: true, ct);
    }

    /// <summary>Depozito İRAT (FAZ 1.2): iade edilmeyen depozito GELİR olur — Borç Depozito(cari) /
    /// Alacak Gelir. Tutulan depozitoyu aşamaz (mevcut bakiye guard'ı). rentalId verilirse gelir o
    /// kiranın aracına atfedilir (karne/Karlilik); kira carisi eşleşmezse repo çiti reddeder.
    /// Çift-submit sessiz idempotent (Depozito% kısmi unique index — I3 sözleşmesi).</summary>
    public async Task<Guid> ForfeitAsync(Guid customerId, decimal amount, string? currency = "TRY",
        decimal? exchangeRate = null, Guid? rentalId = null, DateTimeOffset? date = null,
        Guid? operationKey = null, string? description = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (customerId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (amount <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.");
        await CustomerExistsAsync(customerId, ct);

        var resolvedRate = await _exchangeRateResolver.ResolveAsync(currency, exchangeRate, date, ct); // 1.1 sözleşmesi
        var money = new Money(amount, RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(currency), resolvedRate);
        // Bakiye kontrolü repo tx'inde, kilidin arkasında (TOCTOU çiti — tek otorite).

        var entryDate = date ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi

        var sourceId = operationKey is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        var desc = string.IsNullOrWhiteSpace(description) ? "Depozito irat (gelir)" : description.Trim();
        var record = new DepozitoIrat
        {
            Id = sourceId, CariId = customerId, RentalId = rentalId,
            Tutar = amount, Currency = money.Currency, Kur = resolvedRate,
            Tarih = entryDate, Aciklama = desc
        };
        await _repository.PostDepositTransactionAsync(customerId, shouldCheck: true, record,
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Depozito, AccountRef = customerId,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "DepozitoIrat", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = LedgerAccountType.Gelir, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "DepozitoIrat", SourceId = sourceId, Description = desc }
        ], ct);
        return sourceId;
    }

    /// <summary>F4.4a adversarial MEDIUM-2: cari kiracı içinde var olmalı (RLS + sorgu filtresi; yoksa/başka
    /// kiracınınsa null) — yoksa depozito, hiçbir ekstrede görünmeyen yetim bir AccountRef'e yazılırdı.
    /// Boş kimlik PostAsync'in "Cari seçilmelidir." mesajına bırakılır.</summary>
    private async Task CustomerExistsAsync(Guid customerId, CancellationToken ct)
    {
        if (customerId != Guid.Empty && await _customers.FindAsync(customerId, ct) is null)
            throw new ValidationException("Cari bulunamadı.", "cariId");
    }

    private async Task<Guid> PostAsync(
        Guid customerId, decimal amount, LedgerAccountType account, string? currency, decimal? exchangeRate,
        DateTimeOffset? date, Guid? operationKey, string sourceType, string description,
        LedgerAccountType debit, Guid? debitRef, LedgerAccountType credit, Guid? creditRef, bool shouldCheck,
        CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (customerId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (amount <= 0m) throw new ValidationException("Tutar pozitif olmalıdır.");

        // Kur çözümü (1.1): açık kur (>0) aynen; boş → TRY=1 / döviz KurService (yoksa net red — sessiz 1 YOK).
        var resolvedRate = await _exchangeRateResolver.ResolveAsync(currency, exchangeRate, date, ct);
        var money = new Money(amount, RentACar.Application.Kur.ExchangeRateService.NormalizeCodeStrict(currency), resolvedRate);
        // Bakiye kontrolü repo tx'inde, kilidin arkasında (TOCTOU çiti — adversarial 1.2:
        // eşzamanlı iki iade/irat pre-check'i birlikte geçip tutulanı aşabiliyordu).

        var entryDate = date ?? DateTimeOffset.UtcNow;
        await _lock.EnsureOpenAsync(entryDate, ct); // dönem kilidi: kapalı tarihe depozito işlemi YOK

        var sourceId = operationKey is { } k && k != Guid.Empty ? k : Guid.NewGuid();
        await _repository.PostDepositTransactionAsync(customerId, shouldCheck, auditTrail: null,
        [
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = debit, AccountRef = debitRef,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = sourceType, SourceId = sourceId, Description = description },
            new AccountLedgerEntry { EntryDateUtc = entryDate, AccountType = credit, AccountRef = creditRef,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = sourceType, SourceId = sourceId, Description = description }
        ], ct);
        return sourceId;
    }
}
