using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;

namespace RentACar.Web.Identity;

/// <summary>
/// Kabul B-0 (b): şube FK claim'i BOŞ ama şube ADI dolu operatör oturumunda FK'yı şube adından çözer.
/// <para>Neden: şube kapsamının FK'sız metin yolu (<c>BranchScope.InScope</c>, C5 — KALICI) kaydın metnini kullanıcının
/// şube adıyla karşılaştırır. Kira/rezervasyon/teklifte o metin ÇIKIŞ OFİSİNİN adıdır ("O1 Havalimanı") — şube adı
/// ("Merkez") ile hiç eşleşmez; FK'sız operatör kendi şubesinin ofisinde bile kira açamıyordu. Çözüm 22+ SQL şablonunu
/// ve guard'ı tek tek değiştirmek yerine kimlikte: kullanıcının şube adı şubeye (Branch) çözülünce FK yolu devreye girer
/// ve kayıt, OFİSİN BAĞLI OLDUĞU ŞUBEYLE (türetilmiş <c>CikisSubeId</c>) karşılaştırılır — guard'lar ve listeler AYNI
/// kuraldan geçer. Ofisi hiçbir şubeye bağlı olmayan kayıtta C5 metin yolu aynen kalır.</para>
/// <para>Kaynak sırası: (1) Users satırının FK'sı (açılış backfill'i ya da interceptor doldurmuşsa — oturum FK'dan önce
/// açılmış olabilir), yalnız satırdaki şube adı claim'dekiyle AYNIYSA; (2) Branches'ta ad eşleşmesi —
/// <see cref="BranchNameMatch"/> — <see cref="BranchBackfill"/> ile AYNI kural (harf duyarsız aday; birebir ad → aktif
/// şube → Kod sırası). Çözülemezse boş kalır
/// (metin yolu; daraltma yok, genişletme yok). Sonuç (boş dahil) 60 sn önbellekte — FK'lı oturum hiç sorgu yapmaz.</para>
/// <para>TUZAK (bedeli ödendi): sonuç CLAIM'e YAZILMAZ — antiforgery belirteci, NameIdentifier claim'i olmadığında TÜM
/// claim kümesine bağlanır; claim eklemek girişte verilen XSRF belirtecini geçersiz kılıp her yazmayı 400
/// <c>xsrf_gecersiz</c>'e düşürüyordu. Değer istek öğesine (<see cref="ItemKey"/>) konur;
/// <see cref="HttpContextIdentity.AssignedBranchId"/> claim boşsa onu okur.</para>
/// </summary>
public sealed class AssignedBranchResolutionMiddleware(IMemoryCache cache, IConfiguration config) : IMiddleware
{
    /// <summary><c>HttpContext.Items</c> anahtarı (çözülmüş şube FK'sı, <see cref="Guid"/>).</summary>
    public const string ItemKey = "racar.assigned-branch-id";

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    /// <summary>Saf karar: çözüm gereken oturum mu? (operatör + tenant/kullanıcı kimliği + şube adı dolu + FK boş)</summary>
    public static bool NeedsResolution(ClaimsPrincipal principal, out Guid tenantId, out Guid userId, out string branchName)
    {
        tenantId = default; userId = default; branchName = "";
        if (principal.Identity?.IsAuthenticated != true) return false;
        if (!Enum.TryParse<UserRole>(principal.FindFirst(ClaimTypes.Role)?.Value, out var role) || role != UserRole.Operator)
            return false;
        if (!string.IsNullOrWhiteSpace(principal.FindFirst(IdentityClaims.AssignedBranchId)?.Value)) return false;
        var name = principal.FindFirst(IdentityClaims.AssignedBranch)?.Value;
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!Guid.TryParse(principal.FindFirst(IdentityClaims.TenantId)?.Value, out tenantId)) return false;
        if (!Guid.TryParse(principal.FindFirst(IdentityClaims.UserId)?.Value, out userId)) return false;
        branchName = name.Trim();
        return true;
    }

    public async Task InvokeAsync(HttpContext ctx, RequestDelegate next)
    {
        if (NeedsResolution(ctx.User, out var tenantId, out var userId, out var name))
        {
            var key = $"sube-fk:{tenantId}:{userId}:{name}";
            if (!cache.TryGetValue(key, out Guid? branchId))
            {
                branchId = await ResolveAsync(tenantId, userId, name, ctx.RequestAborted);
                cache.Set(key, branchId, Ttl);
            }
            if (branchId is { } id) ctx.Items[ItemKey] = id;
        }
        await next(ctx);
    }

    private async Task<Guid?> ResolveAsync(Guid tenantId, Guid userId, string name, CancellationToken ct)
    {
        var appConn = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default eksik.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(appConn).Options;

        // (1) Users platform tablosu (RLS yok) — kimlik claim'den; başka kiracının satırı TenantId koşuluyla dışarıda.
        await using (var db0 = new AppDbContext(options, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var row = await db0.Users.AsNoTracking().IgnoreQueryFilters()
                .Where(u => u.Id == userId && u.TenantId == tenantId)
                .Select(u => new { u.AtanmisSube, u.AtanmisSubeId })
                .FirstOrDefaultAsync(ct);
            if (row?.AtanmisSubeId is { } fk && string.Equals(row.AtanmisSube?.Trim(), name, StringComparison.Ordinal))
                return fk;
        }

        // (2) Branches — tenant-owned (RLS FORCE): SystemTenantContext (EF filtresi) + GUC (RLS) çift izolasyon.
        var sys = new SystemTenantContext { TenantId = tenantId };
        await using var db = new AppDbContext(options, sys, sys);
        await TenantGuc.OpenAsync(db, tenantId, ct);
        return await BranchNameMatch.ResolveAsync(db, tenantId, name, ct); // güvenlik F5: backfill ile TEK kural
    }
}
