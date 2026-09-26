using RentACar.Domain.Entities;

namespace RentACar.Application.Expenses;

/// <summary>Toplu giderde tek satır: gider belgesi + dengeli defter kümesi.</summary>
public sealed record ExpensePosting(Expense Expense, IReadOnlyList<AccountLedgerEntry> Entries);

public interface IExpenseRepository
{
    /// <summary>
    /// Giderler; <paramref name="scope"/> şube-kapsamı (Unrestricted = tümü).
    /// <paramref name="filter"/> null → eski davranış (kapsamdaki tüm giderler); FAZ-63 arama paneli.
    /// Filtre kapsamı DARALTIR, asla genişletmez.
    /// </summary>
    Task<IReadOnlyList<Expense>> ListAsync(
        Authorization.BranchScope.BranchFilter scope, ExpenseFilter? filter = null, CancellationToken ct = default);
    Task<Expense?> FindAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// FAZ-64 — kısmi ödeme kaydı. Kalan kontrolü, sıra tahsisi ve yazma AYNI transaction'da,
    /// <c>(tenant, gider)</c> danışma kilidinin ARKASINDA yapılır (kilitsiz "önce oku sonra yaz"
    /// TOCTOU'dur; bu repoda tam o sınıf bir hata canlı para hatası üretti).
    /// <paramref name="amount"/> null → kalanın tamamı. Aynı işlem anahtarıyla ikinci gönderim
    /// sessizce yutulur (kısmi unique index).
    /// </summary>
    Task<GiderOdeme?> AddPaymentAsync(
        Guid expenseId, decimal? amount, DateTimeOffset date, string? receiptNo, string? description,
        string? performedBy, Guid? operationKey, CancellationToken ct = default);

    /// <summary>FAZ-64 — verilen giderler için ödenen toplamlar (ExpenseId → Σ Tutar).</summary>
    Task<Dictionary<Guid, decimal>> PaidTotalsAsync(
        IReadOnlyCollection<Guid> expenseIds, CancellationToken ct = default);

    /// <summary>FAZ-64 — bir giderin ödeme geçmişi (sıraya göre).</summary>
    Task<IReadOnlyList<GiderOdeme>> ListPaymentsAsync(Guid expenseId, CancellationToken ct = default);

    /// <summary>
    /// Gider belgesi + DENGELİ defter kümesini TEK transaction'da işler. No boşluksuz tahsis
    /// edilir; gider/defter DB-seviyesinde değişmez (trigger).
    /// </summary>
    Task PostAsync(Expense expense, IReadOnlyList<AccountLedgerEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// Toplu gider: çok kalem dengeli kayıt + No tahsisi TEK transaction'da (ATOMİK hep-ya-hiç).
    /// Bir kalem geçersiz/çakışırsa hiçbiri yazılmaz; No boşluğu oluşmaz. IslemAnahtari kısmi unique
    /// index → aynı toplu giderin çift-submit'i UniqueViolation ile tüm batch'i geri alır (idempotent).
    /// </summary>
    Task PostBatchAsync(IReadOnlyList<ExpensePosting> items, CancellationToken ct = default);
}
