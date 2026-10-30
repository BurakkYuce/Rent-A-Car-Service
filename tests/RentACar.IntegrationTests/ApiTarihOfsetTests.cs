using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Harici API (<c>/api/v1</c>) ofsetli tarih girdisi: <c>+03:00</c> önceden 500'dü (Npgsql timestamptz ofsetli
/// değeri reddeder). Aynı ANI temsil eden <c>Z</c>, <c>+03:00</c> ve ofsetsiz (sunucu yerel saati — bugünkü yorum)
/// girdiler aynı kaydı üretmeli; gövde (JSON) ve sorgu parametresi ayrı ayrı. Beklenen değerler elle kurulur.
/// </summary>
[Collection("postgres")]
public sealed class ApiTarihOfsetTests(PostgresFixture fx)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private async Task<HttpClient> LoginAsync(ApiFactory api)
    {
        var code = $"ofs{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N"); // sabit parola yok
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", password);
        return await api.LoginClientAsync(code, "umit", password);
    }

    private static async Task<JsonElement> PostCreatedAsync(HttpClient c, string url, object body)
    {
        var resp = await c.PostAsJsonAsync(url, body);
        var raw = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.Created, $"POST {url} → {(int)resp.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement.Clone();
    }

    /// <summary>Aynı an, üç yazım. Ofsetsiz yazım sunucunun yerel saatidir (test host'u aynı süreç).</summary>
    private static string[] ThreeForms(DateTimeOffset utc) =>
    [
        utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv),
        utc.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv),
        utc.ToLocalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", Inv),
    ];

    /// <summary>Sorgu parametresi yazımları. Minimal API sorgu bağlaması ofsetsiz <c>DateTimeOffset</c>'i bugün UTC
    /// sayar (gövdedeki JSON'dan farklı, yerel değil) — bu yorum korunur, o yüzden ofsetsiz yazım UTC duvar saati.</summary>
    private static string[] QueryForms(DateTimeOffset utc) =>
    [
        utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", Inv),
        utc.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", Inv),
        utc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv),
    ];

    [Fact]
    public async Task Reservation_body_dates_in_Z_plus03_and_offsetless_produce_same_record()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await LoginAsync(api);
        var customerId = (await PostCreatedAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Ofset", soyad = "Test" }))
            .GetProperty("id").GetGuid();

        // Elle kurulmuş an: 10 gün sonra 09:00Z (= 12:00+03:00), 3 gün; 3 × 100 = 300.
        var start = TestZaman.DaysLater(10, hour: 9);
        var end = start.AddDays(3);
        var day = start.UtcDateTime.ToString("yyyy-MM-dd", Inv);
        Assert.Equal($"{day}T12:00:00+03:00", ThreeForms(start)[1]); // yazımın kendisi elle doğrulanır

        var startForms = ThreeForms(start);
        var endForms = ThreeForms(end);
        for (var i = 0; i < 3; i++)
        {
            var plate = "34" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var vehicleId = (await PostCreatedAsync(c, "/api/v1/vehicles",
                new { plaka = plate, grup = "B", durum = "Musait", km = 0, yakit = "Benzin" })).GetProperty("id").GetGuid();

            var r = await PostCreatedAsync(c, "/api/v1/reservations", new
            {
                musteriId = customerId, vehicleId, basTar = startForms[i], bitTar = endForms[i], gunlukUcret = 100m,
            });

            Assert.Equal(start, r.GetProperty("basTar").GetDateTimeOffset());
            Assert.Equal(end, r.GetProperty("bitTar").GetDateTimeOffset());
            Assert.Equal(TimeSpan.Zero, r.GetProperty("basTar").GetDateTimeOffset().Offset);
            Assert.Equal(3, r.GetProperty("gun").GetInt32());
            Assert.Equal(300m, r.GetProperty("tutar").GetDecimal());
        }
    }

    [Fact]
    public async Task Cash_body_date_and_report_query_dates_accept_offsets_with_same_boundaries()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await LoginAsync(api);
        var customerId = (await PostCreatedAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Kasa", soyad = "Ofset" }))
            .GetProperty("id").GetGuid();

        // Para tarihi gelecek olamaz → 2 saat önce (tam saniye). Tahsilat gövdesi +03:00 ile.
        var at = TestZaman.Now().AddHours(-2);
        var cash = await PostCreatedAsync(c, "/api/v1/finance/cash/collect",
            new { cariId = customerId, tutar = 100m, tarih = ThreeForms(at)[1] });
        Assert.Equal(at, cash.GetProperty("tarih").GetDateTimeOffset());

        async Task<decimal> KasaGirisAsync(string from, string to)
        {
            var resp = await c.GetAsync(
                $"/api/v1/reports/kasa-banka?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
            var raw = await resp.Content.ReadAsStringAsync();
            Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{(int)resp.StatusCode}: {raw}");
            return JsonDocument.Parse(raw).RootElement.GetProperty("kasaGiris").GetDecimal();
        }

        var from = QueryForms(at.AddMinutes(-1));
        var toIncl = QueryForms(at);              // sınır dahil (<=)
        var toExcl = QueryForms(at.AddSeconds(-1)); // bir saniye önce → hariç
        for (var i = 0; i < 3; i++)
        {
            Assert.True(100m == await KasaGirisAsync(from[i], toIncl[i]), $"dahil, yazım {i}: {toIncl[i]}");
            Assert.True(0m == await KasaGirisAsync(from[i], toExcl[i]), $"hariç, yazım {i}: {toExcl[i]}");
        }

        // Yaşlandırma tek tarih parametresi (asOf) de +03:00 ile 200.
        var aging = await c.GetAsync($"/api/v1/reports/aging?asOf={Uri.EscapeDataString(QueryForms(at)[1])}");
        Assert.Equal(HttpStatusCode.OK, aging.StatusCode);
    }

    [Fact]
    public async Task Malformed_dates_are_400_not_500()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await LoginAsync(api);

        var body = await c.PostAsJsonAsync("/api/v1/reservations", new
        {
            musteriId = Guid.NewGuid(), vehicleId = Guid.NewGuid(), basTar = "gecersiz-tarih", bitTar = "yarın", gunlukUcret = 100m,
        });
        Assert.Equal(HttpStatusCode.BadRequest, body.StatusCode);

        var numeric = await c.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = Guid.NewGuid(), tutar = 100m, tarih = 12345 }); // sayı: dize değil
        Assert.Equal(HttpStatusCode.BadRequest, numeric.StatusCode);

        var query = await c.GetAsync("/api/v1/reports/kasa-banka?from=abc");
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
    }
}
