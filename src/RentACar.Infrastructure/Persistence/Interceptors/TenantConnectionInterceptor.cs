using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentACar.Domain.Common;

namespace RentACar.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Her bağlantı açıldığında <c>app.tenant_id</c> GUC'unu set eder. Postgres RLS
/// policy'leri bu GUC'a göre satırları filtreler → asıl tenant izolasyon SINIRI budur
/// (EF query filter yalnız ergonomi; raw SQL / IgnoreQueryFilters onu aşar, RLS aşamaz).
///
/// Tenant yoksa GUC '' (boş) bırakılır; policy NULLIF(...,'')::uuid ile bunu NULL'a
/// çevirir → hiçbir satır eşleşmez (default-deny). Npgsql pooling'de GUC fiziksel
/// bağlantıda kalıcı olduğundan HER açılışta yeniden set edilir.
///
/// Scoped servis: geçerli scope'un ITenantContext'ini okur.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext = tenantContext;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyTenant(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ApplyTenantAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private string TenantValue()
    {
        var tenant = _tenantContext.TenantId;
        return tenant.HasValue && tenant.Value != Guid.Empty
            ? tenant.Value.ToString()
            : string.Empty; // default-deny
    }

    private void ApplyTenant(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
        AddParam(cmd, TenantValue());
        cmd.ExecuteNonQuery();
    }

    private async Task ApplyTenantAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
        AddParam(cmd, TenantValue());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand cmd, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = "tenant";
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
