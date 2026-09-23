using System.Net;
using Microsoft.EntityFrameworkCore;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    [Fact]
    public async Task MusteriTaksit_plan_oracle_idempotent_odendi_ve_surum()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Muhasebe);
        var defterOnce = await VeriOkuAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync());

        // ORACLE: 1.000 / 3 → 333,33 + 333,33 + 333,34 (son taksit kalanı emer).
        var govde = new { cariId = o.MusteriId, vehicleId = arac, toplamTutar = 1000m, taksitSayisi = 3, ilkVade = DateTimeOffset.UtcNow.AddDays(10) };
        var k = Anahtar();
        var p = await Json(await Gonder(s, HttpMethod.Post, $"{Taksit}/plan", govde, k), HttpStatusCode.Created);
        Assert.Equal(3, p.GetProperty("adet").GetInt32());
        var tekrar = await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Taksit}/plan", govde, k), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(tekrar.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1000m, tekrar.GetProperty("mevcut").GetProperty("tutar").GetDecimal());
        var es = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Gonder(s, HttpMethod.Post, $"{Taksit}/plan", govde, Anahtar())));
        Assert.All(es, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode)); // farklı anahtar = bilinçli yeni plan

        var liste = await Json(await s.C.GetAsync($"{Taksit}?cariId={o.MusteriId}&sirala=sira&boyut=100"));
        Assert.Equal(15, liste.GetProperty("toplam").GetInt32());
        var ilk3 = await VeriOkuAsync(o.TenantId, db => db.MusteriTaksitleri.AsNoTracking()
            .Where(t => p.GetProperty("ids").EnumerateArray().Select(x => x.GetGuid()).Contains(t.Id))
            .OrderBy(t => t.Sira).Select(t => t.TaksitTutari).ToListAsync());
        Assert.Equal([333.33m, 333.33m, 333.34m], ilk3);
        Assert.Equal(5000m, (await Json(await s.C.GetAsync($"{Taksit}/ozet?cariId={o.MusteriId}"))).GetProperty("toplamBaz").GetDecimal());

        // Ödendi: kilit altında; ikinci işaret 409 mukerrer + mevcut; geri-al → Bekliyor.
        var id = p.GetProperty("ids")[0].GetGuid();
        Assert.Equal("Odendi", (await Json(await Gonder(s, HttpMethod.Post, $"{Taksit}/{id}/odendi", new { }))).GetProperty("durum").GetString());
        var m = await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Taksit}/{id}/odendi", new { }), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(m.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var d = await Json(await Gonder(s, HttpMethod.Post, $"{Taksit}/{id}/geri-al"));
        Assert.Equal("Bekliyor", d.GetProperty("durum").GetString());

        // PUT: bayat sürüm 409 cakisma; güncel sürüm 200; sürümsüz 400.
        var surum = d.GetProperty("surum").GetString();
        var put = new { cariId = o.MusteriId, vehicleId = arac, vade = DateTimeOffset.UtcNow.AddDays(20), taksitTutari = 400m, surum };
        Assert.Equal(400m, (await Json(await Gonder(s, HttpMethod.Put, $"{Taksit}/{id}", put))).GetProperty("taksitTutari").GetDecimal());
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Taksit}/{id}", put), HttpStatusCode.Conflict, "cakisma");
        await ProblemBekle(await Gonder(s, HttpMethod.Put, $"{Taksit}/{id}", put with { surum = (string?)null }), HttpStatusCode.BadRequest, "dogrulama", "surum");

        // Giriş kuralları: TRY kur ≠ 1, olmayan cari, anahtarsız.
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Taksit,
            new { cariId = o.MusteriId, vade = DateTimeOffset.UtcNow, taksitTutari = 10m, kur = 2m }, Anahtar()), HttpStatusCode.BadRequest, "dogrulama", "kur");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Taksit,
            new { cariId = Guid.NewGuid(), vade = DateTimeOffset.UtcNow, taksitTutari = 10m }, Anahtar()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Taksit,
            new { cariId = o.MusteriId, vade = DateTimeOffset.UtcNow, taksitTutari = 10m }), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");

        // Takip kaydı DEFTERE YAZMAZ.
        Assert.Equal(defterOnce, await VeriOkuAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync()));

        // Operatör (FinanceWrite yok) 403; başka kiracı 404.
        var op = await GirisAsync(o, Kim.OperatorA);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.C.GetAsync(Taksit)).StatusCode);
        var x = await GirisAsync(await OrtamKurAsync(), Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Taksit}/{id}")).StatusCode);
    }
}
