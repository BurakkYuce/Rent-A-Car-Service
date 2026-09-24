using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

public sealed partial class UiSystemSecurityTests
{
    // M5 — /ara adı anonimleştirilmiş cariyi gerçek adıyla bulmamalı ve adını göstermemeli.
    [Fact]
    public async Task Search_does_not_match_or_show_anonymised_customer_names()
    {
        var e = await _kit.SetupAsync();
        var tag = Random("zq");
        var hidden = new Customer { Tip = CariType.Bireysel, Ad = tag + "gizli", Soyad = "Saklı", AnonimAd = true };
        var visible = new Customer { Tip = CariType.Bireysel, Ad = tag + "acik", Soyad = "Görünür" };
        await _kit.WriteAsync(e.TenantId, db => { db.Customers.Add(hidden); db.Customers.Add(visible); });
        var op = await _kit.LoginAsync(e, Who.OperatorA);

        var byRealName = await Json(await op.C.GetAsync(V1 + "/ara?q=" + tag));
        var titles = byRealName.EnumerateArray().Where(h => h.GetProperty("tur").GetString() == "Cari")
            .Select(h => h.GetProperty("baslik").GetString()).ToList();
        Assert.Equal([tag + "acik"], titles);
        Assert.DoesNotContain(tag + "gizli", byRealName.ToString(), StringComparison.Ordinal);

        var byLabel = await Json(await op.C.GetAsync(V1 + "/ara?q=Anonim"));
        var anon = Assert.Single(byLabel.EnumerateArray(), h => h.GetProperty("url").GetString() == $"/cariler/{hidden.Id}");
        Assert.Equal("Anonim müşteri", anon.GetProperty("baslik").GetString());
        Assert.DoesNotContain(tag + "gizli", byLabel.ToString(), StringComparison.Ordinal);
    }
}
