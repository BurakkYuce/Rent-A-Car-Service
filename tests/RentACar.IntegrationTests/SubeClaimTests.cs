using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 5-C1 — assigned_sube_id claim çift-yazımı. Hiçbir OKUMA davranışı değişmez (yetki C2'de);
/// bu testler yalnız taşıma katmanını kilitler: claim varsa parse, yoksa/eskiyse null (kilitlenme yok).
/// F13.1b: Blazor circuit bağlamı (<c>CircuitTenantContext</c>) kalktı; tek kimlik kaynağı <see cref="HttpContextIdentity"/>.
/// </summary>
public sealed class SubeClaimTests
{
    private static HttpContextIdentity Identity(params Claim[] claims)
        => new(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity(claims, authenticationType: "test")) },
        });

    [Fact]
    public void Claimli_oturum_id_parse_eder()
    {
        var branchId = Guid.NewGuid();
        var ctx = Identity(
            new Claim(IdentityClaims.TenantId, Guid.NewGuid().ToString()),
            new Claim(IdentityClaims.AssignedBranch, "Merkez"),
            new Claim(IdentityClaims.AssignedBranchId, branchId.ToString()));

        Assert.Equal("Merkez", ctx.AssignedBranch);
        Assert.Equal(branchId, ctx.AssignedBranchId);
    }

    [Fact]
    public void Eski_oturum_claimsiz_null_hatasiz()
    {
        // Deploy öncesi açılmış oturum: yeni claim yok → null (metin yolu aynen çalışır — kilitlenme önleyici).
        var ctx = Identity(new Claim(IdentityClaims.AssignedBranch, "Merkez"));
        Assert.Equal("Merkez", ctx.AssignedBranch);
        Assert.Null(ctx.AssignedBranchId);

        // Boş/bozuk değer de null'a düşer.
        Assert.Null(Identity(new Claim(IdentityClaims.AssignedBranchId, "not-a-guid")).AssignedBranchId);
    }
}
