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
    private static readonly HashSet<(SiparisDurum From, SiparisDurum To)> AllowedOrderTransitions =
    [
        (SiparisDurum.Bekliyor, SiparisDurum.Onaylandi), (SiparisDurum.Bekliyor, SiparisDurum.TeslimAlindi),
        (SiparisDurum.Onaylandi, SiparisDurum.TeslimAlindi), (SiparisDurum.Bekliyor, SiparisDurum.Iptal),
        (SiparisDurum.Onaylandi, SiparisDurum.Iptal),
    ];

    private static readonly (SiparisDurum To, string Action, string Flag)[] OrderActions =
    [
        (SiparisDurum.Onaylandi, "onayla", "onayla"), (SiparisDurum.TeslimAlindi, "teslim-al", "teslimAl"),
        (SiparisDurum.Iptal, "iptal", "iptal"),
    ];

    [Fact]
    public async Task Order_transition_table_every_pair_and_flags()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorA);
        foreach (var from in Enum.GetValues<SiparisDurum>())
        {
            // Yetki bayrakları oracle ile aynı olmalı (tek kaynak).
            var probe = await OrderInStateAsync(o, from);
            var yetkiler = (await Json(await s.C.GetAsync($"{Siparis}/{probe}"))).GetProperty("yetkiler");
            foreach (var (to, _, flag) in OrderActions)
                Assert.True(AllowedOrderTransitions.Contains((from, to)) == yetkiler.GetProperty(flag).GetBoolean(), $"{from}→{to} bayrağı");

            foreach (var (to, action, _) in OrderActions)
            {
                var id = await OrderInStateAsync(o, from);
                var r = await Gonder(s, HttpMethod.Post, $"{Siparis}/{id}/{action}");
                if (from == to || AllowedOrderTransitions.Contains((from, to)))
                    Assert.Equal(to.ToString(), (await Json(r)).GetProperty("durum").GetString());
                else
                {
                    await ProblemBekle(r, HttpStatusCode.Conflict, "cakisma");
                    Assert.Equal(from.ToString(), (await Json(await s.C.GetAsync($"{Siparis}/{id}"))).GetProperty("durum").GetString());
                }
            }
        }
    }

    [Fact]
    public async Task Order_two_tabs_cannot_undo_delivery()
    {
        var o = await OrtamKurAsync();
        var tab1 = await GirisAsync(o, Kim.OperatorA);
        var tab2 = await GirisAsync(o, Kim.OperatorA);
        var id = await OrderInStateAsync(o, SiparisDurum.Onaylandi);
        await Json(await Gonder(tab1, HttpMethod.Post, $"{Siparis}/{id}/teslim-al"));
        await ProblemBekle(await Gonder(tab2, HttpMethod.Post, $"{Siparis}/{id}/onayla"), HttpStatusCode.Conflict, "cakisma");
        await ProblemBekle(await Gonder(tab2, HttpMethod.Post, $"{Siparis}/{id}/iptal"), HttpStatusCode.Conflict, "cakisma");
        var d = await Json(await tab2.C.GetAsync($"{Siparis}/{id}"));
        Assert.Equal("TeslimAlindi", d.GetProperty("durum").GetString());
        Assert.False(d.GetProperty("yetkiler").GetProperty("teslimAl").GetBoolean());
    }

    private async Task<Guid> OrderInStateAsync(Ortam o, SiparisDurum state)
    {
        var order = new AracSiparis { No = "SP-T" + Guid.NewGuid().ToString("N")[..8], Tedarikci = "Bayi", Adet = 1, BirimFiyat = 10m, Durum = state };
        await VeriYazAsync(o.TenantId, db => db.AracSiparisleri.Add(order));
        return order.Id;
    }

    [Fact]
    public async Task Excess_scale_rejected_and_exact_repeat_is_same_content()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi, new { bankaAdi = "Z", vehicleId = arac, krediTutari = 1000.00005m, faizOran = 0.1m, taksitSayisi = 1 }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "krediTutari");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi, new { bankaAdi = "Z", vehicleId = arac, krediTutari = 1000m, faizOran = 0.123456m, taksitSayisi = 1 }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "faizOran");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Taksit, new { cariId = o.MusteriId, vade = DateTimeOffset.UtcNow, taksitTutari = 10.005m }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "taksitTutari");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Siparis, new { tedarikci = "B", birimFiyat = 1.00001m }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "birimFiyat");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Hasar, new { vehicleId = arac, tahminiTutar = 1.00001m }),
            HttpStatusCode.BadRequest, "dogrulama", "tahminiTutar");

        // Kolon ölçeğindeki değer birebir yazılır → birebir tekrar "aynı içerik".
        var body = new { bankaAdi = "Z", vehicleId = arac, krediTutari = 1000.1234m, faizOran = 0.1234m, taksitSayisi = 2, doviz = "TL" };
        var key = Anahtar();
        await Json(await Gonder(s, HttpMethod.Post, Kredi, body, key), HttpStatusCode.Created);
        var m = await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi, body, key), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(m.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        var t = new { cariId = o.MusteriId, vade = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()), taksitTutari = 10.5m, doviz = "TL" };
        var tk = Anahtar();
        await Json(await Gonder(s, HttpMethod.Post, Taksit, t, tk), HttpStatusCode.Created);
        Assert.True((await ProblemBekle(await Gonder(s, HttpMethod.Post, Taksit, t, tk), HttpStatusCode.Conflict, "mukerrer"))
            .GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
    }

    [Fact]
    public async Task Installment_account_currency_alias_and_missing_rate_field()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var account = new FinancialAccount { Kod = "K-TRL", Ad = "Eski Kasa", Tur = "Kasa", Doviz = "TRL", Aktif = true };
        await VeriYazAsync(o.TenantId, db => db.FinancialAccounts.Add(account));
        var s = await GirisAsync(o, Kim.Admin);

        // L2: "TRL" tanımlı kasa TRY kredisinin taksidini kabul eder (TRL ≡ TRY).
        var tryLoan = (await Json(await Gonder(s, HttpMethod.Post, Kredi, KrediGovde(arac), Anahtar()), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var paid = await Json(await Gonder(s, HttpMethod.Post, $"{Kredi}/{tryLoan}/taksit-ode", new { sira = 1, hesap = "Kasa", hesapId = account.Id }, Anahtar()));
        Assert.Equal(1100m, paid.GetProperty("tutar").GetDecimal());

        // L3: kuru hiç tanımlanmamış dövizde taksit → errors[kur], yazım yok.
        var fxLoan = (await Json(await Gonder(s, HttpMethod.Post, Kredi,
            new { bankaAdi = "Z", vehicleId = arac, krediTutari = 100m, faizOran = 0m, taksitSayisi = 1, doviz = "XQZ", kur = 30m }, Anahtar()),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kredi}/{fxLoan}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        Assert.Equal(1, await KrediGiderSayisiAsync(o.TenantId));
    }
}
