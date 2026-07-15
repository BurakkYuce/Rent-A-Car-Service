using System.Security.Claims;
using RentACar.Web.Identity;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 5-C1 — assigned_sube_id claim çift-yazımı. Hiçbir OKUMA davranışı değişmez (yetki C2'de);
/// bu testler yalnız taşıma katmanını kilitler: claim varsa parse, yoksa/eskiyse null (kilitlenme yok).
/// </summary>
public sealed class SubeClaimTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void Circuit_yeni_claimli_oturum_id_parse_eder()
    {
        var subeId = Guid.NewGuid();
        var ctx = new CircuitTenantContext();
        ctx.SetFrom(Principal(
            new Claim(IdentityClaims.TenantId, Guid.NewGuid().ToString()),
            new Claim(IdentityClaims.AssignedBranch, "Merkez"),
            new Claim(IdentityClaims.AssignedBranchId, subeId.ToString())));

        Assert.Equal("Merkez", ctx.AssignedBranch);
        Assert.Equal(subeId, ctx.AssignedBranchId);
    }

    [Fact]
    public void Circuit_eski_oturum_claimsiz_null_hatasiz()
    {
        // Deploy öncesi açılmış oturum: yeni claim yok → null (metin yolu aynen çalışır — kilitlenme önleyici).
        var ctx = new CircuitTenantContext();
        ctx.SetFrom(Principal(new Claim(IdentityClaims.AssignedBranch, "Merkez")));
        Assert.Equal("Merkez", ctx.AssignedBranch);
        Assert.Null(ctx.AssignedBranchId);

        // Boş/bozuk değer de null'a düşer.
        var ctx2 = new CircuitTenantContext();
        ctx2.SetFrom(Principal(new Claim(IdentityClaims.AssignedBranchId, "not-a-guid")));
        Assert.Null(ctx2.AssignedBranchId);
    }
}
