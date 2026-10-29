using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Integrations;
using RentACar.Application.Notifications;
using RentACar.Application.PublicSite;
using RentACar.Application.TenantSettings;
using RentACar.Application.Vehicles;
using RentACar.Application.WebSite;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.PublicSite;

namespace RentACar.IntegrationTests;

/// <summary>
/// Firma sitesi talep formu saat taşır: GET ön dolum (<c>basSaat</c>/<c>bitSaat</c>, varsayılan 10:00), POST'ta
/// İstanbul gün+saati UTC'ye çevrilip talebe yazılır; iki saatten biri bile yok/bozuksa ESKİ davranış (günün UTC
/// gece yarısı). BAĞIMSIZ ORACLE: beklenen anlar elle yazılır (İstanbul UTC+3, yaz saati yok: 10:00 → 07:00Z,
/// 00:00 → önceki gün 21:00Z, 23:59 → 20:59Z), üretim kodundaki çeviriden türetilmez.
/// </summary>
[Collection("postgres")]
public sealed class PublicBookingRequestTimeTests(PostgresFixture fx)
{
    private static readonly TimeZoneInfo IstanbulZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAoAAAAICAIAAABPmPnhAAAAFElEQVR4nGM8YWTEgBsw4ZEb0tIAKaUBPDvSacQAAAAASUVORK5CYII=");

    /// <summary>İstanbul'da bugünden <paramref name="offset"/> gün sonrası (ileri tarih: talep geçmişe alınmaz).</summary>
    private static DateOnly IstanbulDay(int offset)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstanbulZone).DateTime).AddDays(offset);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(DateOnly d, int hour, int minute)
        => new(d.Year, d.Month, d.Day, hour, minute, 0, TimeSpan.Zero);

    private static string? InputValue(string html, string name)
        => Regex.Match(html, $@"<input name=""{name}"" type=""(?:date|time)"" value=""([^""]*)""") is { Success: true } m
            ? m.Groups[1].Value : null;

    private async Task<Guid> SeedTenantAsync()
    {
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
            NullTenantContext.Instance, NullCurrentUser.Instance);
        var t = new Tenant { Code = "tlp" + Guid.NewGuid().ToString("N")[..10], Name = "Talep", IsActive = true };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    /// <summary>Formu GET ile açar (antiforgery çerezi + belirteci) ve verilen alanlarla gönderir.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, params (string Key, string Value)[] fields)
    {
        var page = await client.GetStringAsync("/rezervasyon-talebi");
        var input = Regex.Match(page, @"<input[^>]*__RequestVerificationToken[^>]*>").Value;
        var token = WebUtility.HtmlDecode(Regex.Match(input, @"value=""([^""]+)""").Groups[1].Value);
        Assert.False(string.IsNullOrEmpty(token), "antiforgery belirteci bulunamadı");
        var form = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token), new("adSoyad", "Ayşe Yılmaz"), new("telefon", "0555 111 22 33"),
        };
        form.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
        return await client.PostAsync("/rezervasyon-talebi/gonder", new FormUrlEncodedContent(form));
    }

    private static async Task<IReadOnlyList<PublicBookingRequest>> ListAsync(TestHost host, Guid tenant)
    {
        using var scope = host.ScopeFor(tenant);
        return await scope.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync();
    }

    [Fact]
    public async Task Talep_sayfasi_saatleri_on_doldurur_yoksa_ya_da_bozuksa_10_00()
    {
        var tenant = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        var (bas, bit) = (Iso(IstanbulDay(10)), Iso(IstanbulDay(13)));

        var timed = await client.GetStringAsync($"/rezervasyon-talebi?bas={bas}&bit={bit}&basSaat=09:30&bitSaat=18:15");
        Assert.Equal(bas, InputValue(timed, "bas"));
        Assert.Equal(bit, InputValue(timed, "bit"));
        Assert.Equal("09:30", InputValue(timed, "basSaat"));
        Assert.Equal("18:15", InputValue(timed, "bitSaat"));

        foreach (var path in new[] { "/rezervasyon-talebi", "/rezervasyon-talebi?basSaat=25:00&bitSaat=x" })
        {
            var html = await client.GetStringAsync(path);
            Assert.Equal("10:00", InputValue(html, "basSaat"));
            Assert.Equal("10:00", InputValue(html, "bitSaat"));
        }
    }

    public static TheoryData<string?, string?, int, int, int, int, int, int> Cases() => new()
    {
        // basSaat, bitSaat → beklenen alış (gün kayması, saat, dakika) ve iade (gün kayması, saat, dakika), UTC
        { "10:00", "10:00", 0, 7, 0, 0, 7, 0 },       // 10:00 İstanbul = 07:00Z
        { "00:00", "23:59", -1, 21, 0, 0, 20, 59 },   // gece yarısı sınırı: alış önceki UTC gününe düşer
        { null, null, 0, 0, 0, 0, 0, 0 },             // saat yok → eski davranış (UTC gece yarısı)
        { "10:00", null, 0, 0, 0, 0, 0, 0 },          // tek saat → eski davranış (/musaitlik ile aynı kural)
        { " ", "10:00", 0, 0, 0, 0, 0, 0 },           // boşluk = BOŞ alan → eski davranış (biçimsiz DOLU saat reddedilir, aşağıda)
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task POST_saatli_talebi_Istanbul_saatinden_UTC_yazar_saatsizde_eski_davranis(
        string? fromTime, string? toTime, int startShift, int startHour, int startMinute, int endShift, int endHour, int endMinute)
    {
        var tenant = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, tenant);
        var (start, end) = (IstanbulDay(10), IstanbulDay(13));
        var fields = new List<(string, string)> { ("bas", Iso(start)), ("bit", Iso(end)) };
        if (fromTime is not null) fields.Add(("basSaat", fromTime));
        if (toTime is not null) fields.Add(("bitSaat", toTime));

        using var r = await PostFormAsync(factory.Client(), [.. fields]);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/talep-alindi", r.Headers.Location?.OriginalString);

        using var host = new TestHost(fx.AppConnectionString);
        var request = Assert.Single(await ListAsync(host, tenant));
        Assert.Equal(Utc(start.AddDays(startShift), startHour, startMinute), request.BasTar);
        Assert.Equal(Utc(end.AddDays(endShift), endHour, endMinute), request.BitTar);
    }

    [Fact]
    public async Task Saatli_talep_baska_kiracida_gorunmez()
    {
        var a = await SeedTenantAsync();
        var b = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, a);
        using var r = await PostFormAsync(factory.Client(),
            ("bas", Iso(IstanbulDay(10))), ("bit", Iso(IstanbulDay(12))), ("basSaat", "10:00"), ("bitSaat", "10:00"));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        using var host = new TestHost(fx.AppConnectionString);
        Assert.Single(await ListAsync(host, a));
        Assert.Empty(await ListAsync(host, b));
    }

    /// <summary>PR #412 adversarial L1 (Probe_E): saatli aramada 7 gün 3 saat → 8 gün haftalık kademe gösterilir; talep
    /// formunda saat metin kutusuna düşüp "10.00" yazılırsa saat eskiden SESSİZCE düşüyor, talep 7 gün günlük kademeyle
    /// gösterilenden farklı fiyata dönüşüyordu. Artık DOLU ama biçimsiz saat kodla reddedilir, talep YAZILMAZ.</summary>
    [Theory]
    [InlineData("10.00", "13:00")]
    [InlineData("10:00", "25:00")]
    [InlineData("10:00Z", "13:00")]
    [InlineData(" 10:00", "13:00")]
    [InlineData("10:00:00", "13:00")]
    [InlineData("10:00", "x")]
    public async Task POST_dolu_ama_bicimsiz_saat_kodla_reddedilir_talep_yazilmaz(string fromTime, string toTime)
    {
        var tenant = await SeedTenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        var (listingId, _) = await SeedListingAsync(host, tenant);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        using var r = await PostFormAsync(client, ("bas", Iso(IstanbulDay(5))), ("bit", Iso(IstanbulDay(12))),
            ("basSaat", fromTime), ("bitSaat", toTime), ("ilanId", listingId.ToString()));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/rezervasyon-talebi?hata=saat_bicim", r.Headers.Location?.OriginalString);
        Assert.Empty(await ListAsync(host, tenant));

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(r.Headers.Location!.OriginalString));
        Assert.Contains("Saat biçimi geçersiz", page);
    }

    [Fact]
    public async Task POST_gecmis_tarihli_talep_kodla_reddedilir_adresteki_serbest_metin_basilmaz()
    {
        var tenant = await SeedTenantAsync();
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();

        using var r = await PostFormAsync(client, ("bas", Iso(IstanbulDay(-1))), ("bit", Iso(IstanbulDay(2))));
        Assert.Equal("/rezervasyon-talebi?hata=tarih_gecmis", r.Headers.Location?.OriginalString);
        using var host = new TestHost(fx.AppConnectionString);
        Assert.Empty(await ListAsync(host, tenant));
        Assert.Contains("Alış zamanı geçmiş olamaz",
            WebUtility.HtmlDecode(await client.GetStringAsync(r.Headers.Location!.OriginalString)));

        // Adreste serbest metin: basılmaz, genel metin basılır (içerik sahteciliği).
        var spoofed = WebUtility.HtmlDecode(await client.GetStringAsync("/rezervasyon-talebi?hata=Hesabiniz%20askiya%20alindi"));
        Assert.DoesNotContain("Hesabiniz askiya alindi", spoofed);
        Assert.Contains("Talebiniz gönderilemedi", spoofed);
    }

    [Fact]
    public async Task Musaitlik_dolu_ama_bicimsiz_saatte_hata_gosterir_tarihleri_korur_fiyat_hesaplamaz()
    {
        var tenant = await SeedTenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        await SeedListingAsync(host, tenant);
        using var factory = new PublicSiteFactory(fx, tenant);
        var (bas, bit) = (Iso(IstanbulDay(5)), Iso(IstanbulDay(12)));

        // Kontrol: geçerli saatle aynı aralık aranır ve özet basılır (aşağıdaki DoesNotContain anlamlı olsun).
        var valid = WebUtility.HtmlDecode(await factory.Client().GetStringAsync($"/musaitlik?bas={bas}&bit={bit}&basSaat=10:00&bitSaat=13:00"));
        Assert.Contains("araç müsait", valid);
        Assert.DoesNotContain("Saat biçimi geçersiz", valid);

        using var r = await factory.Client().GetAsync($"/musaitlik?bas={bas}&bit={bit}&basSaat=10.00&bitSaat=13:00");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        Assert.Contains("Saat biçimi geçersiz", html);
        Assert.Equal(bas, InputValue(html, "bas"));
        Assert.Equal(bit, InputValue(html, "bit"));
        Assert.DoesNotContain("araç müsait", html); // aralık fiyatı hesaplanmadı; filo tarihsiz listelenir
    }

    [Fact]
    public async Task Talep_alindi_bildirimi_Istanbul_saatini_saatsizde_yalniz_gunu_yazar()
    {
        var mail = new FakeEmail();
        using var host = new TestHost(fx.AppConnectionString, s => s.AddSingleton<IEmailSender>(mail));
        var tenant = Guid.NewGuid();
        using (var admin = host.ScopeFor(tenant))
        {
            await admin.ServiceProvider.GetRequiredService<TenantSettingsService>().SaveAsync(new TenantSettingsModel
            {
                FirmaUnvan = "Talep A.Ş.", SmtpHost = "smtp.test.local", SmtpPort = 587, SmtpGonderenAdres = "talep@ornek.com",
            });
            await admin.ServiceProvider.GetRequiredService<CustomerNotificationService>().SaveTemplateAsync(new MesajSablonInput
            {
                Tur = MessageType.TalepAlindi, Kanal = MessageChannel.Eposta, Konu = "Talep", Govde = "[{CikisTarih}|{DonusTarih}]",
            });
        }

        var (start, end) = (IstanbulDay(10), IstanbulDay(13));
        string Tr(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        using (var pub = host.ScopeFor(tenant, role: null))
        {
            var svc = pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
            await svc.CreateAsync(new PublicBookingRequestInput
            {
                AdSoyad = "Ayşe Yılmaz", Telefon = "0555 111 22 33", Email = "a@ornek.com",
                BasTar = Utc(start.AddDays(-1), 21, 0), BitTar = Utc(end, 15, 30), HasTime = true,
            });
            await svc.CreateAsync(new PublicBookingRequestInput
            {
                AdSoyad = "Ali Veli", Telefon = "0555 222 33 44", Email = "b@ornek.com",
                BasTar = Utc(start, 0, 0), BitTar = Utc(end, 0, 0),
            });
        }

        Assert.Equal(2, mail.Sent.Count);
        Assert.Contains($"[{Tr(start)} 00:00|{Tr(end)} 18:30]", mail.Sent.Single(m => m.Mesaj.Alici == "a@ornek.com").Mesaj.GovdeHtml);
        Assert.Contains($"[{Tr(start)}|{Tr(end)}]", mail.Sent.Single(m => m.Mesaj.Alici == "b@ornek.com").Mesaj.GovdeHtml);
    }

    [Fact]
    public void Teklif_baglantisi_saati_yalniz_iki_saat_de_gecerliyse_tasir()
    {
        Assert.Equal("/rezervasyon-talebi?ilan=egea&bas=2030-01-02&bit=2030-01-05&basSaat=09%3a30&bitSaat=18%3a00",
            QuoteLink.Build("egea", "2030-01-02", "2030-01-05", null, null, "09:30", "18:00"));
        Assert.Equal("/rezervasyon-talebi?ilan=egea&bas=2030-01-02&bit=2030-01-05",
            QuoteLink.Build("egea", "2030-01-02", "2030-01-05", null, null, "09:30", "25:00"));
        Assert.Equal("/rezervasyon-talebi?ilan=egea&bas=2030-01-02&bit=2030-01-05",
            QuoteLink.Build("egea", "2030-01-02", "2030-01-05", null, null, "09:30", null));
    }

    [Fact]
    public async Task Kart_ve_detay_talep_baglantilari_secili_tarih_ve_saati_tasir()
    {
        var tenant = await SeedTenantAsync();
        using var host = new TestHost(fx.AppConnectionString);
        var (_, slug) = await SeedListingAsync(host, tenant);
        using var factory = new PublicSiteFactory(fx, tenant);
        var client = factory.Client();
        var (bas, bit) = (Iso(IstanbulDay(20)), Iso(IstanbulDay(23)));
        var query = $"bas={bas}&bit={bit}&basSaat=09:30&bitSaat=18:00";
        var expected = WebUtility.HtmlEncode($"bas={bas}&bit={bit}&basSaat=09%3a30&bitSaat=18%3a00");

        foreach (var path in new[] { $"/musaitlik?{query}", $"/araclar/{slug}?{query}" })
        {
            using var r = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var html = await r.Content.ReadAsStringAsync();
            Assert.True(html.Contains(expected, StringComparison.Ordinal), $"{path}: talep bağlantısında tarih+saat yok");
        }

        // Saatsiz arama: bağlantıda saat parametresi YOK (talep formu varsayılan 10:00 basar).
        var plain = await client.GetStringAsync($"/musaitlik?bas={bas}&bit={bit}");
        Assert.Contains(WebUtility.HtmlEncode($"bas={bas}&bit={bit}"), plain);
        Assert.DoesNotContain("basSaat=", plain);
    }

    private static async Task<(Guid Id, string Slug)> SeedListingAsync(TestHost host, Guid t)
    {
        using var s = host.ScopeFor(t);
        var vehicles = s.ServiceProvider.GetRequiredService<VehicleService>();
        var listings = s.ServiceProvider.GetRequiredService<WebListingService>();
        var id = await vehicles.CreateAsync(new VehicleInput
        {
            Plaka = "34TS" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Marka = "Fiat", Tip = "Egea", Vites = Transmission.Manuel, Yakit = FuelType.Dizel,
            ModelYili = 2023, Durum = VehicleStatus.Musait, GrupBilincliBos = true,
        });
        await s.ServiceProvider.GetRequiredService<VehiclePhotoService>().AddAsync(id, TinyPng);
        var signature = (await listings.PoolAsync()).Single(k => k.Araclar.Count == 1).Imza;
        var listingId = await listings.StepOneSignatureAsync([signature]);
        await listings.StepTwoAsync(listingId, 1500m, null, null, true);
        await listings.StepThreeAsync(listingId, [new OzellikSatiri("Marka", "Fiat")]);
        return (listingId, (await listings.GetAsync(listingId))!.Ilan.Slug);
    }
}
