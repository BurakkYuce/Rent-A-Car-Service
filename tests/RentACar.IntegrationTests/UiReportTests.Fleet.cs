using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiReportTests
{
    /// <summary>Firma geneli filo raporları (ViewReports).</summary>
    private static readonly string[] FleetViewEndpoints =
    [
        "/filo-analiz", "/filo", "/doluluk", "/arac-gunluk-durum", "/servis-ozet", "/rezervasyon-kaynak",
        "/otomatik-servisler",
    ];

    /// <summary>Dört rolün açtığı operasyon raporları (OperationsWrite VEYA ViewReports).</summary>
    private static readonly string[] OperationEndpoints =
    [
        "/arac-durum-takip", "/arac-durum-takip?gorunum=arac", "/km-detay", "/periyodik-servis", "/sigorta-muayene",
        "/personel-calisma",
    ];

    [Fact]
    public async Task Scorecard_shows_vehicle_pnl_and_hides_other_tenant_vehicle()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);

        var r = await GetJson(s, $"{Report}/arac-karne/{e.VehicleA}");
        var scorecard = r.GetProperty("ozet");
        Assert.Equal("Satildi", scorecard.GetProperty("baslik").GetProperty("durum").GetString());
        Assert.Equal(5000m, Dec(scorecard, "toplamGelir"));   // A aracının satış geliri (net)
        Assert.Equal(0m, Dec(scorecard, "toplamGider"));      // genel gider araca atfedilmez
        Assert.StartsWith($"/raporlar/export/arac-karne?format=excel&vehicleId={e.VehicleA}",
            r.GetProperty("export").GetProperty("excel").GetString());

        var other = await SetupAsync(ledger: false);
        await ExpectProblem(s, $"{Report}/arac-karne/{other.VehicleA}", HttpStatusCode.NotFound, null);
        await ExpectProblem(s, $"{Report}/arac-karne/{Guid.NewGuid()}", HttpStatusCode.NotFound, null);
    }

    [Fact]
    public async Task Fleet_analysis_and_status_match_scenario()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Accounting);

        var fa = await GetJson(s, Report + "/filo-analiz?sirala=-gelir");
        var rows = fa.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(e.VehicleA, rows[0].GetProperty("vehicleId").GetGuid()); // 5000 > 1000
        Assert.Equal(6000m, rows.Sum(x => Dec(x, "gelir")));
        Assert.Equal(1000m, Dec(fa.GetProperty("ozet"), "atanmamisGider"));   // genel gider araca atfedilmez
        await ExpectProblem(s, Report + "/filo-analiz?siralama=renk", HttpStatusCode.BadRequest, null, "siralama");

        var fleet = (await GetJson(s, Report + "/filo")).GetProperty("ozet").GetProperty("durum");
        Assert.Equal(2, fleet.GetProperty("toplam").GetInt32());
        Assert.Equal(2, fleet.GetProperty("satildi").GetInt32());

        var dol = await GetJson(s, $"{Report}/doluluk?bas={Today}&bit={Today}&boyut=Sube");
        Assert.Equal(2, dol.GetProperty("ozet").GetProperty("ozet").GetProperty("aracSayisi").GetInt32());
        Assert.Equal(0, dol.GetProperty("ozet").GetProperty("ozet").GetProperty("kiraGun").GetInt32());
        Assert.Equal("Sube", dol.GetProperty("ozet").GetProperty("gunluk").GetProperty("boyut").GetString());
        await ExpectProblem(s, Report + "/doluluk?boyut=Renk", HttpStatusCode.BadRequest, null, "boyut");
        var tooLong = DateOnly.Parse(Today).AddDays(-400).ToString("yyyy-MM-dd");
        await ExpectProblem(s, $"{Report}/doluluk?bas={tooLong}&bit={Today}", HttpStatusCode.BadRequest, null, "bit");
    }

    [Fact]
    public async Task Operation_reports_force_operator_branch()
    {
        var e = await SetupAsync(ledger: false);
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);

        var all = await GetJson(admin, Report + "/arac-durum-takip?gorunum=arac");
        Assert.Equal(2, all.GetProperty("satirlar").GetProperty("toplam").GetInt32());
        var branchA = await GetJson(opA, Report + "/arac-durum-takip?gorunum=arac");
        var tek = branchA.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray().Single();
        Assert.Equal(e.VehicleA, tek.GetProperty("vehicleId").GetGuid());
        Assert.Equal(JsonValueKind.Null, branchA.GetProperty("export").ValueKind); // export firma geneli → kapsamlıya yok
        await ExpectProblem(opA, Report + "/arac-durum-takip?sube=SubeB", HttpStatusCode.Forbidden, "yetki_yok");
        await ExpectProblem(opA, Report + "/periyodik-servis?sube=SubeB", HttpStatusCode.Forbidden, "yetki_yok");

        var day = await GetJson(opA, $"{Report}/arac-durum-takip?bas={Today}&bit={Today}");
        var row = day.GetProperty("ozet").GetProperty("gunler").EnumerateArray().Single();
        Assert.Equal(1, row.GetProperty("toplamArac").GetInt32()); // yalnız SubeA aracı

        var ps = await GetJson(opA, Report + "/periyodik-servis");
        Assert.All(ps.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray(),
            x => Assert.Equal(e.VehicleA, x.GetProperty("vehicleId").GetGuid()));
        var sm = await GetJson(opA, Report + "/sigorta-muayene");
        Assert.Equal(e.VehicleA, sm.GetProperty("satirlar").GetProperty("kayitlar").EnumerateArray().Single()
            .GetProperty("vehicleId").GetGuid());
        Assert.Equal(2, (await GetJson(admin, Report + "/sigorta-muayene")).GetProperty("ozet").GetProperty("adet").GetInt32());

        // Pivot firma geneli sayımdır → kapsamlı kullanıcıya 403.
        await GetJson(admin, Report + "/karsilastirmali-analiz");
        await ExpectProblem(opA, Report + "/karsilastirmali-analiz", HttpStatusCode.Forbidden, "yetki_yok");

        var oldDay = DateOnly.Parse(Today).AddDays(-400).ToString("yyyy-MM-dd");
        await ExpectProblem(admin, $"{Report}/arac-durum-takip?bas={oldDay}&bit={Today}", HttpStatusCode.BadRequest, null, "bit");
        await ExpectProblem(admin, Report + "/arac-durum-takip?gorunum=hafta", HttpStatusCode.BadRequest, null, "gorunum");
        await ExpectProblem(admin, Report + "/sigorta-muayene?tur=Yok", HttpStatusCode.BadRequest, null, "tur");
        await ExpectProblem(admin, Report + "/periyodik-servis?esik=-1", HttpStatusCode.BadRequest, null, "esik");
    }

    [Fact]
    public async Task Fleet_permissions_follow_blazor_pages()
    {
        var e = await SetupAsync();
        var accounting = await LoginAsync(e, Who.Accounting);
        var opB = await LoginAsync(e, Who.OperatorB);
        foreach (var ep in FleetViewEndpoints.Append($"/arac-karne/{e.VehicleA}"))
        {
            await GetJson(accounting, Report + ep);
            await ExpectProblem(opB, Report + ep, HttpStatusCode.Forbidden, "yetki_yok");
        }
        foreach (var ep in OperationEndpoints)
        {
            await GetJson(accounting, Report + ep);
            await GetJson(opB, Report + ep);
        }
        var shifts = await GetJson(opB, Report + "/personel-calisma");
        Assert.Equal(7, shifts.GetProperty("gunler").GetArrayLength()); // varsayılan pencere bir hafta
    }

    /// <summary>
    /// PARİTE: aynı girdiyle servis (Blazor sayfalarının çağırdığı) ve uç aynı sayıları verir. Dönem gün ortası
    /// kayıtlarla kurulduğu için İstanbul/UTC gün sınırı farkı sonucu değiştirmez.
    /// </summary>
    [Fact]
    public async Task Endpoints_match_service_output_for_same_input()
    {
        var e = await SetupAsync();
        var s = await LoginAsync(e, Who.Admin);
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(e.TenantId);
        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();

        var tf = await svc.GetCollectionInvoiceAsync();
        var tfApi = (await GetJson(s, Report + "/tahsilat-fatura")).GetProperty("ozet");
        Assert.Equal(tf.TahsilatToplam, Dec(tfApi, "tahsilatToplam"));
        Assert.Equal(3000m, Dec(tfApi, "tahsilatToplam")); // elle: tek tahsilat 3000
        Assert.Equal(tf.FaturaToplam, Dec(tfApi, "faturaToplam"));

        var vat = await svc.GetVatListAsync();
        Assert.Equal(vat.ToplamKdv, Dec((await GetJson(s, Report + "/kdv-listesi")).GetProperty("ozet"), "toplamKdv"));
        var extra = await svc.GetAddOnReportAsync();
        Assert.Equal(extra.ToplamBrut, Dec((await GetJson(s, Report + "/ek-hizmet")).GetProperty("ozet"), "toplamBrut"));
        var hs = await svc.GetCashBankSummaryAsync();
        Assert.Equal(hs.KasaBakiye, Dec((await GetJson(s, Report + "/kasa-banka")).GetProperty("ozet").GetProperty("toplam"), "kasaBakiye"));
        var ka = await svc.GetComparativeAnalysisAsync();
        Assert.Equal(ka.GenelToplam, Dec((await GetJson(s, Report + "/karsilastirmali-analiz")).GetProperty("ozet"), "genelToplam"));
        var fo = await svc.GetFleetUtilizationAsync();
        Assert.Equal(fo.Satildi, (await GetJson(s, Report + "/filo")).GetProperty("ozet").GetProperty("durum").GetProperty("satildi").GetInt32());
        var ps = await svc.GetPeriodicServiceAsync();
        Assert.Equal(ps.Count, (await GetJson(s, Report + "/periyodik-servis")).GetProperty("satirlar").GetProperty("toplam").GetInt32());
    }
}
