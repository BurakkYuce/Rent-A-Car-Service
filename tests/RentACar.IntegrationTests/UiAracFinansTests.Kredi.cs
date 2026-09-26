using System.Net;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;

namespace RentACar.IntegrationTests;

public sealed partial class UiAracFinansTests
{
    /// <summary>ORACLE: 12.000 × 0,10 × 12/12 = 1.200 faiz → 13.200 / 12 = 1.100 aylık.</summary>
    private static object LoanBody(Guid vehicle, decimal amount = 12_000m) => new
    { bankaAdi = "Ziraat", vehicleId = vehicle, krediTutari = amount, faizOran = 0.10m, taksitSayisi = 12 };

    private async Task<(decimal Borc, decimal Alacak, int Satir)> LedgerAsync(Guid tenant, Guid expenseId)
        => await ReadDataAsync(tenant, async db =>
        {
            var l = await db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == expenseId).ToListAsync();
            return (l.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.AmountInBase),
                l.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.AmountInBase), l.Count);
        });

    private Task<int> LoanExpenseCountAsync(Guid tenant) => ReadDataAsync(tenant,
        db => db.Expenses.AsNoTracking().CountAsync(e => e.Tip == Domain.Enums.ExpenseType.Finansman));

    [Fact]
    public async Task Kredi_oracle_taksit_defter_dengeli_ve_idempotent()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicle = await VehicleAsync(o);
        var s = await LoginAsync(o, Kim.Admin);

        // Anahtarsız oluşturma 400; TRY'de kur ≠ 1 400.
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan, LoanBody(vehicle)), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan,
            new { bankaAdi = "Z", vehicleId = vehicle, krediTutari = 100m, faizOran = 0m, taksitSayisi = 1, kur = 5m }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "kur");

        var k1 = Key();
        var id = (await Json(await Gonder(s, HttpMethod.Post, Loan, LoanBody(vehicle), k1), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var repeat = await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan, LoanBody(vehicle), k1), HttpStatusCode.Conflict, "mukerrer");
        Assert.True(repeat.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(id, repeat.GetProperty("mevcut").GetProperty("id").GetGuid());
        var different = await ExpectProblem(await Gonder(s, HttpMethod.Post, Loan, LoanBody(vehicle, 999m), k1), HttpStatusCode.Conflict, "mukerrer");
        Assert.False(different.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1, (await Json(await s.C.GetAsync(Loan))).GetProperty("toplam").GetInt32());

        var d = await Json(await s.C.GetAsync($"{Loan}/{id}"));
        Assert.Equal(1200m, d.GetProperty("ozet").GetProperty("toplamFaiz").GetDecimal());
        Assert.Equal(13200m, d.GetProperty("ozet").GetProperty("toplamGeriOdeme").GetDecimal());
        Assert.Equal(1, d.GetProperty("sonrakiTaksit").GetProperty("sira").GetInt32());
        Assert.Equal(1100m, d.GetProperty("sonrakiTaksit").GetProperty("tutar").GetDecimal());

        // Taksit 1: gerçek gider + dengeli defter (Borç Gider[araç] 1.100 = Alacak Kasa 1.100).
        var t1 = Key();
        var payment = await Json(await Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, t1));
        Assert.Equal(1100m, payment.GetProperty("tutar").GetDecimal());
        var expenseId = payment.GetProperty("giderId").GetGuid();
        Assert.Equal((1100m, 1100m, 2), await LedgerAsync(o.TenantId, expenseId));
        var expenseVehicle = await ReadDataAsync(o.TenantId, db => db.AccountLedgerEntries.AsNoTracking()
            .Where(e => e.SourceId == expenseId && e.Direction == LedgerDirection.Debit).Select(e => e.AccountRef).SingleAsync());
        Assert.Equal(vehicle, expenseVehicle);

        // Kaybolan yanıttan sonraki tekrar: 409 mevcut (aynı içerik), İKİNCİ taksit ödenmez.
        var m = await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, t1),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(m.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(expenseId, m.GetProperty("mevcut").GetProperty("id").GetGuid());
        var mf = await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Banka" }, t1),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(mf.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        // Bayat ekran (yeni anahtar, eski sıra) → cakisma, yazım yok.
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Key()),
            HttpStatusCode.Conflict, "cakisma");
        await ExpectProblem(await Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 2, hesap = "Kasa" }),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(1, await LoanExpenseCountAsync(o.TenantId));

        // Eşzamanlı: iki sekme farklı anahtarla sıra 2 → TEK taksit; aynı anahtarla N gönderim → TEK taksit.
        var differentKey = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 2, hesap = "Kasa" }, Key())));
        Assert.Equal(1, differentKey.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(differentKey.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        var t3 = Key();
        var sameKey = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            Gonder(s, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 3, hesap = "Kasa" }, t3)));
        Assert.Equal(1, sameKey.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, await LoanExpenseCountAsync(o.TenantId));
        Assert.Equal(3, (await Json(await s.C.GetAsync($"{Loan}/{id}"))).GetProperty("odenenTaksit").GetInt32());

        // Özet: kalan = 13.200 − 3 × 1.100 = 9.900.
        Assert.Equal(9900m, (await Json(await s.C.GetAsync($"{Loan}/ozet"))).GetProperty("toplamKrediBorcu").GetDecimal());
    }

    [Fact]
    public async Task Kredi_kapsam_varlik_ve_iptal()
    {
        var o = await SetUpEnvironmentAsync();
        var vehicleA = await VehicleAsync(o, "SubeA");
        var a = await LoginAsync(o, Kim.OperatorA);
        var id = (await Json(await Gonder(a, HttpMethod.Post, Loan, LoanBody(vehicleA), Key()), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

        var b = await LoginAsync(o, Kim.OperatorB);
        await ExpectProblem(await b.C.GetAsync($"{Loan}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, (await Json(await b.C.GetAsync(Loan))).GetProperty("toplam").GetInt32());
        await ExpectProblem(await Gonder(b, HttpMethod.Post, Loan, LoanBody(vehicleA), Key()), HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(await Gonder(b, HttpMethod.Post, Loan,
            new { bankaAdi = "Z", krediTutari = 100m, faizOran = 0m, taksitSayisi = 1 }, Key()), HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        await ExpectProblem(await Gonder(a, HttpMethod.Post, Loan,
            new { bankaAdi = "Z", vehicleId = Guid.NewGuid(), krediTutari = 100m, faizOran = 0m, taksitSayisi = 1 }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "vehicleId");
        await ExpectProblem(await Gonder(a, HttpMethod.Post, Loan,
            new { bankaAdi = "Z", vehicleId = vehicleA, cariId = Guid.NewGuid(), krediTutari = 100m, faizOran = 0m, taksitSayisi = 1 }, Key()),
            HttpStatusCode.BadRequest, "dogrulama", "cariId");
        // Operatörde FinanceWrite ve OperationsDelete yok.
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(a, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Key())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Gonder(a, HttpMethod.Post, $"{Loan}/{id}/iptal")).StatusCode);

        var adm = await LoginAsync(o, Kim.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await Gonder(adm, HttpMethod.Post, $"{Loan}/{id}/iptal")).StatusCode);
        await ExpectProblem(await Gonder(adm, HttpMethod.Post, $"{Loan}/{id}/iptal"), HttpStatusCode.BadRequest, "dogrulama");
        await ExpectProblem(await Gonder(adm, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Key()),
            HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(0, await LoanExpenseCountAsync(o.TenantId));

        // Başka kiracı: 404 (RLS).
        var x = await LoginAsync(await SetUpEnvironmentAsync(), Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await x.C.GetAsync($"{Loan}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gonder(x, HttpMethod.Post, $"{Loan}/{id}/taksit-ode", new { sira = 1, hesap = "Kasa" }, Key())).StatusCode);
    }
}
