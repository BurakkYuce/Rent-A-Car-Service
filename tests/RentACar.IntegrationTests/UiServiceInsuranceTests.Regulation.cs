using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiServiceInsuranceTests
{
    private const string Reg = V1 + "/regulasyon";

    private Task<int> MtvPaymentCountAsync(Guid tenant) => ReadAsync(tenant, db => db.MtvOdemeleri.AsNoTracking().CountAsync());

    [Fact]
    public async Task Mtv_partial_payment_oracle_idempotent_balanced_and_stale_screen_conflict()
    {
        var e = await SetupAsync();
        var car = await VehicleAsync(e);
        var s = await LoginAsync(e, Who.Admin);

        // ORACLE: MTV 1.000 TL → 400 + 600 (two payments), each balanced Gider(car) / Kasa.
        var mtv = await Json(await Send(s, HttpMethod.Post, $"{Reg}/mtv",
            new { vehicleId = car, donem = "2026-1", tutar = 1000m, vade = TestZaman.GunSonra(30) }, Key()), HttpStatusCode.Created);
        var id = mtv.GetProperty("mtv").GetProperty("id").GetGuid();
        Assert.Equal(1000m, mtv.GetProperty("mtv").GetProperty("kalan").GetDecimal());

        var pay = $"{Reg}/mtv/{id}/odeme";
        await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 400m }), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 400.005m }, Key()), HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 0m }, Key()), HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "POS", tutar = 10m }, Key()), HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 1000.01m }, Key()), HttpStatusCode.BadRequest, "dogrulama", "tutar");

        var k1 = Key();
        var first = await Json(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 400m, beklenenKalan = 1000m }, k1));
        Assert.Equal(600m, first.GetProperty("kalan").GetDecimal());
        var p1 = first.GetProperty("odemeId").GetGuid();
        Assert.Equal((400m, 400m, 2), await LedgerAsync(e.TenantId, "MtvOdeme", p1));
        var gider = await ReadAsync(e.TenantId, db => db.AccountLedgerEntries.AsNoTracking()
            .Where(x => x.SourceId == p1 && x.Direction == LedgerDirection.Debit).Select(x => new { x.AccountType, x.AccountRef }).SingleAsync());
        Assert.Equal((LedgerAccountType.Gider, (Guid?)car), (gider.AccountType, gider.AccountRef));

        // Lost response → exact retry: 409 mukerrer + mevcut (same content); changed amount: ayniIcerik=false. No 2nd write.
        var again = await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 400m, beklenenKalan = 1000m }, k1),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(again.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(p1, again.GetProperty("mevcut").GetProperty("id").GetGuid());
        var other = await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", tutar = 300m }, k1), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(other.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1, await MtvPaymentCountAsync(e.TenantId));

        // Stale screen (still believes 1.000 is due) with a NEW key → 409 cakisma, nothing written.
        await Problem(await Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", beklenenKalan = 1000m }, Key()), HttpStatusCode.Conflict, "cakisma");
        Assert.Equal(1, await MtvPaymentCountAsync(e.TenantId));

        // Same key × 5 concurrently → one payment; two tabs × 5 "pay the rest" with different keys → one payment.
        var k2 = Key();
        var same = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Send(s, HttpMethod.Post, pay, new { hesap = "Banka", tutar = 100m, beklenenKalan = 600m }, k2)));
        Assert.Equal(1, same.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(same.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var tabs = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Send(s, HttpMethod.Post, pay, new { hesap = "Kasa", beklenenKalan = 500m }, Key())));
        Assert.Equal(1, tabs.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(tabs.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.True(
            r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, r.StatusCode.ToString()));

        var d = await Json(await s.C.GetAsync($"{Reg}/mtv/{id}"));
        Assert.Equal(0m, d.GetProperty("mtv").GetProperty("kalan").GetDecimal());
        Assert.True(d.GetProperty("mtv").GetProperty("odendi").GetBoolean());
        Assert.Equal(3, d.GetProperty("odemeler").GetArrayLength());
        // Whole MTV: Σ debit = Σ credit = 1.000 (400 + 100 + 500).
        Assert.Equal((1000m, 1000m, 6), await LedgerAsync(e.TenantId, "MtvOdeme"));
    }

    [Fact]
    public async Task Inspection_fine_increases_debt_and_policy_fx_payment_is_structural()
    {
        var e = await SetupAsync();
        var car = await VehicleAsync(e);
        var s = await LoginAsync(e, Who.Admin);

        // ORACLE: muayene 500 + ceza 50, "pay all" → 550 paid, kalan 0, ledger 550 = 550.
        var ins = await Json(await Send(s, HttpMethod.Post, $"{Reg}/muayeneler",
            new { vehicleId = car, muayeneTarihi = TestZaman.GunSonra(-2), bitis = TestZaman.GunSonra(700), ucret = 500m }),
            HttpStatusCode.Created);
        var iid = ins.GetProperty("muayene").GetProperty("id").GetGuid();
        var paid = await Json(await Send(s, HttpMethod.Post, $"{Reg}/muayeneler/{iid}/odeme", new { hesap = "Kasa", ceza = 50m }, Key()));
        Assert.Equal(550m, paid.GetProperty("tutar").GetDecimal());
        Assert.Equal(0m, paid.GetProperty("kalan").GetDecimal());
        Assert.Equal((550m, 550m, 2), await LedgerAsync(e.TenantId, "MuayeneOdeme"));

        // EUR policy 1.000 @ 30 → base 30.000 balanced; TRY-style rate on TRY policy rejected.
        var pol = await Json(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar", new
        {
            vehicleId = car, tip = "Kasko", baslangic = TestZaman.GunSonra(-1), bitis = TestZaman.GunSonra(360), prim = 1000m, doviz = "EUR",
        }), HttpStatusCode.Created);
        var pid = pol.GetProperty("police").GetProperty("id").GetGuid();
        var tryPol = await Json(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar", new
        {
            vehicleId = car, tip = "Trafik", baslangic = TestZaman.GunSonra(-1), bitis = TestZaman.GunSonra(360), prim = 200m,
        }), HttpStatusCode.Created);
        var tid = tryPol.GetProperty("police").GetProperty("id").GetGuid();
        await Problem(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar/{tid}/odeme", new { hesap = "Kasa", kur = 5m }), HttpStatusCode.BadRequest, "dogrulama", "kur");
        await Problem(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar/{pid}/odeme", new { hesap = "Kasa", kur = 999_999m, zeyilEkPrim = 999_999_999_999m }),
            HttpStatusCode.BadRequest, "dogrulama");

        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Send(s, HttpMethod.Post, $"{Reg}/sigortalar/{pid}/odeme", new { hesap = "Banka", kur = 30m })));
        Assert.Equal(1, burst.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(burst.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal((30000m, 30000m, 2), await LedgerAsync(e.TenantId, "SigortaOdeme", pid));
        var dup = await Problem(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar/{pid}/odeme", new { hesap = "Banka", kur = 30m }),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(dup.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1000m, dup.GetProperty("mevcut").GetProperty("tutar").GetDecimal());
        var diff = await Problem(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar/{pid}/odeme", new { hesap = "Kasa", zeyilEkPrim = 10m }),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(diff.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var detail = await Json(await s.C.GetAsync($"{Reg}/sigortalar/{pid}"));
        Assert.Equal(30000m, detail.GetProperty("odeme").GetProperty("tutarBaz").GetDecimal());
        Assert.Equal(0m, detail.GetProperty("police").GetProperty("kalan").GetDecimal());

        // Zeyil is information only: add + delete, no ledger rows.
        var z = await Json(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar/{pid}/zeyiller",
            new { zeyilNo = "Z-1", tarih = TestZaman.GunSonra(0), brut = -50m, tipi = "Tenzil" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(s, HttpMethod.Delete, $"{Reg}/zeyiller/{z.GetProperty("id").GetGuid()}")).StatusCode);
        Assert.Equal((30000m, 30000m, 2), await LedgerAsync(e.TenantId, "SigortaOdeme"));
    }

    [Fact]
    public async Task Regulation_scope_permissions_and_tenant_isolation()
    {
        var e = await SetupAsync();
        var carA = await VehicleAsync(e, "SubeA");
        var a = await LoginAsync(e, Who.OperatorA);
        var mtv = await Json(await Send(a, HttpMethod.Post, $"{Reg}/mtv",
            new { vehicleId = carA, donem = "2026-2", tutar = 100m, vade = TestZaman.GunSonra(10) }), HttpStatusCode.Created);
        var id = mtv.GetProperty("mtv").GetProperty("id").GetGuid();
        // Operator has no FinanceWrite → payment 403.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(a, HttpMethod.Post, $"{Reg}/mtv/{id}/odeme", new { hesap = "Kasa" }, Key())).StatusCode);

        var b = await LoginAsync(e, Who.OperatorB);
        await Problem(await b.C.GetAsync($"{Reg}/mtv/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync($"{Reg}/mtv"))).GetProperty("toplam").GetInt32());
        await Problem(await Send(b, HttpMethod.Post, $"{Reg}/mtv",
            new { vehicleId = carA, donem = "x", tutar = 1m, vade = TestZaman.GunSonra(1) }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(a, HttpMethod.Post, $"{Reg}/mtv",
            new { vehicleId = Guid.NewGuid(), donem = "x", tutar = 1m, vade = TestZaman.GunSonra(1) }), HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        Assert.Equal(0, (await Json(await b.C.GetAsync(V1 + "/vade"))).GetProperty("kalemler").GetProperty("toplam").GetInt32());
        Assert.True((await Json(await a.C.GetAsync(V1 + "/vade"))).GetProperty("kalemler").GetProperty("toplam").GetInt32() >= 1);

        // Accounting (FinanceWrite) pays; another tenant sees 404.
        var acc = await LoginAsync(e, Who.Accounting);
        await Json(await Send(acc, HttpMethod.Post, $"{Reg}/mtv/{id}/odeme", new { hesap = "Kasa" }, Key()));
        var other = await SetupAsync();
        var o = await LoginAsync(other, Who.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await o.C.GetAsync($"{Reg}/mtv/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(o, HttpMethod.Post, $"{Reg}/mtv/{id}/odeme", new { hesap = "Kasa" }, Key())).StatusCode);
    }
}
