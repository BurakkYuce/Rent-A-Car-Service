using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #300 eksik uç 3 — <c>GET /faturalar/ozet</c>: liste süzgeçleriyle aynı küme, döviz kırılımlı toplam. Faturalar doğrudan
/// yazılır (racar_app, kiracı bağlamı); beklenen toplamlar aşağıda elle toplandı. Ayrıca #300 L2 (eski): ceza ödemesi
/// tekrarında <c>mevcut.belgeNo</c> = "ceza no/sıra".
/// </summary>
public sealed partial class UiFinanceDocumentTests
{
    private Task InsertInvoicesAsync(Env e, params Invoice[] invoices)
        => ReadAsync(e.TenantId, async sp =>
        {
            await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            foreach (var i in invoices) i.TenantId = e.TenantId;
            db.Invoices.AddRange(invoices);
            return await db.SaveChangesAsync();
        });

    private static Invoice TestInvoice(Guid cari, decimal net, decimal kdv, string currency = "TRY", decimal rate = 1m)
        => new()
        {
            No = "TST" + Guid.NewGuid().ToString("N")[..10], CariId = cari, Tarih = TestZaman.GunSonra(-1), NetTutar = net,
            KdvTutar = kdv, GenelToplam = net + kdv, Currency = currency, Kur = rate,
        };

    private static JsonElement CurrencyTotal(JsonElement summary, string currency)
        => summary.GetProperty("dovizler").EnumerateArray().Single(x => x.GetProperty("doviz").GetString() == currency);

    [Fact]
    public async Task Invoice_summary_totals_per_currency_with_list_filters_and_scope()
    {
        var e = await SetupAsync();
        var rentalB = await BranchBRentalAsync(e);
        var sale = TestInvoice(e.Customer, 100m, 20m);                  // SubeA kirası: 120
        sale.RentalId = e.Rental;
        var refund = TestInvoice(e.Customer, 50m, 10m);                 // iade, kaynak SubeA: 60
        refund.IadeMi = true;
        refund.KaynakFaturaId = sale.Id;
        var usd = TestInvoice(e.Supplier, 1000m, 200m, "USD", 30m);     // kirasız (kiracı geneli): 1.200 USD
        var branchB = TestInvoice(e.Customer, 500m, 100m);              // SubeB kirası: 600
        branchB.RentalId = rentalB;
        await InsertInvoicesAsync(e, sale, refund, usd, branchB);

        var admin = await LoginAsync(e, Who.Admin);
        var all = await Ok(await GetAsync(admin, "/faturalar/ozet"));
        Assert.Equal(4, all.GetProperty("adet").GetInt32());
        Assert.Equal(new[] { "TRY", "USD" }, all.GetProperty("dovizler").EnumerateArray().Select(x => x.GetProperty("doviz").GetString()));
        var tl = CurrencyTotal(all, "TRY");
        Assert.Equal(3, tl.GetProperty("adet").GetInt32());
        Assert.Equal(650m, tl.GetProperty("netTutar").GetDecimal());      // 100 + 50 + 500
        Assert.Equal(130m, tl.GetProperty("kdvTutar").GetDecimal());      // 20 + 10 + 100
        Assert.Equal(780m, tl.GetProperty("genelToplam").GetDecimal());   // 120 + 60 + 600
        Assert.Equal(1, tl.GetProperty("iadeAdet").GetInt32());
        Assert.Equal(60m, tl.GetProperty("iadeToplam").GetDecimal());
        Assert.Equal(1200m, CurrencyTotal(all, "USD").GetProperty("genelToplam").GetDecimal()); // kurla çevrilmez
        // Liste ile aynı küme.
        Assert.Equal(4, (await PageIdsAsync(await GetAsync(admin, "/faturalar?boyut=100"))).Count);

        // Aynı süzgeçler: doviz=USD yalnız USD; cari süzgeci tedarikçinin tek faturası.
        var onlyUsd = await Ok(await GetAsync(admin, "/faturalar/ozet?doviz=USD"));
        Assert.Equal("USD", Assert.Single(onlyUsd.GetProperty("dovizler").EnumerateArray()).GetProperty("doviz").GetString());
        var bySupplier = await Ok(await GetAsync(admin, $"/faturalar/ozet?cariId={e.Supplier}"));
        Assert.Equal(1, bySupplier.GetProperty("adet").GetInt32());

        // Şube kapsamı (liste kuralı): SubeA operatörü yalnız SubeA kirasının faturası ve onun iadesini toplar
        // (120 + 60 = 180); SubeB kirasının faturası ve şubesi bilinmeyen kirasız fatura (USD) kapsam dışı.
        var opA = await Ok(await GetAsync(await LoginAsync(e, Who.OperatorA), "/faturalar/ozet"));
        Assert.Equal(2, opA.GetProperty("adet").GetInt32());
        Assert.Equal(180m, Assert.Single(opA.GetProperty("dovizler").EnumerateArray()).GetProperty("genelToplam").GetDecimal());
        Assert.Equal(2, (await PageIdsAsync(await GetAsync(await LoginAsync(e, Who.OperatorA), "/faturalar"))).Count);

        await Problem(await GetAsync(await LoginAsync(e, Who.OperatorPlain), "/faturalar/ozet"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await GetAsync(admin, "/faturalar/ozet?ofis=" + new string('x', 129)), HttpStatusCode.BadRequest, "dogrulama", "ofis");
        // Başka kiracı: boş özet.
        var foreign = await Ok(await GetAsync(await LoginAsync(await SetupAsync(), Who.Admin), "/faturalar/ozet"));
        Assert.Equal(0, foreign.GetProperty("adet").GetInt32());
        Assert.Empty(foreign.GetProperty("dovizler").EnumerateArray());
    }

    [Fact]
    public async Task Penalty_payment_replay_returns_penalty_number_and_sequence()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var (id, line1, _) = await PenaltyAsync(e, s);
        var no = (await Ok(await GetAsync(s, $"/cezalar/{id}"))).GetProperty("ceza").GetProperty("no").GetString();
        var key = NewKey();
        await Ok(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 10m, hesap = "Kasa" }, key));
        var replay = await Problem(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 10m, hesap = "Kasa" }, key),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal($"{no}/1", replay.GetProperty("mevcut").GetProperty("belgeNo").GetString()); // ilk ödeme: sıra 1
        Assert.Equal(1, await DbAsync(e, db => db.PenaltyOdemeleri.CountAsync()));
    }
}
