using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceDocumentTests
{
    private async Task<(Guid Id, Guid Line1, Guid Line2)> PenaltyAsync(Env e, Session s, Guid? vehicle = null)
    {
        var id = await IdOf(await PostAsync(s, "/cezalar", new
        {
            cezaTuru = "Hız", aracId = vehicle ?? e.RentalVehicle, cariId = e.Customer, tebligTarihi = TestZaman.DaysLater(-2),
            kalemler = new[] { new { tutar = 150m, sebep = "Hız" }, new { tutar = 50.50m, sebep = "Park" } },
        }));
        var detail = await Ok(await GetAsync(s, $"/cezalar/{id}"));
        var lines = detail.GetProperty("kalemler").EnumerateArray().OrderBy(x => x.GetProperty("sira").GetInt32()).ToList();
        return (id, lines[0].GetProperty("id").GetGuid(), lines[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Penalty_reflect_and_partial_payment_post_balanced_sets_without_double_count()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, line1, _) = await PenaltyAsync(e, s);
        var head = (await Ok(await GetAsync(s, $"/cezalar/{id}"))).GetProperty("ceza");
        Assert.Equal(200.50m, head.GetProperty("tutar").GetDecimal()); // 150 + 50,50

        await Ok(await PostAsync(s, $"/cezalar/{id}/yansit", null));
        var reflect = await LedgerAsync(e, id);
        Line(reflect, LedgerAccountType.Cari, e.Customer, LedgerDirection.Debit, 200.50m);
        Line(reflect, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 200.50m);
        await Problem(await PostAsync(s, $"/cezalar/{id}/yansit", null), HttpStatusCode.BadRequest, "dogrulama");

        var key = NewKey();
        var pay = await Ok(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 100m, hesap = "Kasa" }, key));
        Assert.Equal(50m, pay.GetProperty("satirKalan").GetDecimal());   // 150 − 100
        Assert.Equal(100.50m, pay.GetProperty("cezaKalan").GetDecimal()); // 200,50 − 100
        var paySet = await LedgerAsync(e, pay.GetProperty("odemeId").GetGuid());
        Line(paySet, LedgerAccountType.Gider, e.RentalVehicle, LedgerDirection.Debit, 100m);
        Line(paySet, LedgerAccountType.Kasa, null, LedgerDirection.Credit, 100m);

        // Kaybolan yanıttan sonraki doğru tekrar: 409 + mevcut(aynı içerik); ikinci ödeme YOK.
        var replay = await Problem(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 100m, hesap = "Kasa" }, key),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(replay.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var changed = await Problem(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 10m, hesap = "Banka" }, key),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(changed.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        await Problem(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 60m, hesap = "Kasa" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama"); // kalan 50
        await Problem(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 1m, hesap = "Cari" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "hesap");
        Assert.Equal(1, await DbAsync(e, db => db.PenaltyOdemeleri.CountAsync()));
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Penalty_concurrent_payments_never_exceed_line_remaining()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, _, line2) = await PenaltyAsync(e, s);
        // Kalem 2 kalanı 50,50: dört ayrı anahtarla 30'ar → yalnız biri sığar (30 + 30 > 50,50).
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line2, tutar = 30m, hesap = "Kasa" }, NewKey())));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var paid = await DbAsync(e, db => db.PenaltyOdemeleri.Where(o => o.SatirId == line2).SumAsync(o => o.Tutar));
        Assert.Equal(30m, paid);
        var head = (await Ok(await GetAsync(s, $"/cezalar/{id}"))).GetProperty("ceza");
        Assert.Equal(170.50m, head.GetProperty("kalan").GetDecimal());
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Penalty_scope_is_checked_before_state_and_permissions_hold()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var (id, line1, _) = await PenaltyAsync(e, admin);             // SubeA aracı
        await Ok(await PostAsync(admin, $"/cezalar/{id}/yansit", null)); // durum artık Yansitildi

        var b = await LoginAsync(e, Who.OperatorB); // SubeB + FinanceWrite
        await Problem(await GetAsync(b, $"/cezalar/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        // Kapsam durumdan ÖNCE: zaten yansıtılmış cezada bile "yalnız Yeni" 400'ü değil 403 (durum sızmaz).
        await Problem(await PostAsync(b, $"/cezalar/{id}/yansit", null), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, $"/cezalar/{id}/odeme", new { satirId = line1, hesap = "Kasa" }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        Assert.DoesNotContain(id, await PageIdsAsync(await GetAsync(b, "/cezalar")));
        await Problem(await PostAsync(b, "/cezalar", new { cezaTuru = "Hız", aracId = e.RentalVehicle, kalemler = new[] { new { tutar = 10m } } }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/cezalar", new { cezaTuru = "Hız", kalemler = new[] { new { tutar = 10m } } }),
            HttpStatusCode.BadRequest, "dogrulama", "aracId");

        var a = await LoginAsync(e, Who.OperatorA); // SubeA: görür
        Assert.Contains(id, await PageIdsAsync(await GetAsync(a, "/cezalar")));
        var plain = await LoginAsync(e, Who.OperatorPlain); // FinanceWrite yok, OperationsDelete yok
        await Problem(await PostAsync(plain, $"/cezalar/{id}/yansit", null), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(plain, $"/cezalar/{id}/iptal", null), HttpStatusCode.Forbidden, "yetki_yok");

        var other = await SetupAsync();
        var otherAdmin = await LoginAsync(other, Who.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(otherAdmin, $"/cezalar/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(otherAdmin, $"/cezalar/{id}/yansit", null)).StatusCode);
    }

    [Fact]
    public async Task Penalty_cancel_blocks_reflected_and_create_validates_references()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, _, _) = await PenaltyAsync(e, s);
        var (reflected, _, _) = await PenaltyAsync(e, s);
        await Ok(await PostAsync(s, $"/cezalar/{reflected}/yansit", null));

        Assert.Equal("Iptal", (await Ok(await PostAsync(s, $"/cezalar/{id}/iptal", null))).GetProperty("durum").GetString());
        await Problem(await PostAsync(s, $"/cezalar/{reflected}/iptal", null), HttpStatusCode.BadRequest, "dogrulama");
        await Problem(await PostAsync(s, "/cezalar", new { cezaTuru = "Hız", cariId = Guid.NewGuid(), kalemler = new[] { new { tutar = 10m } } }),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/cezalar", new { cezaTuru = "Hız", kalemler = new[] { new { tutar = 1e16m } } }),
            HttpStatusCode.BadRequest, "dogrulama", "kalemler[0].tutar");
        await Problem(await PostAsync(s, "/cezalar", new { cezaTuru = "Hız", kalemler = Array.Empty<object>() }),
            HttpStatusCode.BadRequest, "dogrulama", "kalemler");
    }
}
