using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// F6.1b — "Id = işlem anahtarı" deseniyle oluşturulan kayıtlarda (araç kredisi, sipariş, BAF, hasar dosyası, müşteri
/// taksiti) aynı anahtarla eşzamanlı ikinci oluşturma birincil anahtara (<c>PK_…</c>) çarpar. Bu bir iş benzersizliği
/// değil, mükerrer gönderimdir → <see cref="Application.Common.MukerrerIslemException"/> (409 <c>mukerrer</c>).
/// Karar yalnız kısıt ADINA bakar (<see cref="IdempotencyKisiti"/> ile aynı ilke); başka unique ihlaller eşlenmez.
/// </summary>
public static class PkIhlali
{
    public const string Mesaj = "Bu kayıt zaten oluşturulmuş (çift gönderim); yeni kayıt yazılmadı.";

    public static bool Mi(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } p
           && p.ConstraintName is { } ad && ad.StartsWith("PK_", StringComparison.Ordinal);
}
