using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace RentACar.Infrastructure.Persistence;

/// <summary>
/// Güvenlik F5 — serbest-metin şube adını şubeye (Branch) çeviren TEK kural (SQL). <see cref="BranchBackfill"/> (toplu
/// doldurma) ve oturumdaki FK'sız operatörün şube çözümü (<c>AssignedBranchResolutionMiddleware</c>) aynı parçaları
/// kullanır; iki yolun farklı şubeye çözmesi kapsam kararını istek yoluna göre değiştirirdi.
/// <list type="number">
/// <item>Aday: <c>lower(btrim(Ad)) = lower(btrim(ad))</c> (harf duyarsız).</item>
/// <item>Sıra: önce BİREBİR (boşluk kırpılmış, harf duyarlı) eşleşme, sonra aktif şube, sonra Kod — böylece "Merkez"
/// yazan kullanıcı, Kod'u önce gelen "MERKEZ" adlı başka/pasif şubeye değil kendi şubesine çözülür.</item>
/// </list>
/// </summary>
public static class BranchNameMatch
{
    /// <summary>WHERE parçası: <paramref name="branch"/> takma adlı Branches satırı, <paramref name="name"/> SQL ifadesi.</summary>
    public static string Where(string branch, string name)
        => $"lower(btrim({branch}.\"Ad\")) = lower(btrim({name}))";

    /// <summary>ORDER BY parçası (birebir → aktif → Kod).</summary>
    public static string OrderBy(string branch, string name)
        => $"(btrim({branch}.\"Ad\") = btrim({name})) DESC, {branch}.\"Aktif\" DESC, {branch}.\"Kod\"";

    /// <summary>
    /// Tek ad çözümü (çağıran bağlamın tenant GUC'u açık olmalı — <see cref="TenantGuc.OpenAsync"/>). Eşleşme yoksa null.
    /// </summary>
    public static async Task<Guid?> ResolveAsync(AppDbContext db, Guid tenantId, string name, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT b."Id" AS "Value" FROM "Branches" b
            WHERE b."TenantId" = @t AND {Where("b", "@n")}
            ORDER BY {OrderBy("b", "@n")}
            LIMIT 1
            """;
        var ids = await db.Database.SqlQueryRaw<Guid>(sql,
            new NpgsqlParameter("t", tenantId), new NpgsqlParameter("n", name)).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    /// <summary>Bellek-içi aday (yazım anı interceptor'u).</summary>
    public readonly record struct Candidate(Guid Id, string Ad, string Kod, bool Aktif);

    /// <summary>
    /// #379 L1 — aynı kuralın bellek-içi karşılığı (<c>BranchFkInterceptor</c>): harf duyarsız aday, sıra birebir
    /// (kırpılmış, harf duyarlı) ad → aktif şube → Kod. Eşleşme yoksa null.
    /// </summary>
    public static Guid? Pick(IEnumerable<Candidate> branches, string name)
    {
        var trimmed = name.Trim();
        var key = trimmed.ToLowerInvariant();
        return branches
            .Where(b => b.Ad.Trim().ToLowerInvariant() == key)
            .OrderByDescending(b => string.Equals(b.Ad.Trim(), trimmed, StringComparison.Ordinal))
            .ThenByDescending(b => b.Aktif)
            .ThenBy(b => b.Kod, StringComparer.Ordinal)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefault();
    }
}
