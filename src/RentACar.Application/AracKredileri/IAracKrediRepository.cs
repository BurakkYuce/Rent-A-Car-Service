using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.Application.AracKredileri;

/// <summary>Araç kredisi kalıcılığı (roadmap L4). CreateAsync boşluksuz No (KR-000001) tahsis eder.</summary>
public interface IAracKrediRepository
{
    Task<IReadOnlyList<AracKredi>> ListAsync(CancellationToken ct = default);
    Task<AracKredi?> FindAsync(Guid id, CancellationToken ct = default);
    Task CreateAsync(AracKredi row, CancellationToken ct = default);
    /// <summary>Taksit öde — FOR UPDATE satır kilidi altında sayaç + (FAZ 1.3) taksit GİDERİ aynı tx'te:
    /// <paramref name="posting"/> ödenen SIRA ile çağrılır, dönen Expense+dengeli defter çifti yazılır
    /// (ExpenseNo tahsisli). IslemAnahtari çift-submit'i kısmi unique index yakalar → ValidationException.</summary>
    Task<bool> TaksitOdeAsync(Guid id,
        Func<int, (Expense Expense, IReadOnlyList<AccountLedgerEntry> Entries)>? posting = null,
        CancellationToken ct = default);
    Task<bool> SetDurumAsync(Guid id, KrediDurum durum, CancellationToken ct = default);
}
