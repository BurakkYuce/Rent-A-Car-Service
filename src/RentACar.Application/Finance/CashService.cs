using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Nakit tahsilat + ters kayıt. Her işlem DENGELİ çift-taraflı defter kümesi yazar ve
/// cari bakiyesini günceller. Düzeltme = ters kayıt (asla güncelleme/silme).
///
/// Tahsilat: Borç Kasa, Alacak Cari → cari bakiye ↓.
/// Ters kayıt: Borç Cari, Alacak Kasa → bakiye eski haline.
/// </summary>
public sealed class CashService(ICashRepository repository)
{
    private readonly ICashRepository _repository = repository;

    public Task<IReadOnlyList<CashTransaction>> ListAsync(CancellationToken ct = default)
        => _repository.ListAsync(ct);

    public Task<CashTransaction?> GetAsync(Guid id, CancellationToken ct = default)
        => _repository.FindAsync(id, ct);

    public Task<decimal> GetCariBalanceAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetCariBalanceAsync(cariId, ct);

    public Task<IReadOnlyList<AccountLedgerEntry>> GetStatementAsync(Guid cariId, CancellationToken ct = default)
        => _repository.GetCariStatementAsync(cariId, ct);

    public async Task<Guid> CollectAsync(CashInput input, CancellationToken ct = default)
    {
        if (input.CariId == Guid.Empty) throw new ValidationException("Cari seçilmelidir.");
        if (input.Tutar <= 0) throw new ValidationException("Tahsilat tutarı pozitif olmalıdır.");
        if (input.Kur <= 0) throw new ValidationException("Kur pozitif olmalıdır.");

        var money = new Money(input.Tutar, (input.Doviz ?? "TRY").Trim().ToUpperInvariant(), input.Kur);
        var tx = new CashTransaction
        {
            Tip = CashTransactionType.Tahsilat,
            CariId = input.CariId,
            RentalId = input.RentalId,
            Tarih = input.Tarih ?? DateTimeOffset.UtcNow,
            Amount = money,
            KarsiHesap = LedgerAccountType.Kasa,
            Aciklama = input.Aciklama
        };

        var entries = BuildEntries(tx, reversal: false);
        await _repository.PostAsync(tx, entries, rentalTahsilatDelta: money.AmountInBase, ct);
        return tx.Id;
    }

    public async Task<Guid> ReverseAsync(Guid cashTransactionId, CancellationToken ct = default)
    {
        var original = await _repository.FindAsync(cashTransactionId, ct)
            ?? throw new ValidationException("İşlem bulunamadı.");
        if (original.TersKayitMi)
            throw new ValidationException("Ters kayıt tekrar ters alınamaz.");
        // Idempotency: aynı işlem birden çok kez ters kaydedilemez (uygulama ön-kontrolü;
        // yarış durumu DB'deki kısmi unique index ile kapatılır).
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

        var entries = BuildEntries(reversal, reversal: true);
        await _repository.PostAsync(reversal, entries, rentalTahsilatDelta: -original.Amount.AmountInBase, ct);
        return reversal.Id;
    }

    /// <summary>Tahsilat: Borç Kasa / Alacak Cari. Ters kayıt: tersi. (DENGELİ.)</summary>
    private static List<AccountLedgerEntry> BuildEntries(CashTransaction tx, bool reversal)
    {
        var kasaDir = reversal ? LedgerDirection.Credit : LedgerDirection.Debit;
        var cariDir = reversal ? LedgerDirection.Debit : LedgerDirection.Credit;
        var src = reversal ? "TersKayit" : "Tahsilat";

        return
        [
            new AccountLedgerEntry
            {
                EntryDateUtc = tx.Tarih, AccountType = tx.KarsiHesap, AccountRef = null,
                Direction = kasaDir, Amount = tx.Amount, SourceType = src, SourceId = tx.Id,
                Description = tx.Aciklama
            },
            new AccountLedgerEntry
            {
                EntryDateUtc = tx.Tarih, AccountType = LedgerAccountType.Cari, AccountRef = tx.CariId,
                Direction = cariDir, Amount = tx.Amount, SourceType = src, SourceId = tx.Id,
                Description = tx.Aciklama
            }
        ];
    }
}
