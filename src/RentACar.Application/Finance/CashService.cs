using RentACar.Application.Authorization;
using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Kasa/Banka işlemleri: tahsilat (cash in), ödeme/tediye (cash out), kasa↔banka virman,
/// ters kayıt. Her işlem DENGELİ çift-taraflı defter kümesi yazar. Düzeltme = ters kayıt.
///
///   Tahsilat: Borç Hesap(Kasa/Banka) / Alacak Cari   → cari bakiye ↓
///   Ödeme:    Borç Cari / Alacak Hesap(Kasa/Banka)    → cari bakiye ↑ (mahsup)
///   Virman:   Borç Hedef / Alacak Kaynak (cari yok)
///   Ters:     orijinalin yönleri çevrilir.
/// </summary>
public sealed class CashService(ICashRepository repository, ILedgerPoster ledger, ICurrentUser currentUser)
{
    private readonly ICashRepository _repository = repository;
    private readonly ILedgerPoster _ledger = ledger;
    private readonly ICurrentUser _currentUser = currentUser;

    public Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<CashTransaction?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetCariBalanceAsync(cariId, ct);

    public Task<IReadOnlyList<AccountLedgerEntry>> GetStatementAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetCariStatementAsync(cariId, ct);

    /// <summary>Tahsilat (cash in): Borç Hesap / Alacak Cari.</summary>
    public Task<Guid> CollectAsync(CashInput input, CancellationToken ct = default)
        => PostCashAsync(input, CashTransactionType.Tahsilat, ct);

    /// <summary>Ödeme/tediye (cash out): Borç Cari / Alacak Hesap.</summary>
    public Task<Guid> PayAsync(CashInput input, CancellationToken ct = default)
        => PostCashAsync(input, CashTransactionType.Odeme, ct);

    private async Task<Guid> PostCashAsync(CashInput input, CashTransactionType tip, CancellationToken ct)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        if (input.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (input.Tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");
        if (input.Kur <= 0) throw new ValidationException("Kur pozitif olmalıdır.");
        EnsureKasaBanka(input.Hesap);

        var money = new Money(input.Tutar, (input.Doviz ?? "TRY").Trim().ToUpperInvariant(), input.Kur);
        var tx = new CashTransaction
        {
            Tip = tip,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            Amount = money,
            KarsiHesap = input.Hesap,
            Aciklama = input.Aciklama
        };

        var entries = Natural(tx);
        // Kira bağlıysa: tahsilat tahsilatı artırır, ödeme (iade) azaltır.
        var delta = tip == CashTransactionType.Tahsilat ? money.AmountInBase : -money.AmountInBase;
        await _repository.PostAsync(tx, entries, rentalTahsilatDelta: delta, ct);
        return tx.Id;
    }

    /// <summary>Kasa↔Banka virman (transfer): Borç Hedef / Alacak Kaynak. Belgesiz (dengeli defter).</summary>
    public async Task TransferAsync(
        LedgerAccountType kaynak, LedgerAccountType hedef, decimal tutar,
        string? doviz = "TRY", decimal kur = 1m, string? aciklama = null, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        EnsureKasaBanka(kaynak);
        EnsureKasaBanka(hedef);
        if (kaynak == hedef) throw new ValidationException("Kaynak ve hedef hesap farklı olmalıdır.");
        if (tutar <= 0) throw new ValidationException("Tutar pozitif olmalıdır.");
        if (kur <= 0) throw new ValidationException("Kur pozitif olmalıdır.");

        var money = new Money(tutar, (doviz ?? "TRY").Trim().ToUpperInvariant(), kur);
        var sourceId = Guid.NewGuid();
        var desc = aciklama ?? $"Virman {kaynak}→{hedef}";
        await _ledger.PostAsync(
        [
            new AccountLedgerEntry { EntryDateUtc = DateTimeOffset.UtcNow, AccountType = hedef, AccountRef = null,
                Direction = LedgerDirection.Debit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc },
            new AccountLedgerEntry { EntryDateUtc = DateTimeOffset.UtcNow, AccountType = kaynak, AccountRef = null,
                Direction = LedgerDirection.Credit, Amount = money, SourceType = "Virman", SourceId = sourceId, Description = desc }
        ], ct);
    }

    public async Task<Guid> ReverseAsync(Guid cashTransactionId, CancellationToken ct = default)
    {
        PermissionGuard.Require(_currentUser, Permission.FinanceWrite);
        var original = await _repository.FindAsync(cashTransactionId, ct)
            ?? throw new ValidationException("İşlem bulunamadı.");
        if (original.TersKayitMi)
            throw new ValidationException("Ters kayıt tekrar ters alınamaz.");
        if (await _repository.HasReversalAsync(cashTransactionId, ct))
            throw new ValidationException("Bu işlem zaten ters kaydedilmiş.");

        var reversal = new CashTransaction
        {
            Tip = original.Tip,
            CariId = original.CariId,
            RentalId = original.RentalId,
            Tarih = DateTimeOffset.UtcNow,
            Amount = original.Amount,
            KarsiHesap = original.KarsiHesap,
            Aciklama = $"Ters kayıt: {original.No}",
            TersKayitMi = true,
            TersAlinanId = original.Id
        };

        // Orijinalle aynı hesap/cari/tutar; doğal yönler çevrilir, ters kayıt tarihiyle.
        var entries = Natural(reversal, flip: true);
        // Ters delta: orijinal tahsilatsa kira tahsilatı azalır, ödemeyse artar.
        var origDelta = original.Tip == CashTransactionType.Tahsilat
            ? original.Amount.AmountInBase : -original.Amount.AmountInBase;
        await _repository.PostAsync(reversal, entries, rentalTahsilatDelta: -origDelta, ct);
        return reversal.Id;
    }

    /// <summary>İşlemin doğal (veya çevrilmiş) dengeli defter kümesi.</summary>
    private static List<AccountLedgerEntry> Natural(CashTransaction tx, Guid? sourceIdOverride = null, bool flip = false)
    {
        // Tahsilat: Hesap Borç, Cari Alacak. Ödeme: Hesap Alacak, Cari Borç.
        var hesapDir = tx.Tip == CashTransactionType.Tahsilat ? LedgerDirection.Debit : LedgerDirection.Credit;
        var cariDir = tx.Tip == CashTransactionType.Tahsilat ? LedgerDirection.Credit : LedgerDirection.Debit;
        if (flip) { hesapDir = Flip(hesapDir); cariDir = Flip(cariDir); }

        var src = flip ? "TersKayit" : (tx.Tip == CashTransactionType.Tahsilat ? "Tahsilat" : "Odeme");
        var sourceId = sourceIdOverride ?? tx.Id;

        return
        [
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = tx.KarsiHesap, AccountRef = null,
                Direction = hesapDir, Amount = tx.Amount, SourceType = src, SourceId = sourceId, Description = tx.Aciklama },
            new AccountLedgerEntry { EntryDateUtc = tx.Tarih, AccountType = LedgerAccountType.Cari, AccountRef = tx.CariId,
                Direction = cariDir, Amount = tx.Amount, SourceType = src, SourceId = sourceId, Description = tx.Aciklama }
        ];
    }

    private static LedgerDirection Flip(LedgerDirection d)
        => d == LedgerDirection.Debit ? LedgerDirection.Credit : LedgerDirection.Debit;

    private static void EnsureKasaBanka(LedgerAccountType hesap)
    {
        if (hesap is not (LedgerAccountType.Kasa or LedgerAccountType.Banka))
            throw new ValidationException("Hesap yalnız Kasa veya Banka olabilir.");
    }
}
