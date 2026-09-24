using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceDocumentTests
{
    [Fact]
    public async Task Incoming_invoice_to_expense_posts_once_and_sync_is_honest_stub()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);

        // Dürüst stub: entegrasyon yokken "0 eklendi" başarısı DÖNMEZ.
        await Problem(await PostAsync(s, "/gelen-efatura/sync", new { bas = DateOnly.FromDateTime(TestZaman.GunSonra(-7).Date),
            bit = DateOnly.FromDateTime(TestZaman.GunSonra(0).Date) }), HttpStatusCode.BadRequest, "dogrulama");

        var ettn = Guid.NewGuid().ToString();
        var id = await IdOf(await PostAsync(s, "/gelen-efatura", new
        {
            ettn, gonderenVkn = "1234567890", gonderenUnvan = "Tedarikçi A.Ş.", netTutar = 1000m, kdvTutar = 200m,
            genelToplam = 1200m, doviz = "TL", tarih = TestZaman.GunSonra(-1),
        }));
        await Problem(await PostAsync(s, "/gelen-efatura", new
        {
            ettn, gonderenVkn = "1234567890", gonderenUnvan = "X", netTutar = 1m, kdvTutar = 0m, genelToplam = 1m,
        }), HttpStatusCode.BadRequest, null); // aynı ETTN
        await Problem(await PostAsync(s, $"/gelen-efatura/{id}/giderlestir", new { cariId = e.Supplier }), HttpStatusCode.BadRequest, "dogrulama");

        await Ok(await PostAsync(s, $"/gelen-efatura/{id}/onayla", null));
        var surum = (await Ok(await GetAsync(s, $"/gelen-efatura/{id}"))).GetProperty("surum").GetString();
        await Problem(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{id}/bag", new { surum, kdv20Matrah = 1000m, kdv20 = 200m, cariId = Guid.NewGuid() }, null),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Ok(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{id}/bag", new { surum, kdv20Matrah = 1000m, kdv20 = 200m, cariId = e.Supplier }, null));

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostAsync(s, $"/gelen-efatura/{id}/giderlestir", new { })));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));

        // Açık hesap: Borç Gider 1000 + Borç KDV 200 / Alacak Cari(tedarikçi) 1200 — TRY ("TL" belge etiketi indirgendi).
        var expenses = await DbAsync(e, db => db.Expenses.AsNoTracking().ToListAsync());
        var expense = Assert.Single(expenses);
        var set = await LedgerAsync(e, expense.Id);
        Line(set, LedgerAccountType.Gider, null, LedgerDirection.Debit, 1000m);
        Line(set, LedgerAccountType.Kdv, null, LedgerDirection.Debit, 200m);
        Line(set, LedgerAccountType.Cari, e.Supplier, LedgerDirection.Credit, 1200m);
        Assert.Equal(-1200m, await CustomerBalanceAsync(e, e.Supplier));
        var detail = (await Ok(await GetAsync(s, $"/gelen-efatura/{id}"))).GetProperty("fatura");
        Assert.True(detail.GetProperty("giderlestirildi").GetBoolean());
        Assert.Equal("Islendi", detail.GetProperty("durum").GetString());
        await AllLedgerBalancedAsync(e);

        var a = await LoginAsync(e, Who.OperatorA); // şubeli kullanıcı: kiracı geneli belge
        await Problem(await GetAsync(a, "/gelen-efatura"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await GetAsync(await LoginAsync(e, Who.OperatorPlain), "/gelen-efatura"), HttpStatusCode.Forbidden, "yetki_yok");
    }
}
