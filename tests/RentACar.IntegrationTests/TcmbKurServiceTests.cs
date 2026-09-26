using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Kur;

namespace RentACar.IntegrationTests;

/// <summary>
/// Denetim O8 — TcmbKurService: sahte HttpMessageHandler (TwilioWhatsAppServiceTests deseni) ile
/// SABİT TCMB XML'i → gerçek KurKayitlari yazımı (BAĞIMSIZ ORACLE: USD ForexSatis 34.2567 elle,
/// parser fixture'ından). Upsert (çift kayıt yok), 30dk throttle (HTTP'ye gitmez), bozuk XML/ağ
/// hatası → 0 (patlamaz). KurKayitlari PAYLAŞIMLI tablo → çakışmayan 2099 tarihleri + test başında
/// idempotent temizlik (KurTests.SeedKurAsync deseni).
/// </summary>
[Collection("postgres")]
public sealed class TcmbKurServiceTests(PostgresFixture fx)
{
    /// <summary>Sabit gövde döndüren, çağrı sayan handler (throttle kanıtı için sayaç).</summary>
    private sealed class SayanHandler : HttpMessageHandler
    {
        public string Body = "";
        public bool AgHatasi;
        private int _call;
        public int Cagri => Volatile.Read(ref _call);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref _call);
            if (AgHatasi) throw new HttpRequestException("ağ yok (test)");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }
    }

    private sealed class FakeFactory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h);
    }

    /// <summary>Parser fixture'ıyla aynı yapı; tarih/USD-satış parametrik (2099 → canlıyla çakışmaz).</summary>
    private static string Xml(string date, string usdSelling) => $"""
        <Tarih_Date Tarih="{date}" Date="{date}" Bulten_No="2099/1">
          <Currency CrossOrder="0" Kod="USD" CurrencyCode="USD">
            <Unit>1</Unit><Isim>ABD DOLARI</Isim>
            <ForexBuying>34.1234</ForexBuying><ForexSelling>{usdSelling}</ForexSelling>
            <BanknoteBuying>34.1000</BanknoteBuying><BanknoteSelling>34.2800</BanknoteSelling>
          </Currency>
          <Currency CrossOrder="18" Kod="JPY" CurrencyCode="JPY">
            <Unit>100</Unit><Isim>JAPON YENI</Isim>
            <ForexBuying>22.5000</ForexBuying><ForexSelling>22.7000</ForexSelling>
          </Currency>
          <Currency CrossOrder="99" Kod="XDR" CurrencyCode="XDR">
            <Unit>1</Unit><Isim>SDR</Isim>
            <ForexBuying></ForexBuying><ForexSelling></ForexSelling>
          </Currency>
        </Tarih_Date>
        """;

    private TcmbExchangeRateService Service(SayanHandler handler)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = fx.AppConnectionString
        }).Build();
        return new TcmbExchangeRateService(new FakeFactory(handler), config, NullLogger<TcmbExchangeRateService>.Instance);
    }

    /// <summary>KurKayitlari paylaşımlı → verilen tarihin satırlarını idempotent temizle.</summary>
    private async Task ClearAsync(DateTimeOffset date)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.KurKayitlari.RemoveRange(await db.KurKayitlari.Where(x => x.Tarih == date).ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task<List<RentACar.Domain.Entities.KurKaydi>> ReadAsync(DateTimeOffset date, string code)
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await db.KurKayitlari.AsNoTracking().Where(x => x.Tarih == date && x.Kod == code).ToListAsync();
    }

    // ---- (a) Refresh → yazar; USD ForexSatis 34.2567 (elle oracle) ----
    [Fact]
    public async Task Refresh_yazar_ve_USD_satis_dogru()
    {
        var date = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await ClearAsync(date);

        var h = new SayanHandler { Body = Xml("01.01.2099", "34.2567") };
        var n = await Service(h).RefreshAsync();

        Assert.Equal(3, n); // fixture'da 3 Currency: USD, JPY, XDR (elle sayım)
        var usd = Assert.Single(await ReadAsync(date, "USD"));
        Assert.Equal(34.2567m, usd.ForexSatis); // ORACLE: fixture'daki el değeri
        Assert.Equal(34.1234m, usd.ForexAlis);
        Assert.Equal(34.2800m, usd.EfektifSatis);
        var jpy = Assert.Single(await ReadAsync(date, "JPY"));
        Assert.Equal(100, jpy.Birim);
    }

    // ---- (b) İkinci Refresh (zorla, farklı değer) → satır GÜNCELLENİR, çift kayıt YOK ----
    [Fact]
    public async Task Ikinci_refresh_gunceller_cift_kayit_olmaz()
    {
        var date = new DateTimeOffset(2099, 1, 2, 0, 0, 0, TimeSpan.Zero);
        await ClearAsync(date);

        var h = new SayanHandler { Body = Xml("02.01.2099", "34.2567") };
        var svc = Service(h);
        Assert.Equal(3, await svc.RefreshAsync());

        h.Body = Xml("02.01.2099", "35.1111"); // kur değişti
        Assert.Equal(3, await svc.RefreshAsync(force: true)); // throttle'ı bilinçli atla

        var usd = Assert.Single(await ReadAsync(date, "USD")); // TEK satır (Tarih+Kod upsert)
        Assert.Equal(35.1111m, usd.ForexSatis); // güncellenmiş değer
    }

    // ---- (c) Throttle: ≤30dk ikinci çağrı -1 döner ve HTTP'ye GİTMEZ ----
    [Fact]
    public async Task Throttle_ikinci_cagri_http_yapmaz_eksi_bir_doner()
    {
        var date = new DateTimeOffset(2099, 1, 3, 0, 0, 0, TimeSpan.Zero);
        await ClearAsync(date);

        var h = new SayanHandler { Body = Xml("03.01.2099", "34.2567") };
        var svc = Service(h);

        Assert.Equal(3, await svc.RefreshAsync()); // başarılı → damga atılır
        Assert.Equal(1, h.Cagri);

        Assert.Equal(-1, await svc.RefreshAsync()); // zorla:false, 30dk dolmadı → throttle
        Assert.Equal(1, h.Cagri);                   // TCMB'ye İKİNCİ istek YOK
    }

    // ---- (d) Bozuk XML → 0, patlamaz; başarısızlık damga BASMAZ (sonraki çağrı yine dener) ----
    [Fact]
    public async Task Bozuk_xml_sifir_doner_patlamaz_ve_damga_basmaz()
    {
        var h = new SayanHandler { Body = "bu bir xml değil <<<" };
        var svc = Service(h);

        Assert.Equal(0, await svc.RefreshAsync());
        Assert.Equal(1, h.Cagri);

        // Başarısız çekim throttle damgası basmadı → ikinci çağrı yine HTTP'ye gider.
        Assert.Equal(0, await svc.RefreshAsync());
        Assert.Equal(2, h.Cagri);
    }

    // ---- (d2) Ağ hatası → 0, patlamaz ----
    [Fact]
    public async Task Ag_hatasi_sifir_doner_patlamaz()
    {
        var h = new SayanHandler { AgHatasi = true };
        Assert.Equal(0, await Service(h).RefreshAsync());
        Assert.Equal(1, h.Cagri);
    }
}
