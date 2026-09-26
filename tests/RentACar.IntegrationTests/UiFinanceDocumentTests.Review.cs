using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.VehicleSales;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>#286 adversarial M2 (kuruş altı tutar), M3 (bağlama PUT'u sürümlü), Low-9 (işle ↔ giderleştir), Low-7 (KVKK).</summary>
public sealed partial class UiFinanceDocumentTests
{
    [Fact]
    public async Task Sub_cent_amounts_are_rejected_with_field_errors_and_nothing_is_written()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        await Problem(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 0.001m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "netTutar");
        await Problem(await PostAsync(s, "/faturalar/manuel", Manual(e.Customer, 900_000_000_000_000m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "netTutar");
        await Problem(await PostAsync(s, "/giderler", new { tip = "Genel", netTutar = 0.004m, kdvOrani = 0m, odemeYontemi = "Nakit" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "netTutar");
        await Problem(await PostAsync(s, "/satislar", new { aracId = e.RentalVehicle, aliciCariId = e.Customer, satisNet = 333.333m, kdvOrani = 0.20m }),
            HttpStatusCode.BadRequest, "dogrulama", "satisNet");
        var (id, line1, _) = await PenaltyAsync(e, s);
        await Problem(await PostAsync(s, $"/cezalar/{id}/odeme", new { satirId = line1, tutar = 0.0001m, hesap = "Kasa" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "tutar");
        Assert.Equal(0, await DbAsync(e, db => db.Invoices.CountAsync()));
        Assert.Equal(0, await DbAsync(e, db => db.Expenses.CountAsync()));
        Assert.Equal(0, await DbAsync(e, db => db.VehicleSales.CountAsync()));
        Assert.Equal(0, await DbAsync(e, db => db.PenaltyOdemeleri.CountAsync()));
    }

    /// <summary>Servis düzeyi (Blazor yolu): kontrol yuvarlamadan SONRA; araç satışında net kuruşa sabitlenir.</summary>
    [Fact]
    public async Task Service_level_checks_after_rounding_and_sale_net_is_fixed_to_cents()
    {
        var e = await SetupAsync();
        var sale = await ReadAsync(e.TenantId, async sp =>
        {
            await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<InvoiceService>()
                .CreateManualAsync(new ManualInvoiceInput { CariId = e.Customer, NetTutar = 0.001m, KdvOrani = 0.20m }));
            await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<ExpenseService>()
                .CreateAsync(new ExpenseInput { Tip = ExpenseType.Genel, NetTutar = 0.004m, KdvOrani = 0m, OdemeYontemi = PaymentMethod.Nakit }));
            var v = await VehicleAsync(sp, "SubeA");
            return await sp.GetRequiredService<VehicleSaleService>().CreateAsync(new VehicleSaleInput
            {
                VehicleId = v, AliciCariId = e.Customer, SatisNet = 333.333m, KdvOrani = 0.20m,
            });
        });
        // 333,333 → 333,33; KDV 333,33 × 0,20 = 66,666 → 66,67; brüt 400,00 — küme dengeli.
        var set = await LedgerAsync(e, sale);
        Line(set, LedgerAccountType.Cari, e.Customer, LedgerDirection.Debit, 400.00m);
        Line(set, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 333.33m);
        Line(set, LedgerAccountType.Kdv, null, LedgerDirection.Credit, 66.67m);
        Assert.Equal(0, await DbAsync(e, db => db.Invoices.CountAsync()));
        Assert.Equal(0, await DbAsync(e, db => db.Expenses.CountAsync()));
    }

    private async Task<Guid> IncomingAsync(Env e, Session s, bool approve = true)
    {
        var id = await IdOf(await PostAsync(s, "/gelen-efatura", new
        {
            ettn = Guid.NewGuid().ToString(), gonderenVkn = "1234567890", gonderenUnvan = "Tedarikçi A.Ş.",
            netTutar = 1000m, kdvTutar = 200m, genelToplam = 1200m, tarih = TestZaman.GunSonra(-1),
        }));
        if (approve) await Ok(await PostAsync(s, $"/gelen-efatura/{id}/onayla", null));
        return id;
    }

    [Fact]
    public async Task Incoming_invoice_link_put_requires_current_version()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IncomingAsync(e, s);
        var surum = (await Ok(await GetAsync(s, $"/gelen-efatura/{id}"))).GetProperty("surum").GetString()!;
        object Body(string? v) => new { surum = v, kdv20Matrah = 1000m, kdv20 = 200m, cariId = e.Supplier };

        await Problem(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{id}/bag", Body(null), null), HttpStatusCode.BadRequest, "dogrulama", "surum");
        var next = (await Ok(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{id}/bag", Body(surum), null))).GetProperty("surum").GetString();
        Assert.NotEqual(surum, next);
        // Bayat sekme (eski sürüm) başkasının kaydını ezmez.
        await Problem(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{id}/bag", new { surum, kdv20Matrah = 0m, kdv20 = 0m }, null),
            HttpStatusCode.Conflict, "cakisma");
        var row = await DbAsync(e, db => db.GelenEFaturalar.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.Equal(1000m, row.Kdv20Matrah);
        Assert.Equal(e.Supplier, row.CariId);
    }

    [Fact]
    public async Task Incoming_invoice_process_and_expense_cannot_both_succeed()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IncomingAsync(e, s);
        var surum = (await Ok(await GetAsync(s, $"/gelen-efatura/{id}"))).GetProperty("surum").GetString();
        await Ok(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{id}/bag", new { surum, kdv20Matrah = 1000m, kdv20 = 200m, cariId = e.Supplier }, null));

        // Giderleştirme önce kuyruğa girer (talep kilidi), "işle" ardından: işle 400, defter tek küme.
        var (expense, process) = await OrderedAsync(e, "GelenEFaturalar", id,
            () => PostAsync(s, $"/gelen-efatura/{id}/giderlestir", new { }),
            () => PostAsync(s, $"/gelen-efatura/{id}/isle", null));
        await Ok(expense);
        await Problem(process, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Single(await DbAsync(e, db => db.Expenses.AsNoTracking().ToListAsync()));
        var row = await DbAsync(e, db => db.GelenEFaturalar.AsNoTracking().SingleAsync(x => x.Id == id));
        Assert.NotNull(row.GiderlestirilmeUtc);

        // Ters sıra: işle önce → giderleştirme 400, hiç gider yazılmaz.
        var other = await IncomingAsync(e, s);
        var surum2 = (await Ok(await GetAsync(s, $"/gelen-efatura/{other}"))).GetProperty("surum").GetString();
        await Ok(await SendAsync(s, HttpMethod.Put, $"/gelen-efatura/{other}/bag", new { surum = surum2, kdv20Matrah = 1000m, kdv20 = 200m, cariId = e.Supplier }, null));
        var (process2, expense2) = await OrderedAsync(e, "GelenEFaturalar", other,
            () => PostAsync(s, $"/gelen-efatura/{other}/isle", null),
            () => PostAsync(s, $"/gelen-efatura/{other}/giderlestir", new { }));
        await Ok(process2);
        await Problem(expense2, HttpStatusCode.BadRequest, "dogrulama");
        Assert.Single(await DbAsync(e, db => db.Expenses.AsNoTracking().ToListAsync()));
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Penalty_detail_hides_notice_phone_when_customer_phone_is_anonymized()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var id = await IdOf(await PostAsync(s, "/cezalar", new
        {
            cezaTuru = "Hız", aracId = e.RentalVehicle, cariId = e.Customer, cepTel = "05321234567",
            kalemler = new[] { new { tutar = 10m } },
        }));
        Assert.Equal("05321234567", (await Ok(await GetAsync(s, $"/cezalar/{id}"))).GetProperty("cepTel").GetString());
        await DbAsync(e, db => db.Customers.Where(c => c.Id == e.Customer).ExecuteUpdateAsync(x => x.SetProperty(c => c.AnonimTelefon, true)));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await Ok(await GetAsync(s, $"/cezalar/{id}"))).GetProperty("cepTel").ValueKind);
    }
}
