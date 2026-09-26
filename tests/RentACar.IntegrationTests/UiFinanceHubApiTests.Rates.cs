using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

public sealed partial class UiFinanceHubApiTests
{
    // ------------------------------------------------------------ kurlar (sabit kur, iyimser eşzamanlılık)

    [Fact]
    public async Task Fixed_rate_create_update_with_version_and_delete()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var id = await IdOf(await PostAsync(s, "/kurlar/sabit", new { kod = "usd", kur = 30.5m, aktif = true }));
        await Problem(await PostAsync(s, "/kurlar/sabit", new { kod = "USD", kur = 31m }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await Problem(await PostAsync(s, "/kurlar/sabit", new { kod = "TRY", kur = 1m }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        await Problem(await PostAsync(s, "/kurlar/sabit", new { kod = "EUR", kur = 1.1234567m }), HttpStatusCode.BadRequest, "dogrulama", "kur");

        var screen = await Ok(await GetAsync(s, "/kurlar"));
        var row = screen.GetProperty("sabitler").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("USD", row.GetProperty("kod").GetString());
        Assert.Equal(30.5m, row.GetProperty("kur").GetDecimal());
        var version = row.GetProperty("surum").GetString()!;

        var update = new { kur = 32m, basTar = (string?)null, bitTar = (string?)null, aktif = true, surum = version };
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(s, HttpMethod.Put, $"/kurlar/sabit/{id}", update, null)).StatusCode);
        // Aynı (artık bayat) sürümle ikinci PUT → 409 cakisma; kayıt ezilmez.
        await Problem(await SendAsync(s, HttpMethod.Put, $"/kurlar/sabit/{id}", update with { kur = 99m }, null), HttpStatusCode.Conflict, "cakisma");
        await Problem(await SendAsync(s, HttpMethod.Put, $"/kurlar/sabit/{id}", update with { surum = (string?)null }, null),
            HttpStatusCode.BadRequest, "dogrulama", "surum");
        var exchangeRate = await DbAsync(e, db => db.SabitKurlar.AsNoTracking().Where(x => x.Id == id).Select(x => x.Kur).SingleAsync());
        Assert.Equal(32m, exchangeRate);

        // Sabit kur para çözümüne girer: kursuz USD virmanı 32'den yazılır.
        var transfer = await IdOf(await PostAsync(s, "/kasa/virman", new { kaynak = "Kasa", hedef = "Banka", tutar = 10m, doviz = "USD" }, NewKey()));
        Assert.All(await LedgerAsync(e, transfer), x => Assert.Equal(32m, x.Amount.Rate));

        var conv = await Ok(await GetAsync(s, "/kurlar/cevir?tutar=10&kaynak=USD&hedef=TRY"));
        Assert.Equal(320m, conv.GetProperty("sonuc").GetDecimal());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(s, HttpMethod.Delete, $"/kurlar/sabit/{id}", null, null)).StatusCode);
        await Problem(await SendAsync(s, HttpMethod.Delete, $"/kurlar/sabit/{id}", null, null), HttpStatusCode.NotFound, null);
    }

    // ------------------------------------------------------------ toplu gider (E22)

    [Fact]
    public async Task Bulk_expense_posts_net_plus_vat_and_rejects_foreign_vehicle()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accountant);
        var vehicle = await ReadAsync(e, sp => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 TG " + Random.Shared.Next(1000, 9999) }));
        var key = NewKey();
        var body = new
        {
            tip = "Arac", odemeYontemi = "Nakit", kdvOrani = 0.20m,
            satirlar = new object[] { new { netTutar = 100m, aciklama = "Yıkama", aracId = vehicle }, new { netTutar = 200m, aracId = vehicle } },
        };
        var r = await Ok(await PostAsync(s, "/toplu-gider", body, key));
        Assert.Equal(2, r.GetProperty("adet").GetInt32());
        Assert.Equal(300m, r.GetProperty("toplam").GetDecimal());
        // Oracle: (100 + 200) × 1,20 = 360 kasadan çıkar; gider 300, KDV 60.
        var cash = await DbAsync(e, db => db.AccountLedgerEntries.AsNoTracking()
            .Where(x => x.AccountType == LedgerAccountType.Kasa && x.Direction == LedgerDirection.Credit).SumAsync(x => x.Amount.Amount));
        Assert.Equal(360m, cash);

        await Problem(await PostAsync(s, "/toplu-gider", body, key), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/toplu-gider", body with { satirlar = new object[] { new { netTutar = 10m, aracId = Guid.NewGuid() } } }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "satirlar[0].aracId");
        await Problem(await PostAsync(s, "/toplu-gider", body with { kdvOrani = 20m }, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kdvOrani");
        await Problem(await PostAsync(s, "/toplu-gider", body with { satirlar = new object[] { new { netTutar = 10.001m } } }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "satirlar[0].netTutar");
        Assert.Equal(2, await DbAsync(e, db => db.Expenses.CountAsync()));
        await AllLedgerBalancedAsync(e);
    }
}
