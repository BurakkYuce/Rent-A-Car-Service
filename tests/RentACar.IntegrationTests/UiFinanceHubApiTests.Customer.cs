using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceHubApiTests
{
    // ------------------------------------------------------------ cari virman (E07)

    [Fact]
    public async Task Customer_transfer_moves_balance_between_customers()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var key = NewKey();
        var body = new { kaynakCariId = e.CustomerA, hedefCariId = e.CustomerB, tutar = 70m, makbuzNo = "CV-1" };
        var id = await IdOf(await PostAsync(s, "/cari-virman", body, key));

        // Oracle: kaynak alacaklanır (0 − 70), hedef borçlanır (0 + 70).
        Assert.Equal(-70m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(70m, await BalanceAsync(e, e.CustomerB));
        Assert.Equal(id, await IdOf(await PostAsync(s, "/cari-virman", body, key)));
        await Problem(await PostAsync(s, "/cari-virman", body with { hedefCariId = e.CustomerA, kaynakCariId = e.CustomerB }, key),
            HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/cari-virman", body with { hedefCariId = e.CustomerA }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "hedefCariId");

        var list = await Ok(await GetAsync(s, $"/cari-virmanlar?cariId={e.CustomerA}"));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("Hub Alfa", list[0].GetProperty("kaynakCariAd").GetString());
        Assert.Equal(70m, list[0].GetProperty("tutar").GetDecimal());
        await AllLedgerBalancedAsync(e);
    }

    // ------------------------------------------------------------ tek cari toplu kapatma (E05)

    [Fact]
    public async Task Close_items_collects_selected_debts_and_allocates_permanently()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 100m }, NewKey()));
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 900m }, NewKey()));

        var open = await Ok(await GetAsync(s, $"/cariler/{e.CustomerA}/acik-kalemler"));
        Assert.Equal(1000m, open.GetProperty("acikToplam").GetDecimal());
        var items = open.GetProperty("kalemler").EnumerateArray().ToList();
        var small = items.Single(x => x.GetProperty("baz").GetDecimal() == 100m).GetProperty("id").GetGuid();
        var big = items.Single(x => x.GetProperty("baz").GetDecimal() == 900m).GetProperty("id").GetGuid();

        // 100'lük kalem TAMAMEN + 900'lük kalemden 400 → tek tahsilat 500; bakiye 1000 − 500 = 500.
        var key = NewKey();
        var body = new { secim = new object[] { new { kalemId = small }, new { kalemId = big, tutar = 400m } }, hesap = "Kasa" };
        var result = await Ok(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat", body, key));
        Assert.Equal(500m, result.GetProperty("tutar").GetDecimal());
        Assert.Equal(500m, await BalanceAsync(e, e.CustomerA));

        var after = (await Ok(await GetAsync(s, $"/cariler/{e.CustomerA}/acik-kalemler"))).GetProperty("kalemler").EnumerateArray().ToList();
        Assert.True(after.Single(x => x.GetProperty("id").GetGuid() == small).GetProperty("kapali").GetBoolean());
        Assert.Equal(500m, after.Single(x => x.GetProperty("id").GetGuid() == big).GetProperty("kalan").GetDecimal());

        // Kaybolan yanıttan sonraki birebir tekrar → 409 + mevcut (aynı içerik); yeni tahsilat yok.
        var dup = await Problem(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat", body, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(dup.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(500m, dup.GetProperty("mevcut").GetProperty("tutar").GetDecimal());
        // Kapanmış kalem yeniden seçilemez (tahsis kalıcı).
        await Problem(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat",
            new { secim = new object[] { new { kalemId = small } }, hesap = "Kasa" }, NewKey()), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(1, await CashCountAsync(e));
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Close_items_concurrent_same_key_writes_once()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 300m }, NewKey()));
        var item = (await Ok(await GetAsync(s, $"/cariler/{e.CustomerA}/acik-kalemler"))).GetProperty("kalemler")[0].GetProperty("id").GetGuid();
        var key = NewKey();
        var body = new { secim = new object[] { new { kalemId = item, tutar = 50m } }, hesap = "Banka" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat", body, key)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await CashCountAsync(e));
        Assert.Equal(250m, await BalanceAsync(e, e.CustomerA)); // 300 − 50
    }

    [Fact]
    public async Task Close_items_rejects_foreign_selection_and_unknown_customer()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerB, yon = "Borclandir", tutar = 30m }, NewKey()));
        var itemOfB = (await Ok(await GetAsync(s, $"/cariler/{e.CustomerB}/acik-kalemler"))).GetProperty("kalemler")[0].GetProperty("id").GetGuid();
        // B'nin kalemi A adına kapatılamaz.
        await Problem(await PostAsync(s, $"/cariler/{e.CustomerA}/toplu-kapat",
            new { secim = new object[] { new { kalemId = itemOfB } }, hesap = "Kasa" }, NewKey()), HttpStatusCode.BadRequest, "dogrulama");
        await Problem(await PostAsync(s, $"/cariler/{Guid.NewGuid()}/toplu-kapat",
            new { secim = new object[] { new { kalemId = itemOfB } }, hesap = "Kasa" }, NewKey()), HttpStatusCode.NotFound, null);
        await Problem(await PostAsync(s, $"/cariler/{e.CustomerB}/toplu-kapat",
            new { secim = new object[] { new { kalemId = itemOfB, tutar = 10.005m } }, hesap = "Kasa" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "secim[0].tutar");
        Assert.Equal(0, await CashCountAsync(e));
    }

    // ------------------------------------------------------------ çok cari toplu tahsilat (E03)

    [Fact]
    public async Task Bulk_collection_is_atomic_and_idempotent()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var bad = new { hesap = "Kasa", satirlar = new object[] { new { cariId = e.CustomerA, tutar = 100m }, new { cariId = Guid.NewGuid(), tutar = 50m } } };
        await Problem(await PostAsync(s, "/toplu-tahsilat", bad, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "satirlar[1].cariId");
        Assert.Equal(0, await CashCountAsync(e));

        var key = NewKey();
        var body = new { hesap = "Kasa", kanal = "Mobil", satirlar = new object[] { new { cariId = e.CustomerA, tutar = 100m }, new { cariId = e.CustomerB, tutar = 50m } } };
        var r = await Ok(await PostAsync(s, "/toplu-tahsilat", body, key));
        Assert.Equal(2, r.GetProperty("adet").GetInt32());
        Assert.Equal(150m, r.GetProperty("toplam").GetDecimal());
        Assert.Equal(-100m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(-50m, await BalanceAsync(e, e.CustomerB));

        var dup = await Problem(await PostAsync(s, "/toplu-tahsilat", body, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(dup.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var changed = await Problem(await PostAsync(s, "/toplu-tahsilat",
            body with { satirlar = new object[] { new { cariId = e.CustomerA, tutar = 999m } } }, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(changed.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(2, await CashCountAsync(e));
        await AllLedgerBalancedAsync(e);
    }
}
