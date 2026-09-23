using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    /// <summary>ORACLE: 12.000 × 0,10 × 12/12 = 1.200 faiz → 13.200 / 12 = 1.100 aylık.</summary>
    private static object KrediGovde(Guid arac, decimal tutar = 12_000m) => new
    { bankaAdi = "Ziraat", vehicleId = arac, krediTutari = tutar, faizOran = 0.10m, taksitSayisi = 12 };

    private async Task<(decimal Borc, decimal Alacak, int Satir)> DefterAsync(Guid tenant, Guid giderId)
        => await VeriOkuAsync(tenant, async db =>
        {
            var l = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == giderId).ToListAsync();
            return (l.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
                l.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase), l.Count);
        });

    private Task<int> KrediGiderSayisiAsync(Guid tenant) => VeriOkuAsync(tenant,
        db => db.Expenses.AsNoTracking().CountAsync(e => e.Tip == Domain.Enums.ExpenseType.Finansman));

    [Fact]
    public async Task Kredi_oracle_taksit_defter_dengeli_ve_idempotent()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var s = await GirisAsync(o, Kim.Admin);

        // Anahtarsız oluşturma 400; TRY'de kur ≠ 1 400.
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi, KrediGovde(arac)), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi,
            new { bankaAdi = "Z", vehicleId = arac, krediTutari = 100m, faizOran = 0m, taksitSayisi = 1, kur = 5m }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");

        var k1 = Anahtar();
        var id = (await Json(await Gonder(s, HttpMethod.Post, Kredi, KrediGovde(arac), k1), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var tekrar = await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi, KrediGovde(arac), k1), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(tekrar.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(id, tekrar.GetProperty("mevcut").GetProperty("id").GetGuid());
        var farkli = await ProblemBekle(await Gonder(s, HttpMethod.Post, Kredi, KrediGovde(arac, 999m), k1), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(farkli.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1, (await Json(await s.C.GetAsync(Kredi))).GetProperty("toplam").GetInt32());

        var d = await Json(await s.C.GetAsync($"{Kredi}/{id}"));
        Assert.Equal(1200m, d.GetProperty("ozet").GetProperty("toplamFaiz").GetDecimal());
        Assert.Equal(13200m, d.GetProperty("ozet").GetProperty("toplamGeriOdeme").GetDecimal());
        Assert.Equal(1, d.GetProperty("sonrakiTaksit").GetProperty("sira").GetInt32());
        Assert.Equal(1100m, d.GetProperty("sonrakiTaksit").GetProperty("tutar").GetDecimal());

        // Taksit 1: gerçek gider + dengeli defter (Borç Gider[araç] 1.100 = Alacak Kasa 1.100).
        var t1 = Anahtar();
        var odeme = await Json(await Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, t1));
        Assert.Equal(1100m, odeme.GetProperty("tutar").GetDecimal());
        var giderId = odeme.GetProperty("giderId").GetGuid();
        Assert.Equal((1100m, 1100m, 2), await DefterAsync(o.TenantId, giderId));
        var giderArac = await VeriOkuAsync(o.TenantId, db => db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceId == giderId && e.Direction == LedgerDirection.Debit).Select(e => e.AccountRef).SingleAsync());
        Assert.Equal(arac, giderArac);

        // Kaybolan yanıttan sonraki tekrar: 409 mevcut (aynı içerik), İKİNCİ taksit ödenmez.
        var m = await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, t1),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(m.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(giderId, m.GetProperty("mevcut").GetProperty("id").GetGuid());
        var mf = await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Banka" }, t1),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(mf.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        // Bayat ekran (yeni anahtar, eski sıra) → cakisma, yazım yok.
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Anahtar()),
            HttpStatusCode.Conflict, "cakisma");
        await ProblemBekle(await Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 2, hesap = "Kasa" }),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(1, await KrediGiderSayisiAsync(o.TenantId));

        // Eşzamanlı: iki sekme farklı anahtarla sıra 2 → TEK taksit; aynı anahtarla N gönderim → TEK taksit.
        var farkliAnahtar = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 2, hesap = "Kasa" }, Anahtar())));
        Assert.Equal(1, farkliAnahtar.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(farkliAnahtar.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var t3 = Anahtar();
        var ayniAnahtar = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Gonder(s, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 3, hesap = "Kasa" }, t3)));
        Assert.Equal(1, ayniAnahtar.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, await KrediGiderSayisiAsync(o.TenantId));
        Assert.Equal(3, (await Json(await s.C.GetAsync($"{Kredi}/{id}"))).GetProperty("odenenTaksit").GetInt32());

        // Özet: kalan = 13.200 − 3 × 1.100 = 9.900.
        Assert.Equal(9900m, (await Json(await s.C.GetAsync($"{Kredi}/ozet"))).GetProperty("toplamKrediBorcu").GetDecimal());
    }

    [Fact]
    public async Task Kredi_kapsam_varlik_ve_iptal()
    {
        var o = await OrtamKurAsync();
        var aracA = await AracAsync(o, "SubeA");
        var a = await GirisAsync(o, Kim.OperatorA);
        var id = (await Json(await Gonder(a, HttpMethod.Post, Kredi, KrediGovde(aracA), Anahtar()), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        var b = await GirisAsync(o, Kim.OperatorB);
        await ProblemBekle(await b.C.GetAsync($"{Kredi}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Kredi))).GetProperty("toplam").GetInt32());
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Kredi, KrediGovde(aracA), Anahtar()), HttpStatusCode.Forbidden, "yetki_yok");
        await ProblemBekle(await Gonder(b, HttpMethod.Post, Kredi,
            new { bankaAdi = "Z", krediTutari = 100m, faizOran = 0m, taksitSayisi = 1 }, Anahtar()), HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        await ProblemBekle(await Gonder(a, HttpMethod.Post, Kredi,
            new { bankaAdi = "Z", vehicleId = Guid.NewGuid(), krediTutari = 100m, faizOran = 0m, taksitSayisi = 1 }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        await ProblemBekle(await Gonder(a, HttpMethod.Post, Kredi,
            new { bankaAdi = "Z", vehicleId = aracA, cariId = Guid.NewGuid(), krediTutari = 100m, faizOran = 0m, taksitSayisi = 1 }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        // Operatörde FinanceWrite ve OperationsDelete yok.
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(a, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Anahtar())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(a, HttpMethod.Post, $"{Kredi}/{id}/iptal")).StatusCode);

        var adm = await GirisAsync(o, Kim.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(adm, HttpMethod.Post, $"{Kredi}/{id}/iptal")).StatusCode);
        await ProblemBekle(await Gonder(adm, HttpMethod.Post, $"{Kredi}/{id}/iptal"), HttpStatusCode.BadRequest, "dogrulama");
        await ProblemBekle(await Gonder(adm, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Anahtar()),
            HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(0, await KrediGiderSayisiAsync(o.TenantId));

        // Başka kiracı: 404 (RLS).
        var x = await GirisAsync(await OrtamKurAsync(), Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Kredi}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(x, HttpMethod.Post, $"{Kredi}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Anahtar())).StatusCode);
    }
}
