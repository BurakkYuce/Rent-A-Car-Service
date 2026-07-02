using Npgsql;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Postgres GEÇİCİ çakışma hatalarında (40P01 deadlock_detected, 40001 serialization_failure)
/// yeniden deneme (P0-5). Deadlock'ta Postgres transaction'ı zaten geri almıştır → aynı işlemi
/// baştan koşmak güvenlidir; kalıcı çift-kayıt riski ayrıca idempotency indeksleriyle kapalıdır.
/// KURAL: sarılan gövde TAM transaction'ı kapsamalı ve her denemede TAZE DbContext açmalıdır
/// (factory çağrısı gövdenin İÇİNDE). İş kuralı hataları (ValidationException, UniqueViolation
/// dönüşümleri) retry EDİLMEZ — yalnız 40P01/40001.
/// </summary>
public static class PgRetry
{
    private const int MaxAttempts = 3;

    public static async Task RunAsync(Func<Task> action, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransientConflict(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct); // deadlock_timeout ~1sn — kurbanın rakibi bitirsin
            }
        }
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < MaxAttempts && IsTransientConflict(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), ct);
            }
        }
    }

    /// <summary>
    /// Deadlock/serialization hatası mı? Zincirin TAMAMI yürünür: EF execution strategy SaveChanges
    /// hatasını InvalidOperationException("transient failure") → DbUpdateException → PostgresException
    /// diye sarar (adversarial H-1); commit/raw yolunda PostgresException doğrudan gelir. SqlState
    /// kapısı sayesinde 23505/23P01 (iş kuralı) asla retry edilmez.
    /// </summary>
    public static bool IsTransientConflict(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException pg)
                return pg.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure;
        return false;
    }
}
