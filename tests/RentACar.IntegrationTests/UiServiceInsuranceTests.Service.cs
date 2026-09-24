using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiServiceInsuranceTests
{
    private const string Svc = V1 + "/servisler";

    private Task<int> LineCountAsync(Guid tenant, Guid record)
        => ReadAsync(tenant, db => db.Set<ServiceLine>().AsNoTracking().CountAsync(l => l.ServiceRecordId == record));

    [Fact]
    public async Task Service_lines_idempotent_flow_and_reflection_oracle_balanced()
    {
        var e = await SetupAsync();
        var car = await VehicleAsync(e);
        var s = await LoginAsync(e, Who.Admin);

        var ck = Key();
        var create = new { vehicleId = car, tip = "Hasar", girisKm = 1000, hasarSorumlu = "Musteri", kusurOrani = 0.5m,
            kalem = new { aciklama = "Kaporta", tutar = 1000m } };
        var rec = await Json(await Send(s, HttpMethod.Post, Svc, create, ck), HttpStatusCode.Created);
        var id = rec.GetProperty("kayit").GetProperty("id").GetGuid();
        var dupCreate = await Problem(await Send(s, HttpMethod.Post, Svc, create, ck), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(dupCreate.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());

        // Line: 2 × 250 = 500 (key mandatory; same key N times → one line; ToplamIscilik grows once).
        var line = $"{Svc}/{id}/kalemler";
        await Problem(await Send(s, HttpMethod.Post, line, new { aciklama = "Boya", birimFiyat = 250m, miktar = 2m }),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await Send(s, HttpMethod.Post, line, new { aciklama = "Boya", birimFiyat = 250.001m, miktar = 2m }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "birimFiyat");
        var lk = Key();
        var burst = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Send(s, HttpMethod.Post, line, new { aciklama = "Boya", birimFiyat = 250m, miktar = 2m, kdvOran = 0.20m }, lk)));
        Assert.Equal(1, burst.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(burst.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var dupLine = await Problem(await Send(s, HttpMethod.Post, line, new { aciklama = "Boya", birimFiyat = 250m, miktar = 2m, kdvOran = 0.20m }, lk),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(dupLine.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(2, await LineCountAsync(e.TenantId, id));
        var d = await Json(await s.C.GetAsync($"{Svc}/{id}"));
        Assert.Equal(1500m, d.GetProperty("kayit").GetProperty("toplamIscilik").GetDecimal());
        Assert.Equal(100m, d.GetProperty("kdvToplam").GetDecimal()); // 500 × 0,20; the 1.000 line has no VAT rate

        // Info PUT: mandatory + fresh version.
        var info = new { atolyeAdi = "Usta", faturaNo = "F-1", faturaTutar = 1500m, surum = (string?)null };
        await Problem(await Send(s, HttpMethod.Put, $"{Svc}/{id}/bilgi", info), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var v = d.GetProperty("surum").GetString();
        await Json(await Send(s, HttpMethod.Put, $"{Svc}/{id}/bilgi", info with { surum = v }));
        await Problem(await Send(s, HttpMethod.Put, $"{Svc}/{id}/bilgi", info with { surum = v }), HttpStatusCode.Conflict, "cakisma");

        // Flow: Açık → Serviste → Tamamlandı. Reflection before completion is refused; completed record takes no line.
        await Problem(await Send(s, HttpMethod.Post, $"{Svc}/{id}/yansit", new { cariId = e.CustomerId }), HttpStatusCode.BadRequest, "dogrulama");
        await Json(await Send(s, HttpMethod.Post, $"{Svc}/{id}/baslat"));
        await Problem(await Send(s, HttpMethod.Post, $"{Svc}/{id}/baslat"), HttpStatusCode.BadRequest, "dogrulama");
        await Json(await Send(s, HttpMethod.Post, $"{Svc}/{id}/tamamla", new { cikisKm = 1200 }));
        await Problem(await Send(s, HttpMethod.Post, line, new { aciklama = "Geç", tutar = 1m }, Key()), HttpStatusCode.BadRequest, "dogrulama");

        // ORACLE: 1.500 × 0,50 = 750 → Borç Cari 750 / Alacak Gelir 750; retry → 409 mevcut; other customer → false.
        await Problem(await Send(s, HttpMethod.Post, $"{Svc}/{id}/yansit", new { cariId = Guid.NewGuid() }), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        var refl = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Send(s, HttpMethod.Post, $"{Svc}/{id}/yansit", new { cariId = e.CustomerId })));
        Assert.Equal(1, refl.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(refl.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal((750m, 750m, 2), await LedgerAsync(e.TenantId, "ServisYansitma", id));
        var cari = await ReadAsync(e.TenantId, db => db.AccountLedgerEntries.AsNoTracking()
            .Where(x => x.SourceId == id && x.Direction == LedgerDirection.Debit).Select(x => new { x.AccountType, x.AccountRef }).SingleAsync());
        Assert.Equal((LedgerAccountType.Cari, (Guid?)e.CustomerId), (cari.AccountType, cari.AccountRef));
        var again = await Problem(await Send(s, HttpMethod.Post, $"{Svc}/{id}/yansit", new { cariId = e.CustomerId }), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(again.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(750m, again.GetProperty("mevcut").GetProperty("tutar").GetDecimal());
        var detail = await Json(await s.C.GetAsync($"{Svc}/{id}"));
        Assert.Equal("Ece Tan", detail.GetProperty("yansitma").GetProperty("cariAd").GetString());
    }

    [Fact]
    public async Task Service_scope_and_role_gates()
    {
        var e = await SetupAsync();
        var carA = await VehicleAsync(e, "SubeA");
        var a = await LoginAsync(e, Who.OperatorA);
        var id = (await Json(await Send(a, HttpMethod.Post, Svc, new { vehicleId = carA, girisKm = 10 }), HttpStatusCode.Created))
            .GetProperty("kayit").GetProperty("id").GetGuid();
        var b = await LoginAsync(e, Who.OperatorB);
        await Problem(await b.C.GetAsync($"{Svc}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        // Scope BEFORE state: another branch's record in a wrong state still answers 403, not a state message.
        await Problem(await Send(b, HttpMethod.Post, $"{Svc}/{id}/tamamla", new { cikisKm = 5 }), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(b, HttpMethod.Post, $"{Svc}/{id}/kalemler", new { aciklama = "x", tutar = 1m }, Key()), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Svc))).GetProperty("toplam").GetInt32());
        // Operator: no OperationsDelete (iptal) and no FinanceWrite (yansıt); Accounting cannot create.
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(a, HttpMethod.Post, $"{Svc}/{id}/iptal")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(a, HttpMethod.Post, $"{Svc}/{id}/yansit", new { cariId = e.CustomerId })).StatusCode);
        var acc = await LoginAsync(e, Who.Accounting);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(acc, HttpMethod.Post, Svc, new { vehicleId = carA })).StatusCode);
        Assert.Equal(1, (await Json(await acc.C.GetAsync(Svc))).GetProperty("toplam").GetInt32());

        var other = await SetupAsync();
        var o = await LoginAsync(other, Who.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await o.C.GetAsync($"{Svc}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(o, HttpMethod.Post, $"{Svc}/{id}/baslat")).StatusCode);
    }
}
