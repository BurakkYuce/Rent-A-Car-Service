using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Api;
using RentACar.Web.Api.Kira;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.3b — kira formu parite ekleri GERÇEK Web boru hattında (cookie + CSRF + pilot):
/// <c>GET /kiralar/{id}/musteri-ozet</c> (maskeli PII), detay <c>toplamlar</c> + paylaşım <c>mesaj</c>,
/// <c>GET /kiralar/ek-hizmet-katalogu</c>, <c>GET /secim/musteri/{id}</c> ve <c>/secim/arac/{id}</c>.
/// BAĞIMSIZ ORACLE: maskeler, tutarlar ve metinler elle yazılmış sabitlerdir (3 gün × 100 net + %20 KDV = 360;
/// GPS 50 + 10 = 60; ceza 250 + iptal 90 → 250). Kullanıcı adı/parola çalışma anında rastgele (depoda kimlik yok).
/// </summary>
[Collection("web")]
public sealed class UiKiraPariteTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private const string Kira = V1 + "/kiralar";

    private enum Kim { Admin, OperatorA, OperatorB, Muhasebe, Yasakli }

    private sealed class Ortam
    {
        public required Guid TenantId { get; init; }
        public required string Kod { get; init; }
        public required string Sifre { get; init; }
        public required Dictionary<Kim, string> Kullanicilar { get; init; }
        public Guid MusteriId { get; set; }
        public Guid EkHizmetId { get; set; }
    }

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];

    private static DateTimeOffset Simdi() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private async Task<Ortam> OrtamKurAsync()
    {
        var o = new Ortam
        {
            TenantId = Guid.NewGuid(),
            Kod = Rastgele("f43b"),
            Sifre = WebFixture.RastgeleParola(),
            Kullanicilar = Enum.GetValues<Kim>().ToDictionary(k => k, _ => Rastgele("u")),
        };
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = o.TenantId, Code = o.Kod, Name = o.Kod, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (kim, ad) in o.Kullanicilar)
            {
                var (rol, sube) = kim switch
                {
                    Kim.Admin => (UserRole.Admin, (string?)null),
                    Kim.OperatorA or Kim.Yasakli => (UserRole.Operator, "SubeA"),
                    Kim.OperatorB => (UserRole.Operator, "SubeB"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = o.TenantId, UserName = ad, DisplayName = ad, Rol = rol, AtanmisSube = sube, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, o.Sifre);
                db.Users.Add(u);
                // İzinsiz kullanıcı: operatörün tek okuma izni (OperationsWrite) kullanıcı istisnasıyla geri alınır →
                // ne OperationsWrite ne FinanceWrite.
                if (kim == Kim.Yasakli)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = o.TenantId, UserId = u.Id, Izin = "OperationsWrite", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(o.TenantId, true);

        var musteri = new Customer { Tip = CariType.Bireysel, Ad = "Deniz", Soyad = "Yılmaz" };
        var gps = new EkHizmetTanim { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 50m, KdvOrani = 0.20m, Aktif = true };
        await VeriYazAsync(o.TenantId, db => { db.Customers.Add(musteri); db.EkHizmetTanimlari.Add(gps); });
        o.MusteriId = musteri.Id;
        o.EkHizmetId = gps.Id;
        return o;
    }

    private async Task VeriYazAsync(Guid tenantId, Action<AppDbContext> yaz)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        yaz(db);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AracAsync(Ortam o, string sube = "SubeA")
    {
        var v = new Vehicle { Plaka = "34 PAR " + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = sube, Durum = VehicleStatus.Musait, Km = 1000 };
        await VeriYazAsync(o.TenantId, db => db.Vehicles.Add(v));
        return v.Id;
    }

    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Oturum> GirisAsync(Ortam o, Kim kim)
    {
        var c = fx.Web.Istemci();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var once = CerezDegeri(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({kim}): {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CerezDegeri(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> Gonder(Oturum s, HttpMethod m, string url, object? govde = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return s.C.SendAsync(req);
    }

    private static async Task<(JsonElement Kok, string Metin)> JsonMetin(HttpResponseMessage r)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Created,
            $"Beklenen 2xx, gelen {(int)r.StatusCode}: {metin}");
        return (JsonDocument.Parse(metin).RootElement.Clone(), metin);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => (await JsonMetin(r)).Kok;

    private static async Task ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(kod, JsonDocument.Parse(metin).RootElement.GetProperty("kod").GetString());
    }

    private static string? S(JsonElement e, string ad) => e.GetProperty(ad).ValueKind == JsonValueKind.Null ? null : e.GetProperty(ad).GetString();

    private async Task<Guid> KiraAcAsync(Oturum s, Guid musteriId, Guid arac, DateTimeOffset bas, Guid? gps = null, string cikisOfisi = "SubeA")
    {
        var r = await Gonder(s, HttpMethod.Post, Kira, new
        {
            musteriId, vehicleId = arac, basTar = bas, bitTar = bas.AddDays(3),
            gunlukUcret = 100m, fiyatTuru = "Günlük", cikisOfisi, donusOfisi = cikisOfisi,
            ekHizmetler = gps is Guid g ? new[] { new { tanimId = g, miktar = 1m } } : null,
        });
        var j = await Json(r);
        return j.GetProperty("id").GetGuid();
    }

    // Elle kurulmuş kimlik/belge değerleri (geçerli TC sağlama toplamıyla). Beklenen maskeler aşağıda SABİT.
    private const string Tc = "10000000146";
    private const string Ehliyet = "B9876543";
    private const string Pasaport = "U1234567";

    /// <summary>Müşteri: TC + ehliyet ŞİFRELİ yazma yolundan (hızlı müşteri ucu → CustomerService); adres/risk/kara liste
    /// ve eski düz-metin pasaport kolonu doğrudan (okuma yolu şifreli değer yoksa eskisine düşer).</summary>
    private async Task<Guid> PiiliMusteriAsync(Ortam o, Oturum admin, Action<Customer>? ek = null, string? tc = Tc)
    {
        var j = await Json(await Gonder(admin, HttpMethod.Post, Kira + "/musteri", new
        {
            ad = "Ayşe", soyad = "Kaya", tcKimlik = tc, cepTel = "05321112233", email = "ayse@ornek.test",
            il = "İzmir", ilce = "Karşıyaka", ehliyetNo = Ehliyet, ehliyetSinifi = "B", ehliyetYeri = "İzmir",
            ehliyetTarihi = "2015-06-01T00:00:00Z",
        }));
        var id = j.GetProperty("id").GetGuid();
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(o.TenantId);
        await using var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var m = await db.Customers.SingleAsync(c => c.Id == id);
        Assert.Null(m.TcKimlik); // yazma yolu düz TC bırakmaz (at-rest şifreli)
        Assert.Equal(tc is not null, m.TcKimlikEnc is not null);
        m.Adres = "Atatürk Cd. No:5";
        m.MusteriTipi = "Türk Ehliyetli";
        m.EhliyetUlke = "TR";
        m.PasaportNo = Pasaport;
        m.PasaportYeri = "Ankara";
        m.RiskLimiti = 5000m;
        m.KaraListe = true;
        m.Uyari = true;
        m.UyariNedeni = "Geç iade geçmişi";
        ek?.Invoke(m);
        await db.SaveChangesAsync();
        return id;
    }

    // ------------------------------------------------------------ Maske (Blazor SekmeMusteri.Maske ile aynı kural)

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("AB", "**")]
    [InlineData("ABCD", "****")]
    [InlineData("ABCDE", "***DE")]      // L1: 5–7 karakterde yalnız son 2
    [InlineData("123456", "****56")]
    [InlineData("1234567", "*****67")]
    [InlineData("B9876543", "****6543")] // ≥ 8 → son 4
    [InlineData("U123456789", "******6789")]
    public void Maske_uzunluga_gore_son_dort_ya_da_iki_kisa_deger_tamamen_yildiz(string? girdi, string? beklenen)
        => Assert.Equal(beklenen, MusteriGorunumu.Maske(girdi));

    // ------------------------------------------------------------ müşteri özeti

    [Fact]
    public async Task Musteri_ozeti_TC_hic_donmez_belge_yalniz_maskeli_duz_pii_hicbir_yerde_yok()
    {
        var o = await OrtamKurAsync();
        var admin = await GirisAsync(o, Kim.Admin);
        var musteri = await PiiliMusteriAsync(o, admin);
        var arac = await AracAsync(o);
        var op = await GirisAsync(o, Kim.OperatorA);
        var id = await KiraAcAsync(op, musteri, arac, Simdi().AddHours(2));

        foreach (var kim in new[] { Kim.OperatorA, Kim.Muhasebe, Kim.Admin }) // okuma kapısı: OW veya FW
        {
            var s = kim == Kim.OperatorA ? op : await GirisAsync(o, kim);
            var (j, metin) = await JsonMetin(await s.C.GetAsync($"{Kira}/{id}/musteri-ozet"));
            Assert.Equal(musteri, j.GetProperty("id").GetGuid());
            Assert.Equal("Ayşe Kaya", S(j, "ad"));
            Assert.Equal("Bireysel", S(j, "tip"));
            Assert.Equal("****6543", S(j, "ehliyetNoMaskeli"));
            Assert.Equal("****4567", S(j, "pasaportNoMaskeli"));
            // TC HİÇBİR biçimde yok (#262 kararı — Blazor paritesi, KVKK en az veri): ne alan, ne düz, ne maskeli son 4.
            TcHicbirBicimdeYok(j, metin);
            // Belge numaraları düz hâliyle yanıtın HİÇBİR yerinde yok (alan adı değişse bile yakalanır).
            Assert.DoesNotContain(Ehliyet, metin, StringComparison.Ordinal);
            Assert.DoesNotContain(Pasaport, metin, StringComparison.Ordinal);
            // Blazor ekranıyla aynı salt-okunur alanlar.
            Assert.Equal("05321112233", S(j, "cepTel"));
            Assert.Equal("ayse@ornek.test", S(j, "email"));
            Assert.Equal("Atatürk Cd. No:5", S(j, "adres"));
            Assert.Equal("İzmir", S(j, "il"));
            Assert.Equal("Karşıyaka", S(j, "ilce"));
            Assert.Equal("Türk Ehliyetli", S(j, "musteriTipi"));
            Assert.Equal("B", S(j, "ehliyetSinifi"));
            Assert.Equal("İzmir", S(j, "ehliyetYeri"));
            Assert.Equal("TR", S(j, "ehliyetUlke"));
            Assert.Equal("Ankara", S(j, "pasaportYeri"));
            Assert.Equal(new DateTimeOffset(2015, 6, 1, 0, 0, 0, TimeSpan.Zero), j.GetProperty("ehliyetTarihi").GetDateTimeOffset());
            Assert.Equal(5000m, j.GetProperty("riskLimiti").GetDecimal());
            Assert.True(j.GetProperty("karaListe").GetBoolean());
            Assert.True(j.GetProperty("uyari").GetBoolean());
            Assert.Equal("Geç iade geçmişi", S(j, "uyariNedeni"));
        }

        // Detay ucu PII'yi hâlâ yalnız ad düzeyinde verir (F4.1 kararı değişmedi).
        var (detay, detayMetin) = await JsonMetin(await op.C.GetAsync($"{Kira}/{id}"));
        TcHicbirBicimdeYok(detay, detayMetin);
    }

    /// <summary>
    /// TC'nin HİÇBİR biçimi yanıtta yok: düz numara metinde yok; TC/kimlik alanı yok; kimlik (GUID) ve plaka
    /// dışındaki hiçbir metin değerinde son 4 hane ("0146") geçmiyor (maskeli biçim de yakalanır). GUID/plaka hariç
    /// tutulur çünkü rastgele üretilirler — tesadüfi "0146" geçişi testi kararsız yapmasın.
    /// </summary>
    private static void TcHicbirBicimdeYok(JsonElement kok, string metin)
    {
        Assert.DoesNotContain(Tc, metin, StringComparison.Ordinal);
        Assert.DoesNotContain("*0146", metin, StringComparison.Ordinal);
        foreach (var (ad, deger) in MetinDegerleri(kok))
        {
            // "…Utc" alanları "tc" içerir → yalnız "tc" ile BAŞLAYAN ya da "kimlik" geçen alan adı TC alanı sayılır.
            Assert.False(ad.StartsWith("tc", StringComparison.OrdinalIgnoreCase)
                         || ad.Contains("kimlik", StringComparison.OrdinalIgnoreCase), $"TC alanı yanıtta: {ad}");
            if (deger is null || Guid.TryParse(deger, out _) || ad is "plaka" or "etiket") continue;
            Assert.False(deger.Contains("0146", StringComparison.Ordinal), $"TC son 4 hanesi '{ad}' alanında: {deger}");
        }
    }

    private static IEnumerable<(string Ad, string? Deger)> MetinDegerleri(JsonElement e, string ad = "")
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    yield return (p.Name, null);
                    foreach (var x in MetinDegerleri(p.Value, p.Name)) yield return x;
                }
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray().SelectMany(v => MetinDegerleri(v, ad))) yield return x;
                break;
            case JsonValueKind.String:
                yield return (ad, e.GetString());
                break;
        }
    }

    [Fact]
    public async Task Musteri_ozeti_kapsam_403_baska_kiraci_404_izinsiz_403()
    {
        var o = await OrtamKurAsync();
        var admin = await GirisAsync(o, Kim.Admin);
        var musteri = await PiiliMusteriAsync(o, admin);
        var arac = await AracAsync(o);
        var op = await GirisAsync(o, Kim.OperatorA);
        var id = await KiraAcAsync(op, musteri, arac, Simdi().AddHours(3));

        // Başka şubenin operatörü: kira kapsamı dışında → 403 (müşteri verisi sızmaz).
        await ProblemBekle(await (await GirisAsync(o, Kim.OperatorB)).C.GetAsync($"{Kira}/{id}/musteri-ozet"),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
        // İzinsiz (ne OperationsWrite ne FinanceWrite) → 403.
        await ProblemBekle(await (await GirisAsync(o, Kim.Yasakli)).C.GetAsync($"{Kira}/{id}/musteri-ozet"),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
        // Başka kiracı → 404; olmayan kira → 404.
        var diger = await OrtamKurAsync();
        var d = await GirisAsync(diger, Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await d.C.GetAsync($"{Kira}/{id}/musteri-ozet")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await op.C.GetAsync($"{Kira}/{Guid.NewGuid()}/musteri-ozet")).StatusCode);
    }

    // ------------------------------------------------------------ KVKK Anonim* bayrakları (#262 adversarial M1)

    private static readonly string[] BelgeAlanlari =
        ["ehliyetNoMaskeli", "pasaportNoMaskeli", "ehliyetSinifi", "ehliyetTarihi", "ehliyetYeri", "ehliyetUlke", "pasaportYeri"];
    private static readonly string[] AdresAlanlari = ["adres", "il", "ilce"];

    private static bool Bos(JsonElement e, string ad) => e.GetProperty(ad).ValueKind == JsonValueKind.Null;

    /// <summary>
    /// TEK kural (MusteriGorunumu) hem özeti hem detayı (taraf adı + paylaşım barı + hazır mesaj) kapsar. Her bayrak
    /// TEK BAŞINA yalnız kendi grubunu boşaltır; hepsi işaretliyken hiçbir kişisel değer iki yanıtta da geçmez.
    /// Beklenenler elle: ad "Ayşe Kaya", tel "05321112233", e-posta "ayse@ornek.test", adres "Atatürk Cd. No:5".
    /// </summary>
    [Fact]
    public async Task Anonim_bayraklari_tek_kuraldan_ozet_detay_paylasim_mesaj_her_bayrak_yalniz_kendi_grubu()
    {
        var o = await OrtamKurAsync();
        var admin = await GirisAsync(o, Kim.Admin);
        var op = await GirisAsync(o, Kim.OperatorA); // paylaşım barı yalnız OperationsWrite ile dolar
        string[] senaryolar = ["Yok", "AnonimAd", "AnonimTelefon", "AnonimMail", "AnonimAdres", "AnonimBelge", "Hepsi"];

        foreach (var sen in senaryolar)
        {
            bool Var(string b) => sen == b || sen == "Hepsi";
            // TC her caride boş: aynı TC ikinci caride 409 olur; TC zaten hiçbir yüzeyde dönmüyor.
            var musteri = await PiiliMusteriAsync(o, admin, m =>
            {
                m.AnonimAd = Var("AnonimAd");
                m.AnonimTelefon = Var("AnonimTelefon");
                m.AnonimMail = Var("AnonimMail");
                m.AnonimAdres = Var("AnonimAdres");
                m.AnonimBelge = Var("AnonimBelge");
                m.AnonimTc = sen == "Hepsi";
            }, tc: null);
            var id = await KiraAcAsync(op, musteri, await AracAsync(o), Simdi().AddHours(4));
            var (oz, ozMetin) = await JsonMetin(await op.C.GetAsync($"{Kira}/{id}/musteri-ozet"));
            var (d, dMetin) = await JsonMetin(await op.C.GetAsync($"{Kira}/{id}"));
            var bar = d.GetProperty("paylasim");
            var mesaj = S(bar, "mesaj")!;
            var no = d.GetProperty("kira").GetProperty("sozlesmeNo").GetString();

            // Ad
            if (Var("AnonimAd"))
            {
                Assert.True(Bos(oz, "ad"), $"{sen}: özet ad dolu");
                Assert.Equal("Anonim müşteri", d.GetProperty("musteri").GetProperty("ad").GetString());
                Assert.StartsWith($"Sayın müşterimiz, {no} nolu", mesaj);
                Assert.DoesNotContain("Ayşe", mesaj, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("Ayşe Kaya", S(oz, "ad"));
                Assert.Equal("Ayşe Kaya", d.GetProperty("musteri").GetProperty("ad").GetString());
                Assert.StartsWith($"Sayın Ayşe Kaya, {no} nolu", mesaj);
            }
            // Telefon (özet + WhatsApp ön-doldurması)
            Assert.Equal(Var("AnonimTelefon") ? null : "05321112233", S(oz, "cepTel"));
            Assert.Equal(Var("AnonimTelefon") ? null : "05321112233", S(bar, "musteriTel"));
            // E-posta (özet + Gmail ön-doldurması)
            Assert.Equal(Var("AnonimMail") ? null : "ayse@ornek.test", S(oz, "email"));
            Assert.Equal(Var("AnonimMail") ? null : "ayse@ornek.test", S(bar, "musteriEmail"));
            // Adres grubu
            foreach (var a in AdresAlanlari)
                Assert.True(Bos(oz, a) == Var("AnonimAdres"), $"{sen}: {a} beklenmedik ({oz})");
            // Belge grubu: numara + üst bilgi birlikte
            foreach (var a in BelgeAlanlari)
                Assert.True(Bos(oz, a) == Var("AnonimBelge"), $"{sen}: {a} beklenmedik ({oz})");
            // Anonim olmayan alanlar her senaryoda korunur.
            Assert.Equal("Türk Ehliyetli", S(oz, "musteriTipi"));
            Assert.Equal(5000m, oz.GetProperty("riskLimiti").GetDecimal());

            if (sen == "Hepsi")
            {
                foreach (var kisisel in new[] { "Ayşe", "Kaya", "0532", "ayse@", "Atatürk", "Karşıyaka", "6543", "4567", "Ankara" })
                {
                    Assert.DoesNotContain(kisisel, ozMetin, StringComparison.Ordinal);
                    Assert.DoesNotContain(kisisel, dMetin, StringComparison.Ordinal);
                }
            }
        }
    }

    // ------------------------------------------------------------ detay: sunucu toplamları + paylaşım metni

    [Fact]
    public async Task Detay_toplamlari_sunucuda_ek_hizmet_ve_iptal_haric_ceza_paylasim_mesaji_blazor_metni()
    {
        var o = await OrtamKurAsync();
        var arac = await AracAsync(o);
        var arac2 = await AracAsync(o);
        var admin = await GirisAsync(o, Kim.Admin);
        // 2026-03-10 21:30 UTC = 11.03.2026 00:30 İstanbul (UTC günü 10'u — metin İSTANBUL gününü yazmalı).
        var bas = new DateTimeOffset(2026, 3, 10, 21, 30, 0, TimeSpan.Zero);
        var id = await KiraAcAsync(admin, o.MusteriId, arac, bas, gps: o.EkHizmetId);
        var baska = await KiraAcAsync(admin, o.MusteriId, arac2, bas);

        await VeriYazAsync(o.TenantId, db =>
        {
            db.Penalties.Add(new Penalty { No = "CZ-P1", CezaTuru = "Hız", RentalId = id, Tutar = 250m, Kalan = 250m, Durum = CezaDurum.Yeni });
            db.Penalties.Add(new Penalty { No = "CZ-P2", CezaTuru = "Park", RentalId = id, Tutar = 90m, Kalan = 90m, Durum = CezaDurum.Iptal });
            db.Penalties.Add(new Penalty { No = "CZ-P3", CezaTuru = "Park", RentalId = baska, Tutar = 40m, Kalan = 40m, Durum = CezaDurum.Yeni });
        });

        var d = await Json(await admin.C.GetAsync($"{Kira}/{id}"));
        var t = d.GetProperty("toplamlar");
        Assert.Equal(60m, t.GetProperty("ekHizmetToplam").GetDecimal());   // GPS 50 net + 10 KDV
        Assert.Equal(250m, t.GetProperty("cezaToplam").GetDecimal());       // 250 (iptal 90 ve başka kiranın 40'ı hariç)
        Assert.Equal(420m, d.GetProperty("kira").GetProperty("genelToplam").GetDecimal());

        var bar = d.GetProperty("paylasim");
        var no = d.GetProperty("kira").GetProperty("sozlesmeNo").GetString();
        Assert.Equal($"Kira Sözleşmesi {no}", S(bar, "konu"));
        Assert.Equal($"Sayın Deniz Yılmaz, {no} nolu kira sözleşmeniz: 11.03.2026 - 14.03.2026, genel toplam 420,00 TL.",
            S(bar, "mesaj"));

        // Ceza/ek hizmetsiz kira: toplamlar sıfır (null değil).
        var t2 = (await Json(await admin.C.GetAsync($"{Kira}/{baska}"))).GetProperty("toplamlar");
        Assert.Equal(0m, t2.GetProperty("ekHizmetToplam").GetDecimal());
        Assert.Equal(40m, t2.GetProperty("cezaToplam").GetDecimal());
    }

    // ------------------------------------------------------------ ek hizmet kataloğu

    [Fact]
    public async Task Ek_hizmet_katalogu_aktif_ve_sys_haric_fiyat_kdv_tanimdan_muhasebe_403()
    {
        var o = await OrtamKurAsync();
        await VeriYazAsync(o.TenantId, db =>
        {
            db.EkHizmetTanimlari.Add(new EkHizmetTanim { Kod = "BEBEK", Ad = "Bebek Koltuğu", BirimUcret = 75.5m, KdvOrani = 0.10m, Aktif = true, MaxGun = 30, Aciklama = "0-4 yaş" });
            db.EkHizmetTanimlari.Add(new EkHizmetTanim { Kod = "ESKI", Ad = "Eski Hizmet", BirimUcret = 10m, Aktif = false });
            db.EkHizmetTanimlari.Add(new EkHizmetTanim { Kod = "SYS-GENC", Ad = "Genç Sürücü", BirimUcret = 30m, Aktif = true });
        });
        var op = await GirisAsync(o, Kim.OperatorA);
        var j = await Json(await op.C.GetAsync($"{Kira}/ek-hizmet-katalogu"));
        var ogeler = j.GetProperty("ogeler").EnumerateArray().ToList();
        // Ad sırasıyla (tr-TR): Bebek Koltuğu, Navigasyon. Pasif ve SYS-* yok.
        Assert.Equal(["Bebek Koltuğu", "Navigasyon"], ogeler.Select(x => S(x, "ad")!).ToArray());
        Assert.Equal(2, j.GetProperty("toplam").GetInt32());
        var bebek = ogeler[0];
        Assert.Equal("BEBEK", S(bebek, "kod"));
        Assert.Equal(75.5m, bebek.GetProperty("birimUcret").GetDecimal());
        Assert.Equal(0.10m, bebek.GetProperty("kdvOrani").GetDecimal());
        Assert.Equal(30, bebek.GetProperty("maxGun").GetInt32());
        Assert.Equal("0-4 yaş", S(bebek, "aciklama"));
        Assert.Equal(o.EkHizmetId, ogeler[1].GetProperty("id").GetGuid());

        await ProblemBekle(await (await GirisAsync(o, Kim.Muhasebe)).C.GetAsync($"{Kira}/ek-hizmet-katalogu"),
            HttpStatusCode.Forbidden, UiHata.YetkiYok);
    }

    // ------------------------------------------------------------ kimlikle seçim etiketi

    [Fact]
    public async Task Secim_kimlikle_musteri_ve_arac_etiketi_pii_yok_kapsam_403_baska_kiraci_404()
    {
        var o = await OrtamKurAsync();
        var admin = await GirisAsync(o, Kim.Admin);
        var musteri = await PiiliMusteriAsync(o, admin);
        var aracA = await AracAsync(o, "SubeA");
        var aracB = await AracAsync(o, "SubeB");
        var op = await GirisAsync(o, Kim.OperatorA);

        var (m, metin) = await JsonMetin(await op.C.GetAsync($"{V1}/secim/musteri/{musteri}"));
        Assert.Equal(["etiket", "id", "tip"], m.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal("Ayşe Kaya", S(m, "etiket"));
        Assert.Equal("Bireysel", S(m, "tip"));
        Assert.DoesNotContain(Tc, metin, StringComparison.Ordinal);
        Assert.DoesNotContain("0532", metin, StringComparison.Ordinal);

        var a = await Json(await op.C.GetAsync($"{V1}/secim/arac/{aracA}"));
        Assert.Equal(["durum", "etiket", "grup", "id", "plaka"], a.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.StartsWith("34 PAR ", S(a, "plaka"));
        Assert.Equal($"{S(a, "plaka")} — Fiat Egea", S(a, "etiket"));
        Assert.Equal("C", S(a, "grup"));
        Assert.Equal("Musait", S(a, "durum"));

        // Şube kapsamı: A operatörü B şubesinin aracını çözemez (403); Admin çözer.
        await ProblemBekle(await op.C.GetAsync($"{V1}/secim/arac/{aracB}"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        Assert.Equal(HttpStatusCode.OK, (await admin.C.GetAsync($"{V1}/secim/arac/{aracB}")).StatusCode);

        // Olmayan / başka kiracının kaydı → 404 (varlık sızmaz).
        Assert.Equal(HttpStatusCode.NotFound, (await op.C.GetAsync($"{V1}/secim/musteri/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await op.C.GetAsync($"{V1}/secim/arac/{Guid.NewGuid()}")).StatusCode);
        var diger = await OrtamKurAsync();
        var d = await GirisAsync(diger, Kim.Admin);
        Assert.Equal(HttpStatusCode.NotFound, (await d.C.GetAsync($"{V1}/secim/musteri/{musteri}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await d.C.GetAsync($"{V1}/secim/arac/{aracA}")).StatusCode);

        // Seçim uçlarının izni (OperationsWrite): Muhasebe ve izinsiz kullanıcı 403.
        foreach (var kim in new[] { Kim.Muhasebe, Kim.Yasakli })
        {
            var s = await GirisAsync(o, kim);
            await ProblemBekle(await s.C.GetAsync($"{V1}/secim/musteri/{musteri}"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
            await ProblemBekle(await s.C.GetAsync($"{V1}/secim/arac/{aracA}"), HttpStatusCode.Forbidden, UiHata.YetkiYok);
        }
    }
}
