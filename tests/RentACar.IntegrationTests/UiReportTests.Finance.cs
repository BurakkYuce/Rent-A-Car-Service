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

        var all = (await GetJson(s, Rapor + "/gelir-gider")).GetProperty("ozet");
        Assert.Equal(6000m, Dec(all, "gelirToplam"));
        Assert.Equal(1000m, Dec(all, "giderToplam"));
        Assert.Equal(1200m, Dec(all, "kdvTahsil"));
        Assert.Equal(200m, Dec(all, "kdvIndirilecek"));
        Assert.Equal(5000m, Dec(all, "netKar"));
        var gelir = all.GetProperty("gelirKirilim").EnumerateArray().Single();
        Assert.Equal("AracSatis", gelir.GetProperty("sourceType").GetString());
        Assert.Equal(6000m, Dec(gelir, "tutar"));

        // Bugün (İstanbul günü) dönemi: aynı rakamlar; dünün penceresi boş.
        var today = await GetJson(s, $"{Rapor}/gelir-gider?bas={Today}&bit={Today}");
        Assert.Equal(6000m, Dec(today.GetProperty("ozet"), "gelirToplam"));
        Assert.Equal(Today, today.GetProperty("donem").GetProperty("bas").GetString());
        var dun = DateOnly.Parse(Today).AddDays(-1).ToString("yyyy-MM-dd");
        var yesterday = await GetJson(s, $"{Rapor}/gelir-gider?bas={dun}&bit={dun}");
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

        var r = await GetJson(s, Rapor + "/kasa-banka?hesap=Kasa&boyut=1&sayfa=2");
        var toplam = r.GetProperty("ozet").GetProperty("toplam");
        Assert.Equal(3000m, Dec(toplam, "kasaGiris"));
        Assert.Equal(1200m, Dec(toplam, "kasaCikis"));
        Assert.Equal(1800m, Dec(toplam, "kasaBakiye"));
        var sayfa = r.GetProperty("satirlar");
        Assert.Equal(2, sayfa.GetProperty("toplam").GetInt32());   // gider çıkışı + tahsilat
        var satir = sayfa.GetProperty("kayitlar").EnumerateArray().Single();
        Assert.Equal(1800m, Dec(satir, "yuruyenBakiye"));           // son satır = kasa bakiyesi
        Assert.Contains("Kasa", r.GetProperty("ozet").GetProperty("hesaplar").EnumerateArray()
            .Select(h => h.GetProperty("tur").GetString()));

        await ExpectProblem(s, Rapor + "/kasa-banka?hesap=Cuzdan", HttpStatusCode.BadRequest, "dogrulama", "hesap");
    }

    [Fact]
    public async Task Customer_balance_and_aging_mask_anonymous_customer()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accounting);

        var r = await GetJson(s, Rapor + "/cari-bakiye");
        var rows = r.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var a = rows.Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerA);
        var b = rows.Single(x => x.GetProperty("cariId").GetGuid() == e.CustomerB);
        Assert.Equal(3000m, Dec(a, "bakiye"));      // 6000 borç − 3000 tahsilat
        Assert.Equal(1200m, Dec(b, "bakiye"));
        Assert.Equal("Anonim müşteri", b.GetProperty("ad").GetString());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("telefon").ValueKind);
        Assert.StartsWith(MusteriA, a.GetProperty("ad").GetString());
        Assert.Equal(4200m, Dec(r.GetProperty("ozet"), "borcluToplam"));
        Assert.DoesNotContain(AnonimGercekAd, r.ToString());
        Assert.DoesNotContain("tcKimlik", r.ToString(), StringComparison.OrdinalIgnoreCase);

        // Süzgeç: yalnız 2000 üstü → A. Sıralama beyaz listesi dışı → 400 errors[sirala].
        var min = await GetJson(s, Rapor + "/cari-bakiye?min=2000");
        Assert.Equal(1, min.GetProperty("satirlar").GetProperty("toplam").GetInt32());
        Assert.Equal(4200m, Dec(min.GetProperty("ozet"), "borcluToplam")); // kart toplamı süzgeçten bağımsız
        await ExpectProblem(s, Rapor + "/cari-bakiye?sirala=gizli", HttpStatusCode.BadRequest, null, "sirala");

        var aging = await GetJson(s, Rapor + "/cari-bakiye/yaslandirma");
        Assert.Equal(7200m, Dec(aging.GetProperty("ozet").GetProperty("kovalar"), "b0_30")); // brüt borç 6000 + 1200
        Assert.Equal(0m, Dec(aging.GetProperty("ozet").GetProperty("kovalar"), "b90Plus"));
        Assert.DoesNotContain(AnonimGercekAd, aging.ToString());
    }

    [Fact]
    public async Task Profitability_totals_match_ledger()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var ozet = (await GetJson(s, $"{Rapor}/karlilik?bas={Today}&bit={Today}")).GetProperty("ozet");
        Assert.Equal(6000m, Dec(ozet, "toplamGelir"));
        Assert.Equal(1000m, Dec(ozet, "toplamGider"));
        Assert.Equal(5000m, Dec(ozet, "toplamNetKar"));

        var grup = (await GetJson(s, Rapor + "/karlilik/ozet?boyut=grup")).GetProperty("ozet");
        Assert.Equal(6000m, Dec(grup, "toplamGelir")); // araca atfedilen satış geliri C + D grubunda
        await ExpectProblem(s, Rapor + "/karlilik/ozet?boyut=renk", HttpStatusCode.BadRequest, null, "boyut");
    }

    [Fact]
    public async Task All_finance_reports_open_for_view_reports_and_closed_for_operator()
    {
        var e = await SetupAsync();
        var muhasebe = await LoginAsync(e, Who.Accounting);
        var op = await LoginAsync(e, Who.OperatorA);
        foreach (var ep in FinanceEndpoints)
        {
            await GetJson(muhasebe, Rapor + ep);
            await ExpectProblem(op, Rapor + ep, HttpStatusCode.Forbidden, "yetki_yok");
        }
        // Muhasebe KVKK: hiçbir finans raporunda anonim carinin gerçek adı yok.
        foreach (var ep in FinanceEndpoints)
            Assert.DoesNotContain(AnonimGercekAd, (await GetJson(muhasebe, Rapor + ep)).ToString());
    }

    [Fact]
    public async Task Other_tenant_sees_nothing()
    {
        await SetupAsync();
        var other = await SetupAsync(ledger: false);
        var s = await LoginAsync(other, Who.Admin);
        Assert.Equal(0m, Dec((await GetJson(s, Rapor + "/gelir-gider")).GetProperty("ozet"), "gelirToplam"));
        Assert.Equal(0, (await GetJson(s, Rapor + "/cari-bakiye")).GetProperty("satirlar").GetProperty("toplam").GetInt32());
        Assert.Equal(0, (await GetJson(s, Rapor + "/kasa-banka")).GetProperty("satirlar").GetProperty("toplam").GetInt32());
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
        await ExpectProblem(s, Rapor + url, HttpStatusCode.BadRequest, null, field);
    }

    [Fact]
    public async Task Page_size_is_capped_at_200()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        var r = await GetJson(s, Rapor + "/cari-bakiye?boyut=100000");
        Assert.Equal(200, r.GetProperty("satirlar").GetProperty("boyut").GetInt32());
    }
}
