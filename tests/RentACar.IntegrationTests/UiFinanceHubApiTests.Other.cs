using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceHubApiTests
{
    // ------------------------------------------------------------ depozito iade (E10) / mahsup (E11)

    [Fact]
    public async Task Deposit_refund_and_offset_respect_held_amount()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/depozito/al", new { cariId = e.CustomerA, tutar = 300m, hesap = "Kasa" }, NewKey()));
        var refundKey = NewKey();
        var refund = new { cariId = e.CustomerA, tutar = 100m, hesap = "Kasa" };
        var refundId = await IdOf(await PostAsync(s, "/depozito/iade", refund, refundKey));
        await IdOf(await PostAsync(s, "/depozito/mahsup", new { cariId = e.CustomerA, tutar = 50m }, NewKey()));

        // Oracle: tutulan 300 − 100 − 50 = 150; mahsup cariyi alacaklandırır → bakiye −50.
        var list = await Ok(await GetAsync(s, "/depozito"));
        Assert.Equal(150m, list.EnumerateArray().Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerA).GetProperty("bakiye").GetDecimal());
        Assert.Equal(-50m, await BalanceAsync(e, e.CustomerA));
        var bal = await Ok(await GetAsync(s, $"/cariler/{e.CustomerA}/bakiye"));
        Assert.Equal(150m, bal.GetProperty("depozitoBakiye").GetDecimal());

        Assert.Equal(refundId, await IdOf(await PostAsync(s, "/depozito/iade", refund, refundKey)));
        await Problem(await PostAsync(s, "/depozito/iade", refund with { tutar = 101m }, refundKey), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/depozito/iade", refund with { tutar = 151m }, NewKey()), HttpStatusCode.BadRequest, "dogrulama");
        await Problem(await PostAsync(s, "/depozito/mahsup", new { cariId = e.CustomerB, tutar = 1m }, NewKey()), HttpStatusCode.BadRequest, "dogrulama");
        await Problem(await PostAsync(s, "/depozito/mahsup", new { cariId = Guid.NewGuid(), tutar = 1m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await AllLedgerBalancedAsync(e);
    }

    // #299 L2 — depozito anahtarı TÜM depozito türlerinde tekil: al'ın anahtarıyla iade/mahsup/irat 409 mukerrer
    // (mevcut = al kaydı, ayniIcerik=false) ve HİÇBİR ŞEY yazılmaz; ters yönde (iade anahtarıyla al) de aynı.
    [Fact]
    public async Task Deposit_key_is_unique_across_deposit_operation_types()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var takeKey = NewKey();
        var takeId = await IdOf(await PostAsync(s, "/depozito/al", new { cariId = e.CustomerA, tutar = 300m, hesap = "Kasa" }, takeKey));

        var refund = await Problem(await PostAsync(s, "/depozito/iade", new { cariId = e.CustomerA, tutar = 100m, hesap = "Kasa" }, takeKey),
            HttpStatusCode.Conflict, "mukerrer");
        var existing = refund.GetProperty("mevcut");
        Assert.Equal(takeId, existing.GetProperty("id").GetGuid());
        Assert.False(existing.GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(300m, existing.GetProperty("tutar").GetDecimal());
        Assert.Equal("TRY", existing.GetProperty("doviz").GetString());
        await Problem(await PostAsync(s, "/depozito/mahsup", new { cariId = e.CustomerA, tutar = 50m }, takeKey),
            HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/depozito/irat", new { cariId = e.CustomerA, tutar = 20m }, takeKey),
            HttpStatusCode.Conflict, "mukerrer");
        // Başka carinin aynı anahtarlı iadesi de (farklı cari kilidi) aynı kayda çarpar.
        await Problem(await PostAsync(s, "/depozito/iade", new { cariId = e.CustomerB, tutar = 1m, hesap = "Kasa" }, takeKey),
            HttpStatusCode.Conflict, "mukerrer");

        // Oracle: yalnız al kümesi yazıldı (2 satır, 300 TL); tutulan depozito 300.
        var rows = await LedgerAsync(e, takeId);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("DepozitoAl", r.SourceType));
        var list = await Ok(await GetAsync(s, "/depozito"));
        Assert.Equal(300m, list.EnumerateArray().Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerA).GetProperty("bakiye").GetDecimal());

        // Ters yön: iade anahtarıyla al → 409; aynı iadenin birebir tekrarı sessiz (200 aynı id).
        var refundKey = NewKey();
        var refundBody = new { cariId = e.CustomerA, tutar = 100m, hesap = "Kasa" };
        var refundId = await IdOf(await PostAsync(s, "/depozito/iade", refundBody, refundKey));
        await Problem(await PostAsync(s, "/depozito/al", new { cariId = e.CustomerA, tutar = 100m, hesap = "Kasa" }, refundKey),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(refundId, await IdOf(await PostAsync(s, "/depozito/iade", refundBody, refundKey)));
        // Oracle: 300 − 100 = 200.
        list = await Ok(await GetAsync(s, "/depozito"));
        Assert.Equal(200m, list.EnumerateArray().Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerA).GetProperty("bakiye").GetDecimal());
        await AllLedgerBalancedAsync(e);
    }

    // r314 P1 — aynı anahtarla EŞZAMANLI beş farklı depozito işlemi (al, iade, mahsup, irat, başka carinin iadesi): tam
    // olarak biri yazılır (2 satırlık tek küme), kalanlar 409; 500 yok. Anahtar kilidi (cari kilidinden önce) bunu sağlar.
    [Fact]
    public async Task Concurrent_cross_type_requests_with_one_key_write_exactly_once()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/depozito/al", new { cariId = e.CustomerA, tutar = 1000m, hesap = "Kasa" }, NewKey()));
        for (var round = 0; round < 4; round++)
        {
            var before = await DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking().CountAsync());
            var k = NewKey();
            var rs = await Task.WhenAll(
                PostAsync(s, "/depozito/al", new { cariId = e.CustomerA, tutar = 30m, hesap = "Kasa" }, k),
                PostAsync(s, "/depozito/iade", new { cariId = e.CustomerA, tutar = 10m, hesap = "Kasa" }, k),
                PostAsync(s, "/depozito/mahsup", new { cariId = e.CustomerA, tutar = 5m }, k),
                PostAsync(s, "/depozito/irat", new { cariId = e.CustomerA, tutar = 2m }, k),
                PostAsync(s, "/depozito/iade", new { cariId = e.CustomerB, tutar = 1m, hesap = "Kasa" }, k));
            var codes = rs.Select(r => (int)r.StatusCode).ToList();
            Assert.Equal(1, codes.Count(c => c == 200));
            Assert.All(codes.Take(4), c => Assert.Contains(c, new[] { 200, 409 }));
            // B'nin depozitosu yok: ilk kilidi o alırsa bakiye çitinde 400, sonra gelirse 409 — ama asla yazmaz.
            Assert.Contains(codes[4], new[] { 400, 409 });
            // ELLE: her tür tek kümede 2 satır yazar (borç + alacak).
            Assert.Equal(2, await DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking().CountAsync()) - before);
        }
        await AllLedgerBalancedAsync(e);
    }

    // ------------------------------------------------------------ ekstre + ters kayıt (E08)

    [Fact]
    public async Task Statement_shows_running_balance_and_reverse_restores_it()
    {
        var e = await SetupAsync();
        var foreign = await ForeignCustomerAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerB, yon = "Borclandir", tutar = 200m }, NewKey()));
        var collection = await IdOf(await PostAsync(s, "/api/ui/v1/finans/tahsilat",
            new { cariId = e.CustomerB, tutar = 80m, hesap = "Kasa", aciklama = "Nakit" }, NewKey()));

        var st = await Ok(await GetAsync(s, $"/cariler/{e.CustomerB}/ekstre?mod=ozet"));
        Assert.Equal(120m, st.GetProperty("bakiye").GetDecimal()); // 200 − 80
        var lines = st.GetProperty("satirlar").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(120m, lines[^1].GetProperty("yuruyen").GetDecimal());
        Assert.Equal(200m, st.GetProperty("toplamBorc").GetDecimal());
        Assert.Equal(80m, st.GetProperty("toplamAlacak").GetDecimal());
        var summary = st.GetProperty("ozet").EnumerateArray().ToList();
        Assert.Equal(200m, summary.Sum(o => o.GetProperty("borc").GetDecimal()));
        Assert.Equal(collection, lines.Single(l => l.GetProperty("kaynak").GetString() == "Tahsilat").GetProperty("kasaIslemId").GetGuid());

        await Problem(await GetAsync(s, $"/cariler/{foreign}/ekstre"), HttpStatusCode.NotFound, null);
        var denied = await LoginAsync(e, Who.AccountantNoReverse);
        await Problem(await PostAsync(denied, $"/kasa/islemler/{collection}/ters", null), HttpStatusCode.Forbidden, "yetki_yok");

        await IdOf(await PostAsync(s, $"/kasa/islemler/{collection}/ters", null));
        Assert.Equal(200m, await BalanceAsync(e, e.CustomerB));
        await Problem(await PostAsync(s, $"/kasa/islemler/{collection}/ters", null), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, $"/kasa/islemler/{Guid.NewGuid()}/ters", null), HttpStatusCode.NotFound, null);

        var tx = await Ok(await GetAsync(s, "/kasa/islemler?tip=Tahsilat&kanal=Masaüstü"));
        Assert.Equal(2, tx.GetProperty("liste").GetProperty("toplam").GetInt32()); // tahsilat + tersi
        Assert.Equal("Hub Beta", tx.GetProperty("liste").GetProperty("kayitlar")[0].GetProperty("cariAd").GetString());
        await AllLedgerBalancedAsync(e);
    }

    [Fact]
    public async Task Reverse_of_rental_collection_checks_branch_scope_first()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var collection = await IdOf(await PostAsync(admin, "/api/ui/v1/finans/tahsilat",
            new { cariId = e.CustomerA, kiraId = e.Rental, tutar = 60m, hesap = "Kasa" }, NewKey()));
        var other = await LoginAsync(e, Who.OperatorB); // SubeB; kira SubeA
        await Problem(await PostAsync(other, $"/kasa/islemler/{collection}/ters", null), HttpStatusCode.Forbidden, "yetki_yok");
        var own = await LoginAsync(e, Who.OperatorA);
        await IdOf(await PostAsync(own, $"/kasa/islemler/{collection}/ters", null));
    }

    // ------------------------------------------------------------ dönem kapanışı (E36) + otomatik tahsilat (E20)

    [Fact]
    public async Task Period_close_state_lock_and_unlock()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        await IdOf(await PostAsync(s, "/bakiye-duzeltme", new { cariId = e.CustomerA, yon = "Borclandir", tutar = 40m }, NewKey()));
        var state = await Ok(await GetAsync(s, "/donem-kapanis"));
        Assert.Equal(0m, state.GetProperty("toplamBakiye").GetDecimal()); // defter dengesi
        Assert.Equal(40m, state.GetProperty("toplamBorc").GetDecimal());

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));
        await Problem(await PostAsync(s, "/donem-kapanis/kilitle", new { kapanisTarihi = tomorrow }), HttpStatusCode.BadRequest, "dogrulama", "kapanisTarihi");
        var day = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-3));
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(s, "/donem-kapanis/kilitle", new { kapanisTarihi = day })).StatusCode);
        await Problem(await PostAsync(s, "/donem-kapanis/kilitle", new { kapanisTarihi = day }), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(day.ToString("yyyy-MM-dd"), (await Ok(await GetAsync(s, "/donem-kapanis"))).GetProperty("kapanisTarihi").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await PostAsync(s, "/donem-kapanis/ac", null)).StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await Ok(await GetAsync(s, "/donem-kapanis"))).GetProperty("kapanisTarihi").ValueKind);
    }

    [Fact]
    public async Task Auto_collection_lists_and_skips_unknown_selection()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var list = await Ok(await GetAsync(s, "/otomatik-tahsilat"));
        Assert.False(list.GetProperty("jobAcik").GetBoolean());
        var r = await Ok(await PostAsync(s, "/otomatik-tahsilat/calistir",
            new { secim = new[] { new { kiraId = e.Rental, donemSira = 1 } }, tahsilat = true, hesap = "Kasa" }));
        Assert.Equal(0, r.GetProperty("kesilen").GetInt32());
        Assert.Equal(1, r.GetProperty("atlananlar").GetArrayLength());
        await Problem(await PostAsync(s, "/otomatik-tahsilat/calistir", new { secim = Array.Empty<object>(), tahsilat = true, hesap = "Kasa" }),
            HttpStatusCode.BadRequest, "dogrulama", "secim");
        Assert.Equal(0, await CashCountAsync(e));
    }
}
