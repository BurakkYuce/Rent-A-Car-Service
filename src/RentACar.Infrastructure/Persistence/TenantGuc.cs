using Microsoft.EntityFrameworkCore;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// RAW (interceptor'sız) context'lerde tenant GUC açılışının TEK doğru yolu (denetim O12c: 7 elle kopya vardı;
/// kritik invariant yalnız yorumlarda yaşıyordu). SIRALAMA ZORUNLU: önce bağlantı AÇILIR (GUC bağlantı ömrünce
/// yaşar — bağlantı kapanıp havuza dönerse GUC sıfırlanır ve RLS SESSİZCE 0 satır döndürür), sonra
/// app.tenant_id set edilir. DI yolunda bunu TenantConnectionInterceptor yapar; bu helper yalnız job/backfill/
/// seeder gibi raw-context akışları içindir.
/// </summary>
public static class TenantGuc
{
    public static async Task OpenAsync(AppDbContext db, Guid tenantId, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct); // GUC bağlantı ömrünce açık kalmalı
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT set_config('app.tenant_id', {tenantId.ToString()}, false)", ct);
    }
}
