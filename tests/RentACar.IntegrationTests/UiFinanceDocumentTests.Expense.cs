using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Expenses;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceDocumentTests
{
    [Fact]
    public async Task Expense_tl_label_is_normalized_to_try_in_document_and_ledger()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(s, "/giderler", new
        {
            tip = "Genel", netTutar = 100m, kdvOrani = 0.20m, odemeYontemi = "Nakit", doviz = "TL",
        }, NewKey()));

        Assert.Equal("TRY", await DbAsync(e, db => db.Expenses.Where(x => x.Id == id).Select(x => x.Currency).SingleAsync()));
        // Borç Gider 100 + Borç KDV 20 / Alacak Kasa 120 — hepsi TRY (önce "TL" yazılıyordu, #279 N1).
        var set = await LedgerAsync(e, id);
        Assert.Equal(3, set.Count);
        Line(set, LedgerAccountType.Gider, null, LedgerDirection.Debit, 100m);
        Line(set, LedgerAccountType.Kdv, null, LedgerDirection.Debit, 20m);
        Line(set, LedgerAccountType.Kasa, null, LedgerDirection.Credit, 120m);
        await AllLedgerBalancedAsync(e);
    }

    /// <summary>Servis düzeyi (Blazor yolu dahil): "TL" ham etiketi defterde TRY olur; araç satışında da aynı kural.</summary>
    [Fact]
    public async Task Service_level_tl_label_is_normalized_for_expense_and_vehicle_sale()
    {
        var e = await SetupAsync();
        var (expense, sale) = await ReadAsync(e.TenantId, async sp =>
        {
            var x = await sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
            {
                Tip = ExpenseType.Genel, NetTutar = 50m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit, Doviz = "tl",
            });
            var v = await VehicleAsync(sp, "SubeA");
            var y = await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
            {
                VehicleId = v, AliciCariId = e.Customer, SatisNet = 1000m, KdvOrani = 0.20m, Doviz = "TL",
            });
            return (x, y);
        });
        Assert.All(await LedgerAsync(e, expense), x => Assert.Equal("TRY", x.Amount.Currency));
        Assert.All(await LedgerAsync(e, sale), x => Assert.Equal("TRY", x.Amount.Currency));
        Assert.Equal(1200m, await CustomerBalanceAsync(e, e.Customer)); // 1000 + %20, tek dövizde
    }

    [Fact]
    public async Task Expense_idempotency_payment_tracking_and_scope()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var key = NewKey();
        object Body(decimal net) => new { tip = "Genel", netTutar = net, kdvOrani = 0m, odemeYontemi = "AcikHesap", cariId = e.Supplier };
        var id = await IdOf(await PostAsync(s, "/giderler", Body(500m), key));
        var replay = await Problem(await PostAsync(s, "/giderler", Body(500m), key), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(replay.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var other = await Problem(await PostAsync(s, "/giderler", Body(900m), key), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(other.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(-500m, await CustomerBalanceAsync(e, e.Supplier)); // tedarikçiye borçluyuz: Alacak Cari 500

        var payKey = NewKey();
        var pay = await Ok(await PostAsync(s, $"/giderler/{id}/odeme", new { tutar = 200m }, payKey));
        Assert.Equal(300m, pay.GetProperty("kalanSonrasi").GetDecimal());
        await Problem(await PostAsync(s, $"/giderler/{id}/odeme", new { tutar = 200m }, payKey), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, $"/giderler/{id}/odeme", new { tutar = 301m }, NewKey()), HttpStatusCode.BadRequest, "dogrulama");
        var detail = await Ok(await GetAsync(s, $"/giderler/{id}"));
        Assert.Equal(300m, detail.GetProperty("gider").GetProperty("kalan").GetDecimal());
        Assert.Single(detail.GetProperty("odemeler").EnumerateArray());
        Assert.Equal(-500m, await CustomerBalanceAsync(e, e.Supplier)); // ödeme takibi deftere YAZMAZ

        await Problem(await PostAsync(s, "/giderler", new { tip = "Genel", netTutar = 10m, kdvOrani = 20m, odemeYontemi = "Nakit" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "kdvOrani");
        await Problem(await PostAsync(s, "/giderler", new { tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit", doviz = "KRONER" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "doviz");

        var a = await LoginAsync(e, Who.OperatorA);
        await Problem(await PostAsync(a, "/giderler", new { tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit", sube = "SubeB" }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(a, "/giderler", new { tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "sube");
        var mine = await IdOf(await PostAsync(a, "/giderler", new { tip = "Genel", netTutar = 10m, kdvOrani = 0m, odemeYontemi = "Nakit", sube = "SubeA" }, NewKey()));
        var aList = await PageIdsAsync(await GetAsync(a, "/giderler"));
        Assert.Contains(mine, aList);
        Assert.DoesNotContain(id, aList); // şubesiz gider kapsam dışı
        await Problem(await GetAsync(await LoginAsync(e, Who.OperatorB), $"/giderler/{mine}"), HttpStatusCode.Forbidden, "yetki_yok");
    }

    [Fact]
    public async Task Vehicle_sale_posts_balanced_once_and_respects_scope()
    {
        var e = await SetupAsync();
        var vehicle = await ReadAsync(e.TenantId, sp => VehicleAsync(sp, "SubeA"));
        var b = await LoginAsync(e, Who.OperatorB);
        object Body() => new { aracId = vehicle, aliciCariId = e.Customer, satisNet = 10000m, kdvOrani = 0.20m, doviz = "TL" };
        await Problem(await PostAsync(b, "/satislar", Body()), HttpStatusCode.Forbidden, "yetki_yok");

        var s = await LoginAsync(e, Who.Accountant);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(s, "/satislar", Body())));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var id = await IdOf(results.Single(r => r.StatusCode == HttpStatusCode.OK));
        var set = await LedgerAsync(e, id);
        Line(set, LedgerAccountType.Cari, e.Customer, LedgerDirection.Debit, 12000m);
        Line(set, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 10000m);
        Line(set, LedgerAccountType.Kdv, null, LedgerDirection.Credit, 2000m);
        Assert.Equal(1, await DbAsync(e, db => db.VehicleSales.CountAsync()));
        Assert.DoesNotContain(id, await PageIdsAsync(await GetAsync(b, "/satislar")));
        Assert.Contains(id, await PageIdsAsync(await GetAsync(s, "/satislar")));
        await AllLedgerBalancedAsync(e);
    }
}
