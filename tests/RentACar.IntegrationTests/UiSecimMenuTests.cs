using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Authorization;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F1.6 — <c>/api/ui/v1/secim/*</c> ve <c>/api/ui/v1/menu</c> GERÇEK Web boru hattında.
/// Her test kendi firmasını kurar; kullanıcı adları ve şifreler ÇALIŞMA ANINDA rastgele üretilir
/// (depoda kimlik bilgisi yok). BAĞIMSIZ ORACLE: beklenen kayıtlar/alan kümeleri elle yazılmış sabitlerdir.
/// </summary>
[Collection("web")]
public sealed class UiSecimMenuTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    // ------------------------------------------------------------ ortam

    private enum Kim { Admin, OperatorA, OperatorB, Muhasebe, OperatorYasakli, OperatorEkRapor, AdminYasakli }

    private sealed class Ortam
    {
        public required Guid TenantId { get; init; }
        public required string Kod { get; init; }
        public required string Sifre { get; init; }
        public required Dictionary<Kim, string> Kullanicilar { get; init; }
    }

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Firma + her rolden kullanıcı (rastgele ad/şifre) + kullanıcı-bazlı istisnalar. Owner bağlantısı (platform tabloları).</summary>
    private async Task<Ortam> OrtamKurAsync(bool pilot = true, bool webSitesi = false)
    {
        var ortam = new Ortam
        {
            TenantId = Guid.NewGuid(),
            Kod = Rastgele("f16"),
            Sifre = WebFixture.RastgeleParola(),
            Kullanicilar = Enum.GetValues<Kim>().ToDictionary(k => k, _ => Rastgele("u")),
        };

        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = ortam.TenantId, Code = ortam.Kod, Name = ortam.Kod, IsActive = true, WebSitesiModulu = webSitesi });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>(); // girişin doğruladığı hasher
            foreach (var (kim, ad) in ortam.Kullanicilar)
            {
                var (rol, sube) = kim switch
                {
                    Kim.Admin or Kim.AdminYasakli => (UserRole.Admin, (string?)null),
                    Kim.OperatorA or Kim.OperatorYasakli or Kim.OperatorEkRapor => (UserRole.Operator, "SubeA"),
                    Kim.OperatorB => (UserRole.Operator, "SubeB"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = ortam.TenantId, UserName = ad, DisplayName = ad, Rol = rol, AtanmisSube = sube, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, ortam.Sifre);
                db.Users.Add(u);
                if (kim == Kim.OperatorYasakli)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = ortam.TenantId, UserId = u.Id, Izin = "OperationsWrite", Ver = false });
                if (kim == Kim.OperatorEkRapor)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = ortam.TenantId, UserId = u.Id, Izin = "ViewReports", Ver = true });
                if (kim == Kim.AdminYasakli)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = ortam.TenantId, UserId = u.Id, Izin = "ManageUsers", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(ortam.TenantId, pilot);
        return ortam;
    }

    /// <summary>Kiracı verisi GERÇEK uygulama rolüyle (RLS'li racar_app) yazılır.</summary>
    private async Task VeriYazAsync(Guid tenantId, Action<AppDbContext> yaz)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        yaz(db);
        await db.SaveChangesAsync();
    }

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<HttpClient> GirisAsync(Ortam o, Kim kim)
    {
        var c = fx.Web.Istemci();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var belirtec = CerezDegeri(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", belirtec);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({kim}): {await r.Content.ReadAsStringAsync()}");
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.Clone();
    }

    private static async Task ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(kod, JsonDocument.Parse(metin).RootElement.GetProperty("kod").GetString());
    }

    private static List<string> Etiketler(JsonElement dizi) => dizi.EnumerateArray().Select(e => e.GetProperty("etiket").GetString()!).ToList();

    private static void AlanKumesi(JsonElement dizi, params string[] beklenen)
    {
        Assert.True(dizi.GetArrayLength() > 0, "Boş liste — alan kümesi sınanamadı.");
        foreach (var e in dizi.EnumerateArray())
            Assert.Equal(beklenen.OrderBy(x => x, StringComparer.Ordinal),
                e.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------ müşteri: Türkçe arama, sınır, PII

    [Fact]
    public async Task Musteri_turkce_katlamali_arama_ve_pii_yok()
    {
        var o = await OrtamKurAsync();
        await VeriYazAsync(o.TenantId, db =>
        {
            db.Customers.Add(new Customer { Tip = CariType.Kurumsal, Unvan = "IŞIK Lojistik", CepTel = "05321112233", Email = "gizli-f16@ornek.test", Adres = "Gizli Sokak 7" });
            db.Customers.Add(new Customer { Tip = CariType.Bireysel, Ad = "Ayşe", Soyad = "Işık", CepTel = "05329998877" });
            db.Customers.Add(new Customer { Tip = CariType.Bireysel, Ad = "Ali", Soyad = "Veli" });
        });
        var c = await GirisAsync(o, Kim.Admin);

        foreach (var q in new[] { "ışık", "IŞIK", "isik", "Işık" })
        {
            var sonuc = await Json(await c.GetAsync($"{V1}/secim/musteri?q={Uri.EscapeDataString(q)}"));
            Assert.Equal(new[] { "Ayşe Işık", "IŞIK Lojistik" }, Etiketler(sonuc).Order(StringComparer.Ordinal).ToArray());
        }

        var ham = await c.GetAsync($"{V1}/secim/musteri");
        var metin = await ham.Content.ReadAsStringAsync();
        var tum = JsonDocument.Parse(metin).RootElement;
        Assert.Equal(3, tum.GetArrayLength());
        AlanKumesi(tum, "id", "etiket", "tip");
        Assert.DoesNotContain("0532", metin);
        Assert.DoesNotContain("gizli-f16", metin);
        Assert.DoesNotContain("Gizli Sokak", metin);
        Assert.True(ham.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task Limit_en_cok_20_varsayilan_20()
    {
        var o = await OrtamKurAsync();
        await VeriYazAsync(o.TenantId, db =>
        {
            for (var i = 1; i <= 25; i++)
            {
                db.Customers.Add(new Customer { Tip = CariType.Bireysel, Ad = $"Sınır{i:00}", Soyad = "Müşteri" });
                db.Vehicles.Add(new Vehicle { Plaka = $"34 LMT {i:00}", Sube = "SubeA", Durum = VehicleStatus.Musait });
            }
        });
        var c = await GirisAsync(o, Kim.Admin);

        foreach (var uc in new[] { "musteri", "arac" })
        {
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{uc}?limit=500"))).GetArrayLength());
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{uc}"))).GetArrayLength());
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{uc}?limit=0"))).GetArrayLength());
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{uc}?limit=-3"))).GetArrayLength());
            Assert.Equal(5, (await Json(await c.GetAsync($"{V1}/secim/{uc}?limit=5"))).GetArrayLength());
        }
        // q ile daraltma: "sinir0" → Sınır01..Sınır09 (9 kayıt); "lmt 2" → 20..25 (6 kayıt).
        Assert.Equal(9, (await Json(await c.GetAsync($"{V1}/secim/musteri?q=sinir0"))).GetArrayLength());
        Assert.Equal(6, (await Json(await c.GetAsync($"{V1}/secim/arac?q=LMT%202"))).GetArrayLength());
    }

    // ------------------------------------------------------------ yetki / pilot

    [Fact]
    public async Task Izinsiz_rol_403_yetki_yok_kullanici_yasagi_da_uygulanir()
    {
        var o = await OrtamKurAsync();
        var muh = await GirisAsync(o, Kim.Muhasebe);
        foreach (var uc in new[] { "arac", "lokasyon", "personel", "sube", "ek-hizmet", "sigorta-urunu",
                     "rezervasyon-kaynagi", "ozel-kod", "belge-sablonu", "arac-grubu" })
            await ProblemBekle(await muh.GetAsync($"{V1}/secim/{uc}"), HttpStatusCode.Forbidden, "yetki_yok");
        // F4.4: müşteri ve kur Muhasebe'ye (FinanceWrite) de açık — kira formunun sabit finans paneli.
        Assert.Equal(HttpStatusCode.OK, (await muh.GetAsync($"{V1}/secim/musteri")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await muh.GetAsync($"{V1}/secim/kur")).StatusCode);

        // Operatör ama OperationsWrite kullanıcı-bazlı YASAK (FinanceWrite de yok): iki "herhangi biri" ucu da 403.
        var yasakli = await GirisAsync(o, Kim.OperatorYasakli);
        foreach (var uc in new[] { "musteri", "kur", "arac" })
            await ProblemBekle(await yasakli.GetAsync($"{V1}/secim/{uc}"), HttpStatusCode.Forbidden, "yetki_yok");

        var op = await GirisAsync(o, Kim.OperatorA);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"{V1}/secim/musteri")).StatusCode);

        var anonim = await fx.Web.Istemci().GetAsync($"{V1}/secim/musteri");
        await ProblemBekle(anonim, HttpStatusCode.Unauthorized, "oturum_yok");
    }

    /// <summary>F4.4 yapısal kilit: seçim uçlarının etkin izin kapısı. Yalnız <c>musteri</c> ve <c>kur</c>
    /// arama uçları "OperationsWrite veya FinanceWrite"; geri kalan hepsi (kimlikle etiket uçları dahil) OperationsWrite
    /// (genişleme sessizce yayılmasın).</summary>
    [Fact]
    public void Secim_uclari_izin_haritasi()
    {
        var uclar = fx.Web.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith(V1 + "/secim/", StringComparison.Ordinal))
            .ToDictionary(e => "/" + e.RoutePattern.RawText!.TrimStart('/'));
        // 12 arama + F4.3b kimlikle etiket (musteri/{id}, arac/{id}) — ikisi OW
        // + F9.1 üç uç, hepsi TEK izin OperationsWrite (aşağıdaki else dalı birebir doğrular):
        //   sigorta-sirketi → Blazor poliçe formu (/regulasyon/sigorta, OW grubu) firma listesi;
        //   tarife-grubu → Blazor tarife formu (/tarifeler, izin:OperationsWrite) grup listesi;
        //   sigorta-policesi → Blazor zeyil formu (/regulasyon/zeyil, OW grubu) poliçe listesi (araç şubesi kapsamlı).
        Assert.Equal(17, uclar.Count);
        foreach (var f91 in new[] { "sigorta-sirketi", "tarife-grubu", "sigorta-policesi" })
            Assert.Contains(V1 + "/secim/" + f91, uclar.Keys);
        foreach (var (rota, e) in uclar)
        {
            var biri = e.Metadata.GetMetadata<RentACar.Web.Identity.IzinlerdenBiriMetadata>();
            var tek = e.Metadata.GetMetadata<RentACar.Web.Identity.IzinMetadata>();
            if (rota is V1 + "/secim/musteri" or V1 + "/secim/kur")
            {
                Assert.Null(tek);
                Assert.Equal(new[] { Permission.OperationsWrite, Permission.FinanceWrite }, biri!.Izinler);
            }
            else
            {
                Assert.Null(biri);
                Assert.Equal(Permission.OperationsWrite, tek!.Izin);
            }
        }
    }

    [Fact]
    public async Task Pilot_olmayan_firmada_secim_ve_menu_403_pilot_degil()
    {
        var o = await OrtamKurAsync(pilot: false);
        var c = await GirisAsync(o, Kim.Admin);
        await ProblemBekle(await c.GetAsync($"{V1}/secim/musteri"), HttpStatusCode.Forbidden, "pilot_degil");
        await ProblemBekle(await c.GetAsync($"{V1}/menu"), HttpStatusCode.Forbidden, "pilot_degil");
    }

    // ------------------------------------------------------------ şube kapsamı + alan kümeleri

    [Fact]
    public async Task Sube_kapsami_arac_personel_sube_ve_alan_kumeleri()
    {
        var o = await OrtamKurAsync();
        var bugun = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        await VeriYazAsync(o.TenantId, db =>
        {
            db.Branches.Add(new Branch { Kod = "SA", Ad = "SubeA" });
            db.Branches.Add(new Branch { Kod = "SB", Ad = "SubeB" });
            db.Vehicles.Add(new Vehicle { Plaka = "34 SBA 01", Marka = "Fiat", Tip = "Egea", Grup = "C", Sube = "SubeA", Durum = VehicleStatus.Musait, SasiNo = "GIZLISASI1" });
            db.Vehicles.Add(new Vehicle { Plaka = "06 SBB 01", Marka = "Renault", Tip = "Clio", Grup = "B", Sube = "SubeB", Durum = VehicleStatus.Musait });
            db.Personeller.Add(new Personel { Kod = "P1", Ad = "Ahmet", Soyad = "Asube", Sube = "SubeA", Aktif = true, CepTel = "05551112233" });
            db.Personeller.Add(new Personel { Kod = "P2", Ad = "Mehmet", Soyad = "Bsube", Sube = "SubeB", Aktif = true });
            db.Locations.Add(new Location { Kod = "LA", Ad = "Havalimanı A", Sube = "SubeA", Aktif = true, Telefon = "02125550000" });
            db.Locations.Add(new Location { Kod = "LB", Ad = "Otogar B", Sube = "SubeB", Aktif = true });
            db.EkHizmetTanimlari.Add(new EkHizmetTanim { Kod = "BEBEK", Ad = "Bebek Koltuğu", BirimUcret = 100m, Aktif = true });
            db.CoverageProducts.Add(new CoverageProduct { Kod = "MINI", Ad = "Mini Hasar Sigortası", Aktif = true });
            db.ReservationSources.Add(new ReservationSource { Kod = "WEB", Ad = "Web Sitesi", Aktif = true });
            db.ReservationSources.Add(new ReservationSource { Kod = "ESKI", Ad = "Eski Kaynak", Aktif = false });
            db.CustomCodes.Add(new CustomCode { Kod = "VIP", Ad = "VIP Müşteri", Aktif = true });
            db.BelgeSablonlari.Add(new BelgeSablon { BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Standart Sözleşme", Aktif = true });
            db.BelgeSablonlari.Add(new BelgeSablon { BelgeTuru = BelgeTuru.Fatura, Ad = "Fatura Şablonu", Aktif = true });
            db.VehicleGroups.Add(new VehicleGroup { Kod = "ECO", Ad = "Ekonomik", Aktif = true });
        });
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            if (!await db.KurKayitlari.AnyAsync(k => k.Tarih == bugun && k.Kod == "USD"))
                db.KurKayitlari.Add(new KurKaydi { Tarih = bugun, Kod = "USD", Ad = "ABD DOLARI", Birim = 1, ForexAlis = 40m, ForexSatis = 41m });
            await db.SaveChangesAsync();
        }

        var op = await GirisAsync(o, Kim.OperatorA);
        var admin = await GirisAsync(o, Kim.Admin);

        // Araç: operatör yalnız kendi şubesi; admin hepsi.
        var opArac = await Json(await op.GetAsync($"{V1}/secim/arac"));
        Assert.Equal(new[] { "34 SBA 01 — Fiat Egea" }, Etiketler(opArac));
        AlanKumesi(opArac, "id", "etiket", "plaka", "grup", "durum");
        Assert.Equal("C", opArac[0].GetProperty("grup").GetString());
        Assert.Equal(2, (await Json(await admin.GetAsync($"{V1}/secim/arac"))).GetArrayLength());

        // Personel ve şube: operatör yalnız kendi şubesi.
        var opPersonel = await Json(await op.GetAsync($"{V1}/secim/personel"));
        Assert.Equal(new[] { "Ahmet Asube" }, Etiketler(opPersonel));
        AlanKumesi(opPersonel, "id", "etiket", "kod");
        Assert.Equal(2, (await Json(await admin.GetAsync($"{V1}/secim/personel"))).GetArrayLength());
        Assert.Equal(new[] { "SubeA" }, Etiketler(await Json(await op.GetAsync($"{V1}/secim/sube"))));
        Assert.Equal(2, (await Json(await admin.GetAsync($"{V1}/secim/sube"))).GetArrayLength());

        // Lokasyon bilinçli kapsamsız (dönüş ofisi başka şube olabilir) — subeId taşır.
        var lok = await Json(await op.GetAsync($"{V1}/secim/lokasyon"));
        Assert.Equal(2, lok.GetArrayLength());
        AlanKumesi(lok, "id", "etiket", "kod", "subeId");
        Assert.Equal(new[] { "Havalimanı A" }, Etiketler(await Json(await op.GetAsync($"{V1}/secim/lokasyon?q=HAVALIMANI"))));

        // Tanımlar: yalnız aktif; genel alan kümesi.
        var kaynak = await Json(await op.GetAsync($"{V1}/secim/rezervasyon-kaynagi"));
        Assert.Equal(new[] { "Web Sitesi" }, Etiketler(kaynak));
        AlanKumesi(kaynak, "id", "etiket", "kod");
        AlanKumesi(await Json(await op.GetAsync($"{V1}/secim/ek-hizmet?q=bebek")), "id", "etiket", "kod");
        AlanKumesi(await Json(await op.GetAsync($"{V1}/secim/sigorta-urunu")), "id", "etiket", "kod");
        AlanKumesi(await Json(await op.GetAsync($"{V1}/secim/ozel-kod")), "id", "etiket", "kod");
        AlanKumesi(await Json(await op.GetAsync($"{V1}/secim/arac-grubu")), "id", "etiket", "kod");
        Assert.Equal(new[] { "Standart Sözleşme" }, Etiketler(await Json(await op.GetAsync($"{V1}/secim/belge-sablonu?tur=KiraSozlesmesi"))));
        Assert.Equal(2, (await Json(await op.GetAsync($"{V1}/secim/belge-sablonu"))).GetArrayLength());

        var kur = await Json(await op.GetAsync($"{V1}/secim/kur?q=usd"));
        AlanKumesi(kur, "id", "etiket", "birim", "dovizAlis", "dovizSatis", "tarih");
        Assert.Equal("USD", kur[0].GetProperty("id").GetString());
        Assert.Equal(41m, kur[0].GetProperty("dovizSatis").GetDecimal());

        // PII sızmaz (telefon, şasi no).
        foreach (var uc in new[] { "arac", "personel", "lokasyon" })
        {
            var metin = await (await admin.GetAsync($"{V1}/secim/{uc}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("0555", metin);
            Assert.DoesNotContain("0212", metin);
            Assert.DoesNotContain("GIZLISASI", metin);
        }
    }

    // ------------------------------------------------------------ menü

    private static async Task<(HashSet<string> Rotalar, JsonElement Govde)> MenuAsync(HttpClient c)
    {
        var j = await Json(await c.GetAsync($"{V1}/menu"));
        var rotalar = j.GetProperty("ogeler").EnumerateArray().Select(e => e.GetProperty("rota").GetString()!).ToHashSet();
        return (rotalar, j);
    }

    [Fact]
    public async Task Menu_rol_ve_izne_gore_suzulur()
    {
        var o = await OrtamKurAsync();

        var (admin, adminGovde) = await MenuAsync(await GirisAsync(o, Kim.Admin));
        // F4.6: Panel, Kiralar, Yeni Kira yeni arayüzün (sahip spa, rota /app/…).
        foreach (var r in new[] { "/app/panel", "/app/kiralar/yeni", "/app/araclar", "/app/araclar/detayli", "/kasa", "/app/raporlar/karlilik", "/app/raporlar/personel-calisma", "/tarife-aktar", "/ayarlar", "/vade", "/bildirimler" })
            Assert.Contains(r, admin);
        Assert.DoesNotContain("/web-sitesi", admin);   // modül kapalı
        Assert.DoesNotContain("/site-icerik", admin);
        var ogeler = adminGovde.GetProperty("ogeler").EnumerateArray().ToList();
        AlanKumesi(adminGovde.GetProperty("ogeler"), "rota", "etiket", "grup", "sira", "sahip", "rozetKodu", "hizliBaglanti");
        Assert.All(ogeler, e => Assert.Equal(
            e.GetProperty("rota").GetString()!.StartsWith("/app/", StringComparison.Ordinal) ? "spa" : "blazor",
            e.GetProperty("sahip").GetString()));
        Assert.Equal(50, ogeler.Count(e => e.GetProperty("sahip").GetString() == "spa")); // F4.6: 3, F5.4: +8, F6.4: +14 (Araçlar 12 + Tanımlar 2), F10.3: +25 (Raporlar)
        Assert.DoesNotContain(ogeler, e => e.GetProperty("rota").GetString()!.StartsWith("/raporlar/", StringComparison.Ordinal));
        Assert.Equal(3, ogeler.Count(e => e.GetProperty("hizliBaglanti").GetBoolean()));
        var siralar = ogeler.Select(e => e.GetProperty("sira").GetInt32()).ToList();
        Assert.Equal(siralar.Order(), siralar);         // sıralı döner
        // Gelen Talepler CRM grubunda modülden bağımsız görünür; Web Sitesi grubundaki kopyası gizli.
        Assert.Single(ogeler, e => e.GetProperty("rota").GetString() == "/gelen-talepler");
        Assert.True(adminGovde.GetProperty("rozetler").TryGetProperty("okunmamis-bildirim", out _));
        Assert.False(adminGovde.GetProperty("rozetler").TryGetProperty("yeni-talep", out _)); // modül kapalı → sorulmaz

        var (op, _) = await MenuAsync(await GirisAsync(o, Kim.OperatorA));
        foreach (var r in new[] { "/app/panel", "/app/kiralar/yeni", "/app/rezervasyonlar", "/app/musaitlik", "/app/takvim", "/app/araclar", "/app/arac-durum", "/app/baf", "/app/segmentler", "/app/kiralar", "/cariler", "/vade", "/dokumanlar" })
            Assert.Contains(r, op);
        foreach (var r in new[] { "/app/araclar/detayli", "/app/musteri-taksit", "/vehicles", "/crm", "/maliyet-hesapla", "/tarife-aktar", "/kasa", "/kurlar", "/app/raporlar/gunluk", "/app/raporlar/personel-calisma", "/ayarlar", "/subeler" })
            Assert.DoesNotContain(r, op);

        var (muh, _) = await MenuAsync(await GirisAsync(o, Kim.Muhasebe));
        foreach (var r in new[] { "/app/panel", "/kasa", "/faturalar", "/app/raporlar/gunluk", "/app/raporlar/kasa-banka", "/app/araclar/detayli", "/crm", "/maliyet-hesapla", "/app/musteri-taksit", "/vade" })
            Assert.Contains(r, muh);
        foreach (var r in new[] { "/app/kiralar/yeni", "/app/kiralar", "/app/araclar", "/vehicles", "/raporlar/gunluk", "/cariler", "/tarife-aktar", "/ayarlar" })
            Assert.DoesNotContain(r, muh);
    }

    [Fact]
    public async Task Menu_kullanici_bazli_istisna_ve_modul_bayragini_uygular()
    {
        var o = await OrtamKurAsync(webSitesi: true);

        var (ek, _) = await MenuAsync(await GirisAsync(o, Kim.OperatorEkRapor)); // Operatör + ek ViewReports
        Assert.Contains("/app/raporlar/gunluk", ek);   // F10.3: rapor öğeleri spa
        Assert.Contains("/crm", ek);
        Assert.DoesNotContain("/kasa", ek);

        var (yasakli, _) = await MenuAsync(await GirisAsync(o, Kim.AdminYasakli)); // Admin − ManageUsers
        Assert.DoesNotContain("/ayarlar", yasakli);
        Assert.DoesNotContain("/tarife-aktar", yasakli);
        Assert.Contains("/kasa", yasakli);

        var (admin, govde) = await MenuAsync(await GirisAsync(o, Kim.Admin));
        Assert.Contains("/web-sitesi", admin);
        Assert.Contains("/site-icerik", admin);
        Assert.Equal(2, govde.GetProperty("ogeler").EnumerateArray().Count(e => e.GetProperty("rota").GetString() == "/gelen-talepler"));
        Assert.Equal(0, govde.GetProperty("rozetler").GetProperty("yeni-talep").GetInt32());
    }
}
