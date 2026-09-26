using System.Net;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// #301 eksik uçlar: servis listesinde satır KDV / genel toplam / fatura no + <c>/servisler/sayaclar</c> (durum sekmeleri)
/// ve <c>/regulasyon/zeyiller</c> (tüm poliçelerin zeyilleri). Beklenen değerler elle: 1.000 net × %20 = 200 KDV,
/// genel 1.200; KDV'siz 100 kalem → KDV 0, genel 100. Başka şube satır olarak görünmez; başka kiracı boş.
/// </summary>
public sealed partial class UiServiceInsuranceTests
{
    private async Task<Guid> ServiceAsync(Session s, Guid car, string tip, object? item)
    {
        var body = new { vehicleId = car, tip, girisKm = 1000, hasarSorumlu = "Sirket", kalem = item };
        return (await Json(await Send(s, HttpMethod.Post, Svc, body, Key()), HttpStatusCode.Created))
            .GetProperty("kayit").GetProperty("id").GetGuid();
    }

    private static int CountOf(JsonElement counts, string status)
        => counts.GetProperty("durumlar").EnumerateArray().Single(x => x.GetProperty("durum").GetString() == status)
            .GetProperty("adet").GetInt32();

    [Fact]
    public async Task Service_list_has_row_totals_invoice_no_and_status_counts_with_scope()
    {
        var e = await SetupAsync();
        var carA = await VehicleAsync(e, "SubeA");
        var carB = await VehicleAsync(e, "SubeB");
        var s = await LoginAsync(e, Who.Admin);
        var withVat = await ServiceAsync(s, carA, "Periyodik", new { aciklama = "Bakım", tutar = 1000m, kdvOran = 0.20m });
        var started = await ServiceAsync(s, carA, "Periyodik", null);
        await Json(await Send(s, HttpMethod.Post, $"{Svc}/{started}/baslat"));
        var other = await ServiceAsync(s, carB, "Hasar", new { aciklama = "Kaporta", tutar = 100m });
        var v = (await Json(await s.C.GetAsync($"{Svc}/{withVat}"))).GetProperty("surum").GetString();
        await Json(await Send(s, HttpMethod.Put, $"{Svc}/{withVat}/bilgi", new { faturaNo = "F-77", surum = v }));

        var rows = (await Json(await s.C.GetAsync(Svc))).GetProperty("kayitlar").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetGuid());
        Assert.Equal(200m, rows[withVat].GetProperty("kdvToplam").GetDecimal());
        Assert.Equal(1200m, rows[withVat].GetProperty("genelToplam").GetDecimal());
        Assert.Equal("F-77", rows[withVat].GetProperty("faturaNo").GetString());
        Assert.Equal(0m, rows[other].GetProperty("kdvToplam").GetDecimal());
        Assert.Equal(100m, rows[other].GetProperty("genelToplam").GetDecimal());
        Assert.Equal(0m, rows[started].GetProperty("genelToplam").GetDecimal());
        Assert.Equal(JsonValueKind.Null, rows[started].GetProperty("faturaNo").ValueKind);

        // Sayaçlar: durum süzgecinden bağımsız; diğer süzgeçlerle ve kapsamla.
        var all = await Json(await s.C.GetAsync($"{Svc}/sayaclar?durum=Iptal"));
        Assert.Equal(3, all.GetProperty("tumu").GetInt32());
        Assert.Equal(2, CountOf(all, "Acik"));
        Assert.Equal(1, CountOf(all, "Serviste"));
        Assert.Equal(0, CountOf(all, "Tamamlandi"));
        Assert.Equal(1, (await Json(await s.C.GetAsync($"{Svc}/sayaclar?tip=Hasar"))).GetProperty("tumu").GetInt32());
        var opA = await Json(await (await LoginAsync(e, Who.OperatorA)).C.GetAsync($"{Svc}/sayaclar"));
        Assert.Equal(2, opA.GetProperty("tumu").GetInt32()); // SubeB kaydı sayılmaz
        Assert.Equal(1, CountOf(opA, "Acik"));
        Assert.Equal(3, (await Json(await (await LoginAsync(e, Who.Accounting)).C.GetAsync($"{Svc}/sayaclar")))
            .GetProperty("tumu").GetInt32());
        await Problem(await s.C.GetAsync($"{Svc}/sayaclar?tip=Yok"), HttpStatusCode.BadRequest, "dogrulama", "tip");
        var foreign = await Json(await (await LoginAsync(await SetupAsync(), Who.Admin)).C.GetAsync($"{Svc}/sayaclar"));
        Assert.Equal(0, foreign.GetProperty("tumu").GetInt32());
    }

    [Fact]
    public async Task Endorsement_list_spans_all_policies_with_branch_scope()
    {
        var e = await SetupAsync();
        var carA = await VehicleAsync(e, "SubeA");
        var carB = await VehicleAsync(e, "SubeB");
        var s = await LoginAsync(e, Who.Admin);
        async Task<Guid> PolicyAsync(Guid car, string no) => (await Json(await Send(s, HttpMethod.Post, $"{Reg}/sigortalar", new
        {
            vehicleId = car, tip = "Kasko", baslangic = TestZaman.DaysLater(-1), bitis = TestZaman.DaysLater(360), prim = 1000m,
            policeNo = no, firma = "Sigorta A.Ş.",
        }), HttpStatusCode.Created)).GetProperty("police").GetProperty("id").GetGuid();
        async Task<Guid> EndorseAsync(Guid policy, string no, string type, int day) => (await Json(await Send(s, HttpMethod.Post,
            $"{Reg}/sigortalar/{policy}/zeyiller", new { zeyilNo = no, tarih = TestZaman.DaysLater(day), brut = 120m, net = 100m, tipi = type }),
            HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var pa1 = await PolicyAsync(carA, "P-A1");
        var pa2 = await PolicyAsync(carA, "P-A2");
        var pb = await PolicyAsync(carB, "P-B");
        var z1 = await EndorseAsync(pa1, "Z-1", "Zam", -10);
        var z2 = await EndorseAsync(pa2, "Z-2", "Tenzil", -3);
        var z3 = await EndorseAsync(pb, "Z-3", "Zam", -3);

        var page = await Json(await s.C.GetAsync($"{Reg}/zeyiller?sirala=zeyilNo"));
        var rows = page.GetProperty("kayitlar").EnumerateArray().ToList();
        Assert.Equal(new[] { z1, z2, z3 }, rows.Select(r => r.GetProperty("id").GetGuid()));
        Assert.Equal("P-A2", rows[1].GetProperty("policeNo").GetString());
        Assert.Equal(pa2, rows[1].GetProperty("policyId").GetGuid());
        Assert.Equal(120m, rows[1].GetProperty("brut").GetDecimal());

        var zam = await Json(await s.C.GetAsync($"{Reg}/zeyiller?tipi=zam"));
        Assert.Equal(2, zam.GetProperty("kayitlar").GetArrayLength());
        var start = DateOnly.FromDateTime(TestZaman.DaysLater(-5).Date);
        var recent = await Json(await s.C.GetAsync($"{Reg}/zeyiller?bas={start:yyyy-MM-dd}"));
        Assert.Equal(2, recent.GetProperty("kayitlar").GetArrayLength());

        var opA = (await Json(await (await LoginAsync(e, Who.OperatorA)).C.GetAsync($"{Reg}/zeyiller")))
            .GetProperty("kayitlar").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(new[] { z1, z2 }.OrderBy(x => x), opA.OrderBy(x => x)); // SubeB poliçesinin zeyili yok
        await Problem(await s.C.GetAsync($"{Reg}/zeyiller?plaka={new string('x', 33)}"), HttpStatusCode.BadRequest, "dogrulama", "plaka");
        var foreign = await Json(await (await LoginAsync(await SetupAsync(), Who.Admin)).C.GetAsync($"{Reg}/zeyiller"));
        Assert.Equal(0, foreign.GetProperty("kayitlar").GetArrayLength());
    }
}
