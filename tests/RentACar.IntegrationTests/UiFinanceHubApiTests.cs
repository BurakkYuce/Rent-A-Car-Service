using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceHubApiTests
{
    // ------------------------------------------------------------ kasa ↔ banka virman (E06)

    [Fact]
    public async Task Cash_transfer_posts_balanced_pair_and_is_idempotent()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var key = NewKey();
        var body = new { kaynak = "Kasa", hedef = "Banka", tutar = 250m, aciklama = "Bankaya yatan" };

        var id = await IdOf(await PostAsync(s, "/kasa/virman", body, key));
        var set = await LedgerAsync(e, id);
        Assert.Equal(2, set.Count);
        Assert.Single(set, x => x.AccountType == LedgerAccountType.Banka && x.Direction == LedgerDirection.Debit && x.Amount.Amount == 250m);
        Assert.Single(set, x => x.AccountType == LedgerAccountType.Kasa && x.Direction == LedgerDirection.Credit && x.Amount.Amount == 250m);

        // Oracle: boş kiracıda kasa 0 − 250, banka 0 + 250.
        var summary = await Ok(await GetAsync(s, "/kasa/ozet"));
        Assert.Equal(-250m, summary.GetProperty("kasaBakiye").GetDecimal());
        Assert.Equal(250m, summary.GetProperty("bankaBakiye").GetDecimal());

        // Aynı anahtar aynı içerik → aynı id, yeni satır yok; farklı tutar → 409 mukerrer.
        Assert.Equal(id, await IdOf(await PostAsync(s, "/kasa/virman", body, key)));
        await Problem(await PostAsync(s, "/kasa/virman", body with { tutar = 300m }, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(2, (await LedgerAsync(e, id)).Count);

        var list = await Ok(await GetAsync(s, "/kasa/virmanlar"));
        Assert.Equal(250m, list[0].GetProperty("tutar").GetDecimal());
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Cash_transfer_rejects_bad_input_before_writing()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var ok = new { kaynak = "Kasa", hedef = "Banka", tutar = 10m };
        await Problem(await PostAsync(s, "/kasa/virman", ok, key: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await PostAsync(s, "/kasa/virman", ok with { tutar = 1.23456m }, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await PostAsync(s, "/kasa/virman", new { kaynak = "Kasa", hedef = "Banka", tutar = 10m, kur = 5m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        await Problem(await PostAsync(s, "/kasa/virman", new { kaynak = "Kasa", hedef = "Banka", tutar = 10m, doviz = "USD", kur = 1.1234567m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        await Problem(await PostAsync(s, "/kasa/virman", new { kaynak = "Cari", hedef = "Banka", tutar = 10m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "kaynak");
        await Problem(await PostAsync(s, "/kasa/virman", ok with { tutar = 1_000_000_000_000_000m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "tutar");
        Assert.Equal(0, await DbAsync(e, db => db.AccountLedgerEntries.CountAsync()));
    }

    [Fact]
    public async Task Finance_endpoints_require_finance_permission()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.OperatorPlain);
        await Problem(await GetAsync(s, "/kasa/ozet"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(s, "/kasa/virman", new { kaynak = "Kasa", hedef = "Banka", tutar = 10m }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 5m }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        // Kurlar Blazor'da her role açık (parite): operatör okur.
        await Ok(await GetAsync(s, "/kurlar"));
        // Karar (5), 2026-09-25: ekstre cari bakiyesiyle aynı kapı (FinanceWrite ∨ ViewReports) — kendi şubesindeki
        // carinin ekstresi dahil operatöre 403; Muhasebe okur.
        await Problem(await GetAsync(s, $"/cariler/{e.CustomerA}/ekstre"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await GetAsync(s, $"/cariler/{e.CustomerA}/ekstre?mod=ozet"), HttpStatusCode.Forbidden, "yetki_yok");
        var muhasebe = await LoginAsync(e, Who.Accountant);
        await Ok(await GetAsync(muhasebe, $"/cariler/{e.CustomerA}/ekstre"));
        await Problem(await PostAsync(s, "/kurlar/sabit", new { kod = "USD", kur = 30m }), HttpStatusCode.Forbidden, "yetki_yok");
    }

    // ------------------------------------------------------------ bakiye düzeltme (E13)

    [Fact]
    public async Task Balance_adjustment_moves_customer_balance_without_touching_cash()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var key = NewKey();
        var debit = new { cariId = e.CustomerA, yon = "Borclandir", tutar = 100m, makbuzNo = "MK-1" };
        var id = await IdOf(await PostAsync(s, "/bakiye-duzeltme", debit, key));
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Alacaklandir", tutar = 40m }, NewKey()));

        // Oracle: 0 + 100 − 40 = 60 (pozitif = müşteri borçlu). Kasa/Banka satırı yok.
        Assert.Equal(60m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(0, await DbAsync(e, db => db.AccountLedgerEntries.CountAsync(x =>
            x.AccountType == LedgerAccountType.Kasa || x.AccountType == LedgerAccountType.Banka)));

        Assert.Equal(id, await IdOf(await PostAsync(s, "/bakiye-duzeltme", debit, key)));
        await Problem(await PostAsync(s, "/bakiye-duzeltme", debit with { tutar = 101m }, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(60m, await BalanceAsync(e, e.CustomerA));
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Balance_adjustment_rejects_unknown_foreign_customer_and_closed_period()
    {
        var e = await SetupAsync();
        var foreign = await ForeignCustomerAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await Problem(await PostAsync(s, "/bakiye-duzeltme", new { cariId = Guid.NewGuid(), yon = "Borclandir", tutar = 5m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/bakiye-duzeltme", new { cariId = foreign, yon = "Borclandir", tutar = 5m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Yukari", tutar = 5m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "yon");

        // Dönem dün kapatılır → iki gün önceye düzeltme 400, bugüne 200.
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(s, "/donem-kapanis/kilitle", new { kapanisTarihi = yesterday })).StatusCode);
        await Problem(await PostAsync(s, "/bakiye-duzeltme",
            new { cariId = e.CustomerA, yon = "Borclandir", tutar = 5m, tarih = TestZaman.GunSonra(-2) }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama");
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 5m }, NewKey()));
        Assert.Equal(5m, await BalanceAsync(e, e.CustomerA));
    }
}
