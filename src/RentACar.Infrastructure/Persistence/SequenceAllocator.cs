using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Tenant-başına boşluksuz sıra tahsisi. Çağıran AKTİF bir transaction içinde
/// çalıştırmalı → rollback numarayı geri alır (boşluk olmaz). UPSERT + RETURNING
/// atomiktir; aynı (tenant, name) için eşzamanlı tahsisler satır kilidiyle serileşir.
/// (INSERT...RETURNING EF SqlQuery ile sarmalanamadığından ham ADO ile çalıştırılır.)
/// </summary>
public static class SequenceAllocator
{
    public static async Task<long> NextAsync(AppDbContext db, Guid tenantId, string name, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText =
            "INSERT INTO \"TenantSequences\" (\"TenantId\", \"Name\", \"NextValue\") VALUES (@tenant, @name, 1) " +
            "ON CONFLICT (\"TenantId\", \"Name\") " +
            "DO UPDATE SET \"NextValue\" = \"TenantSequences\".\"NextValue\" + 1 " +
            "RETURNING \"NextValue\"";

        var pt = cmd.CreateParameter();
        pt.ParameterName = "tenant";
        pt.Value = tenantId;
        cmd.Parameters.Add(pt);

        var pn = cmd.CreateParameter();
        pn.ParameterName = "name";
        pn.Value = name;
        cmd.Parameters.Add(pn);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}
