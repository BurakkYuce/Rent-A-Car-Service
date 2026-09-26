using Microsoft.EntityFrameworkCore;
using Npgsql;
using RentACar.Application.Common;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Unique-ihlalini "mükerrer gönderim" (idempotency kısıtı) ile "iş benzersizliği" (belge no, ödeme
/// <c>Sira</c>) arasında ayırır — F1.1 hata sözleşmesi. Yeni SPA 409 <c>mukerrer</c>'de kaydı yeniden
/// yükler; iş benzersizliğinde ise formu korur. Karar YALNIZ <c>PostgresException.ConstraintName</c>'e
/// bakar (mesaj metni değil — Npgsql/PG sürümünde değişebilir).
///
/// <para>Tanınan kısıtlar: adı <c>_IslemAnahtari</c> / <c>_Anahtar</c> / <c>_Idem</c> ile biten kısmi
/// unique index'ler + iki açık ad: HGS defter idempotency'si
/// (<c>IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction</c>) ve aynı işlemin ikinci ters
/// kaydı (<c>IX_CashTransactions_TenantId_TersAlinanId</c>). Bu iki adın EF modelinde var olduğu testle
/// kilitlidir (yeniden adlandırılırsa test kırılır).</para>
/// </summary>
public static class IdempotencyConstraint
{
    public const string HgsLedgerIdem = "IX_AccountLedgerEntries_TenantId_SourceType_SourceId_Direction";
    public const string SecondReversal = "IX_CashTransactions_TenantId_TersAlinanId";

    /// <summary>Kısıt adı bir idempotency kısıtına mı ait? Null/boş → false.</summary>
    public static bool IsDuplicateConstraint(string? constraintName)
    {
        if (string.IsNullOrEmpty(constraintName)) return false;
        return constraintName.EndsWith("_IslemAnahtari", StringComparison.Ordinal)
            || constraintName.EndsWith("_Anahtar", StringComparison.Ordinal)
            || constraintName.EndsWith("_Idem", StringComparison.Ordinal)
            || constraintName == HgsLedgerIdem
            || constraintName == SecondReversal;
    }

    /// <summary>
    /// Çift-gönderim catch bloklarının fırlatacağı istisna: idempotency kısıtıysa
    /// <see cref="DuplicateOperationException"/>, değilse (ör. <c>_Sira</c>, <c>_No</c>) AYNI mesajlı düz
    /// <see cref="ValidationException"/> — o yolların davranışı birebir eskisi gibi kalır.
    /// </summary>
    public static ValidationException Red(DbUpdateException ex, string message) =>
        IsDuplicateConstraint((ex.InnerException as PostgresException)?.ConstraintName)
            ? new DuplicateOperationException(message)
            : new ValidationException(message);
}
