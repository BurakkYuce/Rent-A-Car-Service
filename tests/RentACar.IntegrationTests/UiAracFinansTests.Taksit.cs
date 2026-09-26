using System.Net;
using Microsoft.EntityFrameworkCore;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    [Fact]
    public async Task MusteriTaksit_plan_oracle_idempotent_odendi_ve_surum()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Muhasebe);
        var ledgerBefore = await ReadDataAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync());

        // ORACLE: 1.000 / 3 → 333,33 + 333,33 + 333,34 (son taksit kalanı emer).
        var body = new { cariId = o.MusteriId, vehicleId = vehicle, toplamTutar = 1000m, taksitSayisi = 3, ilkVade = DateTimeOffset.UtcNow.AddDays(10) };
        var k = Key();
        var p = await Json(await Gonder(s, HttpMethod.Post, $"{Installment}/plan", body, k), HttpStatusCode.Created);
        Assert.Equal(3, p.GetProperty("adet").GetInt32());
        var repeat = await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Installment}/plan", body, k), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(repeat.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1000m, repeat.GetProperty("mevcut").GetProperty("tutar").GetDecimal());
        var es = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Gonder(s, HttpMethod.Post, $"{Installment}/plan", body, Key())));
        Assert.All(es, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode)); // farklı anahtar = bilinçli yeni plan

        var list = await Json(await s.C.GetAsync($"{Installment}?cariId={o.MusteriId}&sirala=sira&boyut=100"));
        Assert.Equal(15, list.GetProperty("toplam").GetInt32());
        var first3 = await ReadDataAsync(o.TenantId, db => db.MusteriTaksitleri.AsNoTracking()
            .Where(t => p.GetProperty("ids").EnumerateArray().Select(x => x.GetGuid()).Contains(t.Id))
            .OrderBy(t => t.Sira).Select(t => t.TaksitTutari).ToListAsync());
        Assert.Equal([333.33m, 333.33m, 333.34m], first3);
        Assert.Equal(5000m, (await Json(await s.C.GetAsync($"{Installment}/ozet?cariId={o.MusteriId}"))).GetProperty("toplamBaz").GetDecimal());

        // Ödendi: kilit altında; ikinci işaret 409 mukerrer + mevcut; geri-al → Bekliyor.
        var id = p.GetProperty("ids")[0].GetGuid();
        Assert.Equal("Odendi", (await Json(await Gonder(s, HttpMethod.Post, $"{Installment}/{id}/odendi", new { }))).GetProperty("durum").GetString());
        var m = await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Installment}/{id}/odendi", new { }), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(m.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var d = await Json(await Gonder(s, HttpMethod.Post, $"{Installment}/{id}/geri-al"));
        Assert.Equal("Bekliyor", d.GetProperty("durum").GetString());

        // PUT: bayat sürüm 409 cakisma; güncel sürüm 200; sürümsüz 400.
        var version = d.GetProperty("surum").GetString();
        var put = new { cariId = o.MusteriId, vehicleId = vehicle, vade = DateTimeOffset.UtcNow.AddDays(20), taksitTutari = 400m, surum = version };
        Assert.Equal(400m, (await Json(await Gonder(s, HttpMethod.Put, $"{Installment}/{id}", put))).GetProperty("taksitTutari").GetDecimal());
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Installment}/{id}", put), HttpStatusCode.Conflict, "cakisma");
        await ExpectProblem(await Gonder(s, HttpMethod.Put, $"{Installment}/{id}", put with { surum = (string?)null }), HttpStatusCode.BadRequest, "dogrulama", "surum");

        // Giriş kuralları: TRY kur ≠ 1, olmayan cari, anahtarsız.
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Installment,
            new { cariId = o.MusteriId, vade = DateTimeOffset.UtcNow, taksitTutari = 10m, kur = 2m }, Key()), HttpStatusCode.BadRequest, "dogrulama", "kur");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Installment,
            new { cariId = Guid.NewGuid(), vade = DateTimeOffset.UtcNow, taksitTutari = 10m }, Key()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Installment,
            new { cariId = o.MusteriId, vade = DateTimeOffset.UtcNow, taksitTutari = 10m }), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");

        // Takip kaydı DEFTERE YAZMAZ.
        Assert.Equal(ledgerBefore, await ReadDataAsync(o.TenantId, db => db.AccountLedgerEntries.CountAsync()));

        // Operatör (FinanceWrite yok) 403; başka kiracı 404.
        var op = await LoginAsync(o, Kim.OperatorA);
        Assert.Equal(HttpStatusCode.Forbidden, (await op.C.GetAsync(Installment)).StatusCode);
        var x = await LoginAsync(await SetUpEnvironmentAsync(), Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Installment}/{id}")).StatusCode);
    }
}
