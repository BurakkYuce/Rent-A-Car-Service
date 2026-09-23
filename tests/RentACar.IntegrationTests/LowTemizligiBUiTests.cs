using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;

namespace RentACar.IntegrationTests;

/// <summary>
/// Low temizliği B — <c>/api/ui</c> yüzeyi GERÇEK Web boru hattında (cookie + CSRF + pilot):
/// (3) ek hizmet ekleme <c>Idempotency-Key</c> zorunlu + mükerrer 409 (sıralı, eşzamanlı, farklı içerik, başka kira);
/// (5) "+03:00" ofsetli tarih para/kira uçlarında 500 DEĞİL (DB'ye UTC gider, an korunur).
/// BAĞIMSIZ ORACLE: 3 gün × 100 net = 300 + %20 KDV = 360 brüt; GPS 50 net × 2 = 100 + 20 KDV = 120 → 480.
/// Kullanıcılar çalışma anında rastgele parolayla üretilir.
/// </summary>
[Collection("web")]
public sealed class LowTemizligiBUiTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Kira = V1 + "/kiralar";
    private static readonly TimeSpan Ist = TimeSpan.FromHours(3);

    private sealed record Ortam(Guid TenantId, string Kod, string Kullanici, string Sifre, Guid MusteriId, Guid GpsId);
    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];
    private static DateTimeOffset Simdi() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> OrtamKurAsync()
    {
        var tenant = Guid.NewGuid();
        var kod = Rastgele("lowb");
        var ad = Rastgele("u");
        var sifre = WebFixture.RastgeleParola();
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Code = kod, Name = kod, IsActive = true });
            var u = new User { TenantId = tenant, UserName = ad, DisplayName = ad, Rol = UserRole.Admin, IsActive = true };
            u.PasswordHash = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>().HashPassword(u, sifre);
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(tenant, true);
        var musteri = new Customer { Tip = CariType.Bireysel, Ad = "Ece", Soyad = "Kaya" };
        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true };
        await VeriYazAsync(tenant, db => { db.Customers.Add(musteri); db.EkHizmetTanimlari.Add(gps); });
        return new Ortam(tenant, kod, ad, sifre, musteri.Id, gps.Id);
    }

    private async Task VeriYazAsync(Guid tenantId, Action<AppDbContext> yaz)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        yaz(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AracAsync(Ortam o)
    {
        var v = new Vehicle
        {
            Plaka = "34 LB " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea",
            Grup = "C", Sube = "SubeA", Durum = VehicleStatus.Musait, Km = 1000
        };
        await VeriYazAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private async Task<Oturum> GirisAsync(Ortam o)
    {
        var c = fx.Web.Istemci();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var once = CerezDegeri(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanici, sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CerezDegeri(r, "XSRF-TOKEN")!);
    }

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? govde = null, string? idem = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (idem is not null) req.Headers.Add("Idempotency-Key", idem);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r, HttpStatusCode beklenen = HttpStatusCode.OK)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == beklenen, $"Beklenen {(int)beklenen}, gelen {(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod)
    {
        var kok = await Json(r, durum);
        Assert.Equal(kod, kok.GetProperty("kod").GetString());
        return kok;
    }

    private static string YeniAnahtar() => "lowb-" + Guid.NewGuid().ToString("N");

    /// <summary>3 gün × 100 net "Günlük" kira; <paramref name="bas"/> ofsetiyle gönderilir.</summary>
    private static async Task<Guid> KiraAcAsync(Oturum s, Ortam o, Guid arac, DateTimeOffset bas)
    {
        var j = await Json(await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId = o.MusteriId, vehicleId = arac, basTar = bas, bitTar = bas.AddDays(3),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi = "SubeA", donusOfisi = "SubeA",
        }), HttpStatusCode.Created);
        return j.GetProperty("id").GetGuid();
    }

    private static decimal Dec(JsonElement e, string ad) => e.GetProperty(ad).GetDecimal();

    private static string EkUrl(Guid kira) => $"{Kira}/{kira}/ek-hizmetler";

    private static async Task<(int Kalem, decimal Genel)> KiraDurumuAsync(Oturum s, Guid kira)
    {
        var d = await Json(await s.C.GetAsync($"{Kira}/{kira}"));
        return (d.GetProperty("ekHizmetler").GetArrayLength(), Dec(d.GetProperty("kira"), "genelToplam"));
    }

    // ------------------------------------------------------------ (3) ek hizmet idempotency

    [Fact]
    public async Task Ek_hizmet_basliksiz_400_ayni_anahtar_ikinci_kez_409_mevcut_ile_tek_kalem()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o);
        var kira = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(8));
        var govde = new { ekHizmetTanimId = o.GpsId, miktar = 2m };

        // Başlıksız: 400 errors[Idempotency-Key], hiçbir şey yazılmaz.
        var p = await ProblemBekle(await Gonder(s, HttpMethod.Post, EkUrl(kira), govde), HttpStatusCode.BadRequest, UiHata.Dogrulama);
        Assert.True(p.GetProperty("errors").TryGetProperty("Idempotency-Key", out _), p.ToString());
        Assert.Equal((0, 360m), await KiraDurumuAsync(s, kira));

        var anahtar = YeniAnahtar();
        var ilk = await Json(await Gonder(s, HttpMethod.Post, EkUrl(kira), govde, anahtar));
        Assert.Equal(480m, Dec(ilk.GetProperty("kira"), "genelToplam"));
        var kalemId = ilk.GetProperty("kalemler")[0].GetProperty("id").GetGuid();

        // Kaybolan yanıttan sonraki birebir tekrar: 409 mukerrer + mevcut{ayniIcerik=true}; ikinci kalem YOK.
        var tekrar = await ProblemBekle(await Gonder(s, HttpMethod.Post, EkUrl(kira), govde, anahtar), HttpStatusCode.Conflict, UiHata.Mukerrer);
        var mevcut = tekrar.GetProperty("mevcut");
        Assert.Equal(kalemId, mevcut.GetProperty("id").GetGuid());
        Assert.True(mevcut.GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(120m, Dec(mevcut, "tutar"));

        // Aynı anahtar, farklı miktar: 409 + ayniIcerik=false; gelen kalem YAZILMADI.
        var farkli = await ProblemBekle(await Gonder(s, HttpMethod.Post, EkUrl(kira), new { ekHizmetTanimId = o.GpsId, miktar = 5m }, anahtar),
            HttpStatusCode.Conflict, UiHata.Mukerrer);
        Assert.False(farkli.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal((1, 480m), await KiraDurumuAsync(s, kira));

        // Yeni anahtar = meşru ikinci kalem (1 × 60 brüt) → 540.
        await Json(await Gonder(s, HttpMethod.Post, EkUrl(kira), new { ekHizmetTanimId = o.GpsId, miktar = 1m }, YeniAnahtar()));
        Assert.Equal((2, 540m), await KiraDurumuAsync(s, kira));
    }

    [Fact]
    public async Task Ek_hizmet_eszamanli_ayni_anahtar_tek_kalem_ve_baska_kira_mevcut_sizdirmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o);
        var kira = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(8));
        var baska = await KiraAcAsync(s, o, await AracAsync(o), Simdi().AddHours(8));
        var anahtar = YeniAnahtar();

        var yanitlar = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            Gonder(s, HttpMethod.Post, EkUrl(kira), new { ekHizmetTanimId = o.GpsId, miktar = 1m }, anahtar)));
        var kodlar = yanitlar.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, kodlar.Count(k => k == HttpStatusCode.OK));
        Assert.All(kodlar.Where(k => k != HttpStatusCode.OK), k => Assert.Equal(HttpStatusCode.Conflict, k));
        Assert.Equal((1, 420m), await KiraDurumuAsync(s, kira)); // 360 + 60

        // Aynı anahtar BAŞKA kirada: 409, mevcut YOK (öbür kiranın kalemi sızmaz), başka kiraya yazılmaz.
        var p = await ProblemBekle(await Gonder(s, HttpMethod.Post, EkUrl(baska), new { ekHizmetTanimId = o.GpsId, miktar = 1m }, anahtar),
            HttpStatusCode.Conflict, UiHata.Mukerrer);
        Assert.False(p.TryGetProperty("mevcut", out var m) && m.ValueKind != JsonValueKind.Null, p.ToString());
        Assert.Equal((0, 360m), await KiraDurumuAsync(s, baska));
    }

    // ------------------------------------------------------------ (5) +03:00 ofsetli tarih → 500 değil

    [Fact]
    public async Task Ofsetli_tarih_nakit_tahsilat_ve_odeme_500_vermez_an_korunur()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o);
        var an = Simdi().AddHours(-2);
        var yerel = an.ToOffset(Ist); // aynı an, "+03:00" ile

        foreach (var uc in new[] { "/finans/tahsilat", "/finans/odeme" })
        {
            var r = await Gonder(s, HttpMethod.Post, V1 + uc,
                new { cariId = o.MusteriId, tutar = 10m, hesap = "Kasa", tarih = yerel }, YeniAnahtar());
            await Json(r);
        }

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var tarihler = await db.CashTransactions.AsNoTracking().Where(t => t.CariId == o.MusteriId).Select(t => t.Tarih).ToListAsync();
        Assert.Equal(2, tarihler.Count);
        Assert.All(tarihler, t => Assert.Equal(an.UtcDateTime, t.UtcDateTime)); // an kaymadı (3 saat hatası yok)
    }

    [Fact]
    public async Task Ofsetli_tarih_kira_olustur_teslim_uzat_donus_500_vermez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o);
        var bas = Simdi().AddHours(-1);
        var kira = await KiraAcAsync(s, o, await AracAsync(o), bas.ToOffset(Ist));
        var d = await Json(await s.C.GetAsync($"{Kira}/{kira}"));
        Assert.Equal(bas.UtcDateTime, d.GetProperty("kira").GetProperty("basTar").GetDateTimeOffset().UtcDateTime);

        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{kira}/teslim", new { cikisKm = 1000, cikisYakit = 8 }));
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{kira}/uzat", new { yeniBitTar = bas.AddDays(4).ToOffset(Ist) }));
        await Json(await Gonder(s, HttpMethod.Post, $"{Kira}/{kira}/donus",
            new { donusKm = 1100, donusYakit = 8, gercekDonus = Simdi().ToOffset(Ist) }));
    }
}
