using System.Net;
using System.Text.Json;

namespace RentACar.IntegrationTests;

public sealed partial class UiReportTests
{
    /// <summary>Firma geneli (ViewReports) rapor uçları — izin testi bu listeyi gezer.</summary>
    private static readonly string[] FinanceEndpoints =
    [
        "/gelir-gider", "/kasa-banka", "/finans-analiz", "/virman-gecmisi", "/kdv-listesi", "/kdv-listesi/genis",
        "/cari-bakiye", "/cari-bakiye/yaslandirma", "/extre-ozeti", "/tahsilat-fatura", "/tahsilat-fatura/mutabakat",
        "/fatura-donem", "/fatura-donem/kira-durum", "/karlilik", "/karlilik/ozet", "/ek-hizmet",
        "/ek-hizmet/arac-pivot", "/ek-hizmet/detay", "/gunluk",
    ];

    [Fact]
    public async Task Income_expense_matches_hand_built_ledger_and_period_filter()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accounting);

        var all = (await GetJson(s, Report + "/gelir-gider")).GetProperty("ozet");
        Assert.Equal(6000m, Dec(all, "gelirToplam"));
        Assert.Equal(1000m, Dec(all, "giderToplam"));
        Assert.Equal(1200m, Dec(all, "kdvTahsil"));
        Assert.Equal(200m, Dec(all, "kdvIndirilecek"));
        Assert.Equal(5000m, Dec(all, "netKar"));
        var revenue = all.GetProperty("gelirKirilim").EnumerateArray().Single();
        Assert.Equal("AracSatis", revenue.GetProperty("sourceType").GetString());
        Assert.Equal(6000m, Dec(revenue, "tutar"));

        // Bugün (İstanbul günü) dönemi: aynı rakamlar; dünün penceresi boş.
        var today = await GetJson(s, $"{Report}/gelir-gider?bas={Today}&bit={Today}");
        Assert.Equal(6000m, Dec(today.GetProperty("ozet"), "gelirToplam"));
        Assert.Equal(Today, today.GetProperty("donem").GetProperty("bas").GetString());
        var dun = DateOnly.Parse(Today).AddDays(-1).ToString("yyyy-MM-dd");
        var yesterday = await GetJson(s, $"{Report}/gelir-gider?bas={dun}&bit={dun}");
        Assert.Equal(0m, Dec(yesterday.GetProperty("ozet"), "gelirToplam"));

        // Export bağlantısı mevcut sunucu ucuna AYNI süzgeçle gider ve dosya döner.
        var excel = today.GetProperty("export").GetProperty("excel").GetString()!;
        Assert.Equal($"/raporlar/export/gelir-gider?format=excel&from={Today}&to={Today}", excel);
        var file = await s.C.GetAsync(excel);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Contains("spreadsheetml", file.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Cash_ledger_balance_and_paging()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);

        var r = await GetJson(s, Report + "/kasa-banka?hesap=Kasa&boyut=1&sayfa=2");
        var total = r.GetProperty("ozet").GetProperty("toplam");
        Assert.Equal(3000m, Dec(total, "kasaGiris"));
        Assert.Equal(1200m, Dec(total, "kasaCikis"));
        Assert.Equal(1800m, Dec(total, "kasaBakiye"));
        var page = r.GetProperty("satirlar");
        Assert.Equal(2, page.GetProperty("toplam").GetInt32());   // gider çıkışı + tahsilat
        var row = page.GetProperty("kayitlar").EnumerateArray().Single();
        Assert.Equal(1800m, Dec(row, "yuruyenBakiye"));           // son satır = kasa bakiyesi
        Assert.Contains("Kasa", r.GetProperty("ozet").GetProperty("hesaplar").EnumerateArray()
            .Select(h => h.GetProperty("tur").GetString()));

        await ExpectProblem(s, Report + "/kasa-banka?hesap=Cuzdan", HttpStatusCode.BadRequest, "dogrulama", "hesap");
    }

    [Fact]
    public async Task Customer_balance_and_aging_mask_anonymous_customer()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accounting);

        var r = await GetJson(s, Report + "/cari-bakiye");
        var rows = r.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var a = rows.Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerA);
        var b = rows.Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerB);
        Assert.Equal(3000m, Dec(a, "bakiye"));      // 6000 borç − 3000 tahsilat
        Assert.Equal(1200m, Dec(b, "bakiye"));
        Assert.Equal("Anonim müşteri", b.GetProperty("ad").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("telefon").ValueKind);
        Assert.StartsWith(CustomerA, a.GetProperty("ad").GetString());
        Assert.Equal(4200m, Dec(r.GetProperty("ozet"), "borcluToplam"));
        Assert.DoesNotContain(AnonymousRealName, r.ToString());
        Assert.DoesNotContain("tcKimlik", r.ToString(), StringComparison.OrdinalIgnoreCase);

        // Süzgeç: yalnız 2000 üstü → A. Sıralama beyaz listesi dışı → 400 errors[sirala].
        var min = await GetJson(s, Report + "/cari-bakiye?min=2000");
        Assert.Equal(1, min.GetProperty("satirlar").GetProperty("toplam").GetInt32());
        Assert.Equal(4200m, Dec(min.GetProperty("ozet"), "borcluToplam")); // kart toplamı süzgeçten bağımsız
        await ExpectProblem(s, Report + "/cari-bakiye?sirala=gizli", HttpStatusCode.BadRequest, null, "sirala");

        var aging = await GetJson(s, Report + "/cari-bakiye/yaslandirma");
        Assert.Equal(7200m, Dec(aging.GetProperty("ozet").GetProperty("kovalar"), "b0_30")); // brüt borç 6000 + 1200
        Assert.Equal(0m, Dec(aging.GetProperty("ozet").GetProperty("kovalar"), "b90Plus"));
        Assert.DoesNotContain(AnonymousRealName, aging.ToString());
    }

    [Fact]
    public async Task Profitability_totals_match_ledger()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var summary = (await GetJson(s, $"{Report}/karlilik?bas={Today}&bit={Today}")).GetProperty("ozet");
        Assert.Equal(6000m, Dec(summary, "toplamGelir"));
        Assert.Equal(1000m, Dec(summary, "toplamGider"));
        Assert.Equal(5000m, Dec(summary, "toplamNetKar"));

        var group = (await GetJson(s, Report + "/karlilik/ozet?boyut=grup")).GetProperty("ozet");
        Assert.Equal(6000m, Dec(group, "toplamGelir")); // araca atfedilen satış geliri C + D grubunda
        await ExpectProblem(s, Report + "/karlilik/ozet?boyut=renk", HttpStatusCode.BadRequest, null, "boyut");
    }

    [Fact]
    public async Task All_finance_reports_open_for_view_reports_and_closed_for_operator()
    {
        var e = await SetupAsync();
        var accounting = await LoginAsync(e, Who.Accounting);
        var op = await LoginAsync(e, Who.OperatorA);
        foreach (var ep in FinanceEndpoints)
        {
            await GetJson(accounting, Report + ep);
            await ExpectProblem(op, Report + ep, HttpStatusCode.Forbidden, "yetki_yok");
        }
        // Muhasebe KVKK: hiçbir finans raporunda anonim carinin gerçek adı yok.
        foreach (var ep in FinanceEndpoints)
            Assert.DoesNotContain(AnonymousRealName, (await GetJson(accounting, Report + ep)).ToString());
    }

    [Fact]
    public async Task Other_tenant_sees_nothing()
    {
        await SetupAsync();
        var other = await SetupAsync(ledger: false);
        var s = await LoginAsync(other, Who.Admin);
        Assert.Equal(0m, Dec((await GetJson(s, Report + "/gelir-gider")).GetProperty("ozet"), "gelirToplam"));
        Assert.Equal(0, (await GetJson(s, Report + "/cari-bakiye")).GetProperty("satirlar").GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await GetJson(s, Report + "/kasa-banka")).GetProperty("satirlar").GetProperty("toplam").GetInt32());
    }

    [Theory]
    [InlineData("/gelir-gider?bas=1899-12-31", "bas")]
    [InlineData("/gelir-gider?bit=2101-01-01", "bit")]
    [InlineData("/gelir-gider?bas=2026-05-02&bit=2026-05-01", "bit")]
    [InlineData("/gunluk?gun=1800-01-01", "gun")]
    [InlineData("/cari-bakiye/yaslandirma?tarih=2200-01-01", "tarih")]
    [InlineData("/tahsilat-fatura/mutabakat?durum=Uydurma", "durum")]
    [InlineData("/fatura-donem/kira-durum?faturaDurum=belki", "faturaDurum")]
    public async Task Invalid_parameters_are_400_with_field(string url, string field)
    {
        var e = await SetupAsync(ledger: false);
        var s = await LoginAsync(e, Who.Admin);
        await ExpectProblem(s, Report + url, HttpStatusCode.BadRequest, null, field);
    }

    [Fact]
    public async Task Page_size_is_capped_at_200()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var r = await GetJson(s, Report + "/cari-bakiye?boyut=100000");
        Assert.Equal(200, r.GetProperty("satirlar").GetProperty("boyut").GetInt32());
    }
}
