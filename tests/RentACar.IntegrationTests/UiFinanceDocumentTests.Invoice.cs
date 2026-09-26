using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceDocumentTests
{
    private static object Manual(Guid account, decimal net, decimal? rate = 0.20m) => new { cariId = account, netTutar = net, kdvOrani = rate, aciklama = "F8.1b manuel" };

    [Fact]
    public async Task Manual_invoice_rounds_vat_per_line_posts_balanced_ledger_and_gib_number()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);

        var body = await Ok(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 333.33m), NewKey()));
        var id = body.GetProperty("id").GetGuid();
        Assert.Matches("^[A-Z]{3}\\d{13}$", body.GetProperty("no").GetString()!); // GİB 16 hane: seri(3)+yıl(4)+sıra(9)

        // 333,33 × 0,20 = 66,666 → 66,67 (kuruş, yukarı); brüt 400,00. Borç Cari / Alacak Gelir + KDV.
        var set = await LedgerAsync(e, id);
        Assert.Equal(3, set.Count);
        Line(set, LedgerAccountType.Cari, e.Customer, LedgerDirection.Debit, 400.00m);
        Line(set, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 333.33m);
        Line(set, LedgerAccountType.Kdv, null, LedgerDirection.Credit, 66.67m);
        Assert.Equal(400.00m, await CustomerBalanceAsync(e, e.Customer));

        var detail = await Ok(await GetAsync(s, $"/faturalar/{id}"));
        Assert.Equal(66.67m, detail.GetProperty("kdvTutar").GetDecimal());
        Assert.Equal($"/faturalar/{id}/pdf", detail.GetProperty("pdfAdresi").GetString());
        Assert.False(detail.GetProperty("eFaturaGonderildi").GetBoolean()); // dürüst stub: gönderilmedi
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Manual_invoice_replay_returns_409_with_existing_and_writes_once()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var key = NewKey();
        var id = await IdOf(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 500m), key));

        var same = await Problem(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 500m), key), HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(id, same.GetProperty("mevcut").GetProperty("id").GetGuid());
        Assert.True(same.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(600m, same.GetProperty("mevcut").GetProperty("tutar").GetDecimal()); // 500 + %20

        var other = await Problem(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 700m), key), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(other.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());

        await Problem(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 500m), null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 500m, 20m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kdvOrani");
        await Problem(await PostAsync(s, "/faturalar/manuel", Manual(Guid.NewGuid(), 500m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        Assert.Equal(1, await DbAsync(e, db => db.Invoices.CountAsync()));
    }

    [Fact]
    public async Task Manual_invoice_concurrent_same_key_writes_single_invoice()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var key = NewKey();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 250m), key)));
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"{(int)r.StatusCode}"));
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await DbAsync(e, db => db.Invoices.CountAsync()));
        Assert.Equal(300m, await CustomerBalanceAsync(e, e.Customer)); // tek fatura: 250 + 50
    }

    [Fact]
    public async Task Refund_reverses_ledger_once_even_concurrently_and_needs_finance_reverse()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 333.33m), NewKey()));

        var denied = await LoginAsync(e, Who.AccountantNoReverse);
        await Problem(await PostAsync(denied, $"/faturalar/{id}/iade", null), HttpStatusCode.Forbidden, "yetki_yok");

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(s, $"/faturalar/{id}/iade", null)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.Conflict));
        var refundId = await IdOf(results.Single(r => r.StatusCode == HttpStatusCode.OK));

        // Ters küme: Alacak Cari 400 / Borç Gelir 333,33 / Borç KDV 66,67 — cari bakiye 0.
        var set = await LedgerAsync(e, refundId);
        Line(set, LedgerAccountType.Cari, e.Customer, LedgerDirection.Credit, 400.00m);
        Line(set, LedgerAccountType.Gelir, null, LedgerDirection.Debit, 333.33m);
        Line(set, LedgerAccountType.Kdv, null, LedgerDirection.Debit, 66.67m);
        Assert.Equal(0m, await CustomerBalanceAsync(e, e.Customer));
        Assert.Equal(refundId, (await Ok(await GetAsync(s, $"/faturalar/{id}"))).GetProperty("iadeFaturaId").GetGuid());
        await Problem(await PostAsync(s, $"/faturalar/{id}/iade", null), HttpStatusCode.BadRequest, "dogrulama");
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Invoice_scope_hides_tenant_wide_and_other_branch_invoices()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var manual = await IdOf(await PostAsync(admin, "/faturalar/manuel", Manual(e.Customer, 100m), NewKey()));
        var rentalInvoice = await IdOf(await PostAsync(admin, "/finans/fatura", new { kiraId = e.Rental }));

        var a = await LoginAsync(e, Who.OperatorA); // SubeA + FinanceWrite istisnası
        var visible = await PageIdsAsync(await GetAsync(a, "/faturalar"));
        Assert.Contains(rentalInvoice, visible);
        Assert.DoesNotContain(manual, visible);
        await Ok(await GetAsync(a, $"/faturalar/{rentalInvoice}"));
        await Problem(await GetAsync(a, $"/faturalar/{manual}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(a, "/faturalar/manuel", Manual(e.Customer, 100m), NewKey()), HttpStatusCode.Forbidden, "yetki_yok");

        var b = await LoginAsync(e, Who.OperatorB); // SubeB
        await Problem(await GetAsync(b, $"/faturalar/{rentalInvoice}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, $"/faturalar/{rentalInvoice}/iade", null), HttpStatusCode.Forbidden, null);
        await Problem(await PostAsync(b, "/faturalar/toplu", new { kiraIds = new[] { e.Rental } }), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Empty(await PageIdsAsync(await GetAsync(b, "/faturalar")));

        var other = await SetupAsync(); // başka kiracının faturası → 404
        var otherAdmin = await LoginAsync(other, Who.Admin);
        var foreign = await IdOf(await PostAsync(otherAdmin, "/faturalar/manuel", Manual(other.Customer, 100m), NewKey()));
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(admin, $"/faturalar/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(admin, $"/faturalar/{foreign}/iade", null)).StatusCode);
    }
}
