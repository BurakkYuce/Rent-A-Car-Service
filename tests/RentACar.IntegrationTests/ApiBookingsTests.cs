using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Rezervasyon + Kira API — gerçek HTTP yaşam döngüsü: rezervasyon oluştur → onayla → kiraya
/// çevir → teslim → dönüş. Durum geçişleri + tenant izolasyonu (JWT→RLS). Manuel günlük ücret
/// (tarife gerektirmez).
/// </summary>
[Collection("postgres")]
public sealed class ApiBookingsTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";

    private static async Task<Guid> CreateAndIdAsync(HttpClient c, string url, object body)
    {
        var resp = await c.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private sealed record StatusBody(Guid id, string durum);

    [Fact]
    public async Task Reservation_lifecycle_confirm_convert_deliver_return()
    {
        var code = Uniq("bk");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await api.LoginClientAsync(code, "umit", "p");

        var vehicleId = await CreateAndIdAsync(c, "/api/v1/vehicles",
            new { plaka = "34BK01", grup = "B", durum = "Musait", km = 0, yakit = "Benzin" });
        var custId = await CreateAndIdAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Kir", soyad = "Acı" });

        var bas = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var bit = bas.AddDays(3);

        // Rezervasyon (manuel 100/gün → 3×100=300)
        var resvId = await CreateAndIdAsync(c, "/api/v1/reservations",
            new { musteriId = custId, vehicleId, basTar = bas, bitTar = bit, gunlukUcret = 100m });

        // Onayla → Onayli
        var conf = await c.PostAsync($"/api/v1/reservations/{resvId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, conf.StatusCode);
        Assert.Equal("Onayli", (await conf.Content.ReadFromJsonAsync<StatusBody>())!.durum);

        // Kiraya çevir → 201, rentalId
        var conv = await c.PostAsync($"/api/v1/reservations/{resvId}/convert", null);
        Assert.Equal(HttpStatusCode.Created, conv.StatusCode);
        using var convDoc = JsonDocument.Parse(await conv.Content.ReadAsStringAsync());
        var rentalId = convDoc.RootElement.GetProperty("rentalId").GetGuid();

        // Kira: Kirada, tutar 300
        var rental = await c.GetFromJsonAsync<JsonElement>($"/api/v1/rentals/{rentalId}");
        Assert.Equal("Kirada", rental.GetProperty("durum").GetString());
        Assert.Equal(300m, rental.GetProperty("tutar").GetDecimal());

        // Teslim (çıkış km/yakıt)
        var deliver = await c.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/deliver", new { cikisKm = 1000, cikisYakit = 80 });
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);

        // Dönüş → Tamamlandi
        var ret = await c.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/return",
            new { donusKm = 1300, donusYakit = 80, gercekDonus = bit });
        Assert.Equal(HttpStatusCode.OK, ret.StatusCode);
        Assert.Equal("Tamamlandi", (await ret.Content.ReadFromJsonAsync<StatusBody>())!.durum);
    }

    [Fact]
    public async Task Reservations_are_tenant_isolated()
    {
        var codeA = Uniq("ra");
        var codeB = Uniq("rb");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeA, "umit", "p");
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, codeB, "umit", "p");
        using var api = new ApiFactory(fx.AppConnectionString);

        var ca = await api.LoginClientAsync(codeA, "umit", "p");
        var vId = await CreateAndIdAsync(ca, "/api/v1/vehicles", new { plaka = "34RA01", durum = "Musait", km = 0, yakit = "Benzin" });
        var mId = await CreateAndIdAsync(ca, "/api/v1/customers", new { tip = "Bireysel", ad = "X" });
        var resvId = await CreateAndIdAsync(ca, "/api/v1/reservations",
            new { musteriId = mId, vehicleId = vId, basTar = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), bitTar = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero), gunlukUcret = 50m });

        // Tenant B, A'nın rezervasyonunu görmemeli (liste boş + tekil 404).
        var cb = await api.LoginClientAsync(codeB, "umit", "p");
        var bList = await cb.GetFromJsonAsync<List<StatusBody>>("/api/v1/reservations");
        Assert.DoesNotContain(bList!, r => r.id == resvId);
        var single = await cb.GetAsync($"/api/v1/reservations/{resvId}");
        Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
    }
}
