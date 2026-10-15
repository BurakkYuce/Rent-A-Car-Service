using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// KABİS uçları GERÇEK Web boru hattında (cookie + CSRF + racar_app). BAĞIMSIZ ORACLE: şifre test içinde rastgele
/// üretilir; beklenen bayrak/durum/kod elle kurulan senaryodan yazılır.
/// </summary>
[Collection("web")]
public sealed class UiKabisApiTests(WebFixture fx)
{
    private readonly SystemApiTestKit _kit = new(fx);
    private const string Settings = V1 + "/ayarlar/kabis";

    [Fact]
    public async Task Ayar_sifresi_hicbir_yanitta_donmez_PUT_surum_ister_ve_bayat_surum_409()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var first = await Json(await admin.C.GetAsync(Settings));
        Assert.False(first.GetProperty("sifreTanimli").GetBoolean());
        Assert.False(first.TryGetProperty("sifre", out _));

        var password = "S" + Guid.NewGuid().ToString("N");
        var body = new { etkin = true, kurum = "Jandarma", kullanici = "firma01", sifre = password, sifreTemizle = false,
            teslimiEngelle = false, surum = first.GetProperty("surum").GetString() };
        var r = await Send(admin, HttpMethod.Put, Settings, body);
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain(password, text, StringComparison.Ordinal);
        var saved = System.Text.Json.JsonDocument.Parse(text).RootElement;
        Assert.True(saved.GetProperty("sifreTanimli").GetBoolean());
        Assert.Equal("Jandarma", saved.GetProperty("kurum").GetString());

        // Cipher da hiçbir okuma yüzeyine sızmaz (ayar + denetim).
        var cipher = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.KabisSifreEnc).FirstAsync());
        Assert.False(string.IsNullOrEmpty(cipher));
        foreach (var url in new[] { Settings, V1 + "/ayarlar", V1 + "/denetim?tablo=Ayarlar&boyut=200" })
        {
            var t = await (await admin.C.GetAsync(url)).Content.ReadAsStringAsync();
            Assert.False(t.Contains(password, StringComparison.Ordinal) || t.Contains(cipher!, StringComparison.Ordinal),
                $"{url} KABİS sırrını sızdırıyor");
        }

        // Sürümsüz tam değiştirme reddedilir; bayat sürüm 409 cakisma.
        await Problem(await Send(admin, HttpMethod.Put, Settings, body with { }), HttpStatusCode.Conflict, "cakisma");
        await Problem(await Send(admin, HttpMethod.Put, Settings, new { etkin = false, kurum = "Emniyet", kullanici = "firma01",
            sifre = (string?)null, sifreTemizle = false, teslimiEngelle = false, surum = (string?)null }), HttpStatusCode.BadRequest, null, "surum");

        // Bağlantı sınaması stub'ta dürüst: başarı yok.
        var test = await Json(await Send(admin, HttpMethod.Post, Settings + "/baglanti-testi"));
        Assert.False(test.GetProperty("ok").GetBoolean());
        Assert.Contains("bağlı değil", test.GetProperty("mesaj").GetString());

        // Operatör ayar uçlarına erişemez (ManageUsers).
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        await Problem(await op.C.GetAsync(Settings), HttpStatusCode.Forbidden, "yetki_yok");
    }

    [Fact]
    public async Task Kira_kabis_ucu_baska_kiraciya_404_baska_subeye_403_islem_muhasebeye_403()
    {
        var e = await _kit.SetupAsync();
        Guid rental;
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var s = host.ScopeFor(e.TenantId))
        {
            var sp = s.ServiceProvider;
            var customer = await sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput
            { Tip = CustomerType.Bireysel, Ad = "Ali", Soyad = "Kaya" });
            var start = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-1).AddHours(9);
            rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
            {
                MusteriId = customer, VehicleId = await TestVehicle.NewAsync(sp), BasTar = start, BitTar = start.AddDays(2),
                GunlukUcret = 100m, CikisOfisi = "SubeB",
            });
        }

        var admin = await _kit.LoginAsync(e, Who.Admin);
        var status = await Json(await admin.C.GetAsync($"{V1}/kiralar/{rental}/kabis"));
        Assert.False(status.GetProperty("etkin").GetBoolean());
        Assert.Equal(0, status.GetProperty("bildirimler").GetArrayLength());

        // Kayıt yokken işlem 400 (alan yok); başka kiracı 404; operatör (SubeA) başka şubenin kirasına 403.
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/kiralar/{rental}/kabis/Kiralama/yeniden-dene"),
            HttpStatusCode.BadRequest, null);
        var other = await _kit.SetupAsync();
        var otherAdmin = await _kit.LoginAsync(other, Who.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAdmin.C.GetAsync($"{V1}/kiralar/{rental}/kabis")).StatusCode);
        var op = await _kit.LoginAsync(e, Who.OperatorA);
        await Problem(await op.C.GetAsync($"{V1}/kiralar/{rental}/kabis"), HttpStatusCode.Forbidden, "yetki_yok");
        // Muhasebe okur, işlem yapamaz (OperationsWrite).
        var acc = await _kit.LoginAsync(e, Who.Accounting);
        await Json(await acc.C.GetAsync($"{V1}/kiralar/{rental}/kabis"));
        await Problem(await Send(acc, HttpMethod.Post, $"{V1}/kiralar/{rental}/kabis/Kiralama/gerekli-degil", new { gerekce = "x" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        // Geçersiz tür adı alan hatası.
        await Problem(await Send(admin, HttpMethod.Post, $"{V1}/kiralar/{rental}/kabis/Sorgu/yeniden-dene"),
            HttpStatusCode.BadRequest, null, "tur");

        // Bildirilemeyenler listesi kapıyla aynı: KABİS kapalıyken boş.
        var problems = await Json(await admin.C.GetAsync($"{V1}/kabis/bildirilemeyenler"));
        Assert.Equal(0, problems.GetArrayLength());
    }
}
