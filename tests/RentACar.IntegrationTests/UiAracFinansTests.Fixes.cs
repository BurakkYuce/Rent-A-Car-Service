using System.Net;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>F6.1b adversarial düzeltmeleri (#279 turu): M1 geçiş tablosu, L1 ölçek, L2 döviz normalizasyonu, L3 kur alanı.</summary>
public sealed partial class UiAracFinansTests
{
    /// <summary>
    /// BAĞIMSIZ ORACLE — izinli sipariş geçişleri elle yazıldı (servis tablosundan türetilmedi):
    /// Bekliyor→Onaylandı, Bekliyor→TeslimAlındı, Onaylandı→TeslimAlındı, Bekliyor→İptal, Onaylandı→İptal.
    /// Aynı duruma geçiş no-op (200). Geri kalan her çift 409 cakisma (TeslimAlındı ve İptal terminal).
    /// </summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> AllowedOrderTransitions =
    [
        (OrderStatus.Bekliyor, OrderStatus.Onaylandi), (OrderStatus.Bekliyor, OrderStatus.TeslimAlindi),
        (OrderStatus.Onaylandi, OrderStatus.TeslimAlindi), (OrderStatus.Bekliyor, OrderStatus.Iptal),
        (OrderStatus.Onaylandi, OrderStatus.Iptal),
    ];

    private static readonly (OrderStatus To, string Action, string Flag)[] OrderActions =
    [
        (OrderStatus.Onaylandi, "onayla", "onayla"), (OrderStatus.TeslimAlindi, "teslim-al", "teslimAl"),
        (OrderStatus.Iptal, "iptal", "iptal"),
    ];

    [Fact]
    public async Task Order_transition_table_every_pair_and_flags()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorA);
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            // Yetki bayrakları oracle ile aynı olmalı (tek kaynak).
            var probe = await OrderInStateAsync(o, from);
            var permissions = (await Json(await s.C.GetAsync($"{Order}/{probe}"))).GetProperty("yetkiler");
            foreach (var (to, _, flag) in OrderActions)
                Assert.True(AllowedOrderTransitions.Contains((from, to)) == permissions.GetProperty(flag).GetBoolean(), $"{from}→{to} bayrağı");

            foreach (var (to, action, _) in OrderActions)
            {
                var id = await OrderInStateAsync(o, from);
                var r = await Gonder(s, HttpMethod.Post, $"{Order}/{id}/{action}");
                if (from == to || AllowedOrderTransitions.Contains((from, to)))
                    Assert.Equal(to.ToString(), (await Json(r)).GetProperty("durum").GetString());
                else
                {
                    await ExpectProblem(r, HttpStatusCode.Conflict, "cakisma");
                    Assert.Equal(from.ToString(), (await Json(await s.C.GetAsync($"{Order}/{id}"))).GetProperty("durum").GetString());
                }
            }
        }
    }

    [Fact]
    public async Task Order_two_tabs_cannot_undo_delivery()
    {
        var o = await SetUpEnvironmentAsync();
        var tab1 = await LoginAsync(o, Kim.OperatorA);
        var tab2 = await LoginAsync(o, Kim.OperatorA);
        var id = await OrderInStateAsync(o, OrderStatus.Onaylandi);
        await Json(await Gonder(tab1, HttpMethod.Post, $"{Order}/{id}/teslim-al"));
        await ExpectProblem(await Gonder(tab2, HttpMethod.Post, $"{Order}/{id}/onayla"), HttpStatusCode.Conflict, "cakisma");
        await ExpectProblem(await Gonder(tab2, HttpMethod.Post, $"{Order}/{id}/iptal"), HttpStatusCode.Conflict, "cakisma");
        var d = await Json(await tab2.C.GetAsync($"{Order}/{id}"));
        Assert.Equal("TeslimAlindi", d.GetProperty("durum").GetString());
        Assert.False(d.GetProperty("yetkiler").GetProperty("teslimAl").GetBoolean());
    }

    private async Task<Guid> OrderInStateAsync(Ortam o, OrderStatus state)
    {
        var order = new AracSiparis { No = "SP-T" + Guid.NewGuid().ToString("N")[..8], Tedarikci = "Bayi", Adet = 1, BirimFiyat = 10m, Durum = state };
        await WriteDataAsync(o.TenantId, db => db.AracSiparisleri.Add(order));
        return order.Id;
    }

    [Fact]
    public async Task Excess_scale_rejected_and_exact_repeat_is_same_content()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan, new { bankaAdi = "Z", vehicleId = vehicle, krediTutari = 1000.00005m, faizOran = 0.1m, taksitSayisi = 1 }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "krediTutari");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan, new { bankaAdi = "Z", vehicleId = vehicle, krediTutari = 1000m, faizOran = 0.123456m, taksitSayisi = 1 }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "faizOran");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Installment, new { cariId = o.MusteriId, vade = DateTimeOffset.UtcNow, taksitTutari = 10.005m }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "taksitTutari");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Order, new { tedarikci = "B", birimFiyat = 1.00001m }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "birimFiyat");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Damage, new { vehicleId = vehicle, tahminiTutar = 1.00001m }),
            HttpStatusCode.BadRequest, "dogrulama", "tahminiTutar");

        // Kolon ölçeğindeki değer birebir yazılır → birebir tekrar "aynı içerik".
        var body = new { bankaAdi = "Z", vehicleId = vehicle, krediTutari = 1000.1234m, faizOran = 0.1234m, taksitSayisi = 2, doviz = "TL" };
        var key = Key();
        await Json(await Gonder(s, HttpMethod.Post, Loan, body, key), HttpStatusCode.Created);
        var m = await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan, body, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(m.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var t = new { cariId = o.MusteriId, vade = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), taksitTutari = 10.5m, doviz = "TL" };
        var tk = Key();
        await Json(await Gonder(s, HttpMethod.Post, Installment, t, tk), HttpStatusCode.Created);
        Assert.True((await ExpectProblem(await Gonder(s, HttpMethod.Post, Installment, t, tk), HttpStatusCode.Conflict, "mukerrer"))
            .GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
    }

    [Fact]
    public async Task Installment_account_currency_alias_and_missing_rate_field()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var account = new FinancialAccount { Kod = "K-TRL", Ad = "Eski Kasa", Tur = "Kasa", Doviz = "TRL", Aktif = true };
        await WriteDataAsync(o.TenantId, db => db.FinancialAccounts.Add(account));
        var s = await LoginAsync(o, Kim.Admin);

        // L2: "TRL" tanımlı kasa TRY kredisinin taksidini kabul eder (TRL ≡ TRY).
        var tryLoan = (await Json(await Gonder(s, HttpMethod.Post, Loan, LoanBody(vehicle), Key()), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var paid = await Json(await Gonder(s, HttpMethod.Post, $"{Loan}/{tryLoan}/taksit-ode", new { sira = 1, hesap = "Kasa", hesapId = account.Id }, Key()));
        Assert.Equal(1100m, paid.GetProperty("tutar").GetDecimal());

        // L3: kuru hiç tanımlanmamış dövizde taksit → errors[kur], yazım yok.
        var fxLoan = (await Json(await Gonder(s, HttpMethod.Post, Loan,
            new { bankaAdi = "Z", vehicleId = vehicle, krediTutari = 100m, faizOran = 0m, taksitSayisi = 1, doviz = "XQZ", kur = 30m }, Key()),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Loan}/{fxLoan}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        Assert.Equal(1, await LoanExpenseCountAsync(o.TenantId));
    }
}
