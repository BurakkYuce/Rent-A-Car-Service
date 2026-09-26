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

    private static string RandomText(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Firma + her rolden kullanıcı (rastgele ad/şifre) + kullanıcı-bazlı istisnalar. Owner bağlantısı (platform tabloları).</summary>
    private async Task<Ortam> SetUpEnvironmentAsync(bool pilot = true, bool website = false)
    {
        var environment = new Ortam
        {
            TenantId = Guid.NewGuid(),
            Kod = RandomText("f16"),
            Sifre = WebFixture.RandomPassword(),
            Kullanicilar = Enum.GetValues<Kim>().ToDictionary(k => k, _ => RandomText("u")),
        };

        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = environment.TenantId, Code = environment.Kod, Name = environment.Kod, IsActive = true, WebSitesiModulu = website });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>(); // girişin doğruladığı hasher
            foreach (var (kim, name) in environment.Kullanicilar)
            {
                var (rol, branch) = kim switch
                {
                    Kim.Admin or Kim.AdminYasakli => (UserRole.Admin, (string?)null),
                    Kim.OperatorA or Kim.OperatorYasakli or Kim.OperatorEkRapor => (UserRole.Operator, "SubeA"),
                    Kim.OperatorB => (UserRole.Operator, "SubeB"),
                    _ => (UserRole.Muhasebe, null),
                };
                var u = new User { TenantId = environment.TenantId, UserName = name, DisplayName = name, Rol = rol, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, environment.Sifre);
                db.Users.Add(u);
                if (kim == Kim.OperatorYasakli)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = environment.TenantId, UserId = u.Id, Izin = "OperationsWrite", Ver = false });
                if (kim == Kim.OperatorEkRapor)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = environment.TenantId, UserId = u.Id, Izin = "ViewReports", Ver = true });
                if (kim == Kim.AdminYasakli)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = environment.TenantId, UserId = u.Id, Izin = "ManageUsers", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(environment.TenantId, pilot);
        return environment;
    }

    /// <summary>Kiracı verisi GERÇEK uygulama rolüyle (RLS'li racar_app) yazılır.</summary>
    private async Task WriteDataAsync(Guid tenantId, Action<AppDbContext> write)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var scope = host.ScopeFor(tenantId);
        var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await f.CreateDbContextAsync();
        write(db);
        await db.SaveChangesAsync();
    }

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<HttpClient> LoginAsync(Ortam o, Kim kim)
    {
        var c = fx.Web.Client();
        var x = await c.GetAsync(V1 + "/oturum/xsrf");
        var token = CookieValue(x, "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", token);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({kim}): {await r.Content.ReadAsStringAsync()}");
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, JsonDocument.Parse(text).RootElement.GetProperty("kod").GetString());
    }

    private static List<string> Tags(JsonElement array) => array.EnumerateArray().Select(e => e.GetProperty("etiket").GetString()!).ToList();

    private static void FieldSet(JsonElement array, params string[] expected)
    {
        Assert.True(array.GetArrayLength() > 0, "Boş liste — alan kümesi sınanamadı.");
        foreach (var e in array.EnumerateArray())
            Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal),
                e.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------ müşteri: Türkçe arama, sınır, PII

    [Fact]
    public async Task Musteri_turkce_katlamali_arama_ve_pii_yok()
    {
        var o = await SetUpEnvironmentAsync();
        await WriteDataAsync(o.TenantId, db =>
        {
            db.Customers.Add(new Customer { Tip = CustomerType.Kurumsal, Unvan = "IŞIK Lojistik", CepTel = "05321112233", Email = "gizli-f16@ornek.test", Adres = "Gizli Sokak 7" });
            db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = "Ayşe", Soyad = "Işık", CepTel = "05329998877" });
            db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = "Ali", Soyad = "Veli" });
        });
        var c = await LoginAsync(o, Kim.Admin);

        foreach (var q in new[] { "ışık", "IŞIK", "isik", "Işık" })
        {
            var result = await Json(await c.GetAsync($"{V1}/secim/musteri?q={Uri.EscapeDataString(q)}"));
            Assert.Equal(new[] { "Ayşe Işık", "IŞIK Lojistik" }, Tags(result).Order(StringComparer.Ordinal).ToArray());
        }

        var raw = await c.GetAsync($"{V1}/secim/musteri");
        var text = await raw.Content.ReadAsStringAsync();
        var all = JsonDocument.Parse(text).RootElement;
        Assert.Equal(3, all.GetArrayLength());
        FieldSet(all, "id", "etiket", "tip");
        Assert.DoesNotContain("0532", text);
        Assert.DoesNotContain("gizli-f16", text);
        Assert.DoesNotContain("Gizli Sokak", text);
        Assert.True(raw.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task Limit_en_cok_20_varsayilan_20()
    {
        var o = await SetUpEnvironmentAsync();
        await WriteDataAsync(o.TenantId, db =>
        {
            for (var i = 1; i <= 25; i++)
            {
                db.Customers.Add(new Customer { Tip = CustomerType.Bireysel, Ad = $"Sınır{i:00}", Soyad = "Müşteri" });
                db.Vehicles.Add(new Vehicle { Plaka = $"34 LMT {i:00}", Sube = "SubeA", Durum = VehicleStatus.Musait });
            }
        });
        var c = await LoginAsync(o, Kim.Admin);

        foreach (var endpoint in new[] { "musteri", "arac" })
        {
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{endpoint}?limit=500"))).GetArrayLength());
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{endpoint}"))).GetArrayLength());
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{endpoint}?limit=0"))).GetArrayLength());
            Assert.Equal(20, (await Json(await c.GetAsync($"{V1}/secim/{endpoint}?limit=-3"))).GetArrayLength());
            Assert.Equal(5, (await Json(await c.GetAsync($"{V1}/secim/{endpoint}?limit=5"))).GetArrayLength());
        }
        // q ile daraltma: "sinir0" → Sınır01..Sınır09 (9 kayıt); "lmt 2" → 20..25 (6 kayıt).
        Assert.Equal(9, (await Json(await c.GetAsync($"{V1}/secim/musteri?q=sinir0"))).GetArrayLength());
        Assert.Equal(6, (await Json(await c.GetAsync($"{V1}/secim/arac?q=LMT%202"))).GetArrayLength());
    }

    // ------------------------------------------------------------ yetki / pilot

    [Fact]
    public async Task Izinsiz_rol_403_yetki_yok_kullanici_yasagi_da_uygulanir()
    {
        var o = await SetUpEnvironmentAsync();
        var acct = await LoginAsync(o, Kim.Muhasebe);
        foreach (var endpoint in new[] { "arac", "lokasyon", "personel", "sube", "ek-hizmet", "sigorta-urunu",
                     "rezervasyon-kaynagi", "ozel-kod", "belge-sablonu", "arac-grubu" })
            await ExpectProblem(await acct.GetAsync($"{V1}/secim/{endpoint}"), HttpStatusCode.Forbidden, "yetki_yok");
        // F4.4: müşteri ve kur Muhasebe'ye (FinanceWrite) de açık — kira formunun sabit finans paneli.
        Assert.Equal(HttpStatusCode.OK, (await acct.GetAsync($"{V1}/secim/musteri")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await acct.GetAsync($"{V1}/secim/kur")).StatusCode);

        // Operatör ama OperationsWrite kullanıcı-bazlı YASAK (FinanceWrite de yok): iki "herhangi biri" ucu da 403.
        var banned = await LoginAsync(o, Kim.OperatorYasakli);
        foreach (var endpoint in new[] { "musteri", "kur", "arac" })
            await ExpectProblem(await banned.GetAsync($"{V1}/secim/{endpoint}"), HttpStatusCode.Forbidden, "yetki_yok");

        var op = await LoginAsync(o, Kim.OperatorA);
        Assert.Equal(HttpStatusCode.OK, (await op.GetAsync($"{V1}/secim/musteri")).StatusCode);

        var anonymous = await fx.Web.Client().GetAsync($"{V1}/secim/musteri");
        await ExpectProblem(anonymous, HttpStatusCode.Unauthorized, "oturum_yok");
    }

    /// <summary>F4.4 yapısal kilit: seçim uçlarının etkin izin kapısı. Yalnız <c>musteri</c>, <c>kur</c> ve
    /// <c>gider-kategorisi</c> arama uçları "OperationsWrite veya FinanceWrite"; <c>satilabilir-arac</c> FinanceWrite; geri
    /// kalan hepsi (kimlikle etiket uçları dahil) OperationsWrite (genişleme sessizce yayılmasın).</summary>
    [Fact]
    public void Secim_uclari_izin_haritasi()
    {
        var endpoints = fx.Web.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => ("/" + (e.RoutePattern.RawText ?? "").TrimStart('/')).StartsWith(V1 + "/secim/", StringComparison.Ordinal))
            .ToDictionary(e => "/" + e.RoutePattern.RawText!.TrimStart('/'));
        // 12 arama + F4.3b kimlikle etiket (musteri/{id}, arac/{id}) — ikisi OW
        // + F9.1 üç uç, hepsi TEK izin OperationsWrite (aşağıdaki else dalı birebir doğrular):
        //   sigorta-sirketi → Blazor poliçe formu (/regulasyon/sigorta, OW grubu) firma listesi;
        //   tarife-grubu → Blazor tarife formu (/tarifeler, izin:OperationsWrite) grup listesi;
        //   sigorta-policesi → Blazor zeyil formu (/regulasyon/zeyil, OW grubu) poliçe listesi (araç şubesi kapsamlı).
        // + #300 eksik uçlar: gider-kategorisi (OW ∨ FW — gelen e-faturadan gider, Muhasebe) ve satilabilir-arac
        //   (FinanceWrite — araç satışıyla aynı izin).
        Assert.Equal(19, endpoints.Count);
        foreach (var f91 in new[] { "sigorta-sirketi", "tarife-grubu", "sigorta-policesi" })
            Assert.Contains(V1 + "/secim/" + f91, endpoints.Keys);
        foreach (var (route, e) in endpoints)
        {
            var one = e.Metadata.GetMetadata<RentACar.Web.Identity.IzinlerdenBiriMetadata>();
            var tek = e.Metadata.GetMetadata<RentACar.Web.Identity.IzinMetadata>();
            if (route is V1 + "/secim/musteri" or V1 + "/secim/kur" or V1 + "/secim/gider-kategorisi")
            {
                Assert.Null(tek);
                Assert.Equal(new[] { Permission.OperationsWrite, Permission.FinanceWrite }, one!.Izinler);
            }
            else if (route == V1 + "/secim/satilabilir-arac")
            {
                Assert.Null(one);
                Assert.Equal(Permission.FinanceWrite, tek!.Izin);
            }
            else
            {
                Assert.Null(one);
                Assert.Equal(Permission.OperationsWrite, tek!.Izin);
            }
        }
    }

    /// <summary>F13.1b: pilot kapısı kalktı — bayrağı kapalı firma da seçim ve menü uçlarını kullanır.</summary>
    [Fact]
    public async Task Pilot_bayragi_kapali_firma_da_secim_ve_menu_okur()
    {
        var o = await SetUpEnvironmentAsync(pilot: false);
        var c = await LoginAsync(o, Kim.Admin);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{V1}/secim/musteri")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{V1}/menu")).StatusCode);
    }

    // ------------------------------------------------------------ şube kapsamı + alan kümeleri

    [Fact]
    public async Task Sube_kapsami_arac_personel_sube_ve_alan_kumeleri()
    {
        var o = await SetUpEnvironmentAsync();
        var today = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        await WriteDataAsync(o.TenantId, db =>
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
            if (!await db.KurKayitlari.AnyAsync(k => k.Tarih == today && k.Kod == "USD"))
                db.KurKayitlari.Add(new KurKaydi { Tarih = today, Kod = "USD", Ad = "ABD DOLARI", Birim = 1, ForexAlis = 40m, ForexSatis = 41m });
            await db.SaveChangesAsync();
        }

        var op = await LoginAsync(o, Kim.OperatorA);
        var admin = await LoginAsync(o, Kim.Admin);

        // Araç: operatör yalnız kendi şubesi; admin hepsi.
        var opVehicle = await Json(await op.GetAsync($"{V1}/secim/arac"));
        Assert.Equal(new[] { "34 SBA 01 — Fiat Egea" }, Tags(opVehicle));
        FieldSet(opVehicle, "id", "etiket", "plaka", "grup", "durum");
        Assert.Equal("C", opVehicle[0].GetProperty("grup").GetString());
        Assert.Equal(2, (await Json(await admin.GetAsync($"{V1}/secim/arac"))).GetArrayLength());

        // Personel ve şube: operatör yalnız kendi şubesi.
        var opStaff = await Json(await op.GetAsync($"{V1}/secim/personel"));
        Assert.Equal(new[] { "Ahmet Asube" }, Tags(opStaff));
        FieldSet(opStaff, "id", "etiket", "kod");
        Assert.Equal(2, (await Json(await admin.GetAsync($"{V1}/secim/personel"))).GetArrayLength());
        Assert.Equal(new[] { "SubeA" }, Tags(await Json(await op.GetAsync($"{V1}/secim/sube"))));
        Assert.Equal(2, (await Json(await admin.GetAsync($"{V1}/secim/sube"))).GetArrayLength());

        // Lokasyon bilinçli kapsamsız (dönüş ofisi başka şube olabilir) — subeId taşır.
        var lok = await Json(await op.GetAsync($"{V1}/secim/lokasyon"));
        Assert.Equal(2, lok.GetArrayLength());
        FieldSet(lok, "id", "etiket", "kod", "subeId");
        Assert.Equal(new[] { "Havalimanı A" }, Tags(await Json(await op.GetAsync($"{V1}/secim/lokasyon?q=HAVALIMANI"))));

        // Tanımlar: yalnız aktif; genel alan kümesi.
        var source = await Json(await op.GetAsync($"{V1}/secim/rezervasyon-kaynagi"));
        Assert.Equal(new[] { "Web Sitesi" }, Tags(source));
        FieldSet(source, "id", "etiket", "kod");
        FieldSet(await Json(await op.GetAsync($"{V1}/secim/ek-hizmet?q=bebek")), "id", "etiket", "kod");
        FieldSet(await Json(await op.GetAsync($"{V1}/secim/sigorta-urunu")), "id", "etiket", "kod");
        FieldSet(await Json(await op.GetAsync($"{V1}/secim/ozel-kod")), "id", "etiket", "kod");
        FieldSet(await Json(await op.GetAsync($"{V1}/secim/arac-grubu")), "id", "etiket", "kod");
        Assert.Equal(new[] { "Standart Sözleşme" }, Tags(await Json(await op.GetAsync($"{V1}/secim/belge-sablonu?tur=KiraSozlesmesi"))));
        Assert.Equal(2, (await Json(await op.GetAsync($"{V1}/secim/belge-sablonu"))).GetArrayLength());

        var exchangeRate = await Json(await op.GetAsync($"{V1}/secim/kur?q=usd"));
        FieldSet(exchangeRate, "id", "etiket", "birim", "dovizAlis", "dovizSatis", "tarih");
        Assert.Equal("USD", exchangeRate[0].GetProperty("id").GetString());
        Assert.Equal(41m, exchangeRate[0].GetProperty("dovizSatis").GetDecimal());

        // PII sızmaz (telefon, şasi no).
        foreach (var endpoint in new[] { "arac", "personel", "lokasyon" })
        {
            var text = await (await admin.GetAsync($"{V1}/secim/{endpoint}")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("0555", text);
            Assert.DoesNotContain("0212", text);
            Assert.DoesNotContain("GIZLISASI", text);
        }
    }

    // ------------------------------------------------------------ menü

    private static async Task<(HashSet<string> Rotalar, JsonElement Govde)> MenuAsync(HttpClient c)
    {
        var j = await Json(await c.GetAsync($"{V1}/menu"));
        var routes = j.GetProperty("ogeler").EnumerateArray().Select(e => e.GetProperty("rota").GetString()!).ToHashSet();
        return (routes, j);
    }

    [Fact]
    public async Task Menu_rol_ve_izne_gore_suzulur()
    {
        var o = await SetUpEnvironmentAsync();

        var (admin, adminBody) = await MenuAsync(await LoginAsync(o, Kim.Admin));
        // F4.6: Panel, Kiralar, Yeni Kira yeni arayüzün (sahip spa, rota /app/…).
        foreach (var r in new[] { "/app/panel", "/app/kiralar/yeni", "/app/araclar", "/app/araclar/detayli", "/app/cariler", "/app/crm", "/app/hukuk", "/app/kasa", "/app/faturalar", "/app/raporlar/karlilik", "/app/raporlar/personel-calisma", "/app/tarife-aktar", "/app/servisler", "/app/regulasyon", "/app/maliyet-teklifleri", "/app/ayarlar", "/app/vade", "/app/bildirimler", "/app/markalar", "/app/subeler", "/app/blog-yonetim" })
            Assert.Contains(r, admin);
        Assert.DoesNotContain("/app/web-sitesi", admin);   // modül kapalı
        Assert.DoesNotContain("/app/site-icerik", admin);
        var items = adminBody.GetProperty("ogeler").EnumerateArray().ToList();
        FieldSet(adminBody.GetProperty("ogeler"), "rota", "etiket", "grup", "sira", "sahip", "rozetKodu", "hizliBaglanti");
        Assert.All(items, e => Assert.Equal(
            e.GetProperty("rota").GetString()!.StartsWith("/app/", StringComparison.Ordinal) ? "spa" : "blazor",
            e.GetProperty("sahip").GetString()));
        // F4.6: 3, F5.4: +8, F6.4: +14 (Araçlar 12 + Tanımlar 2), F7.3: +6 (Cariler & CRM), F10.3: +25 (Raporlar), F9.3: +15 (Servis & Sigorta 3, Fiyat & Tarife 11, Vade Panosu),
        // F8.3: +17 (Finans), F11.3: +39 (Tanımlar 24 + Sistem 9 + grupsuz 4 + Blog/Gelen Talepler 2; Web Sitesi grubu
        // modül kapalı → gizli) = 127
        Assert.Equal(127, items.Count(e => e.GetProperty("sahip").GetString() == "spa"));
        Assert.DoesNotContain(items, e => e.GetProperty("rota").GetString() is "/markalar" or "/ayarlar" or "/bildirimler" or "/gelen-talepler" or "/blog-yonetim");
        Assert.All(items.Where(e => e.GetProperty("grup").GetString() is "Tanımlar" or "Sistem"),
            e => Assert.Equal("spa", e.GetProperty("sahip").GetString()));
        Assert.DoesNotContain(items, e => e.GetProperty("rota").GetString() is "/cariler" or "/crm" or "/anketler" or "/sikayetler" or "/assistans" or "/hukuk");
        Assert.DoesNotContain(items, e => e.GetProperty("rota").GetString() is "/kasa" or "/faturalar" or "/kurlar" or "/cezalar" or "/giderler" or "/satislar" or "/donem-kapanis");
        Assert.Equal(17, items.Count(e => e.GetProperty("grup").GetString() == "Finans" && e.GetProperty("sahip").GetString() == "spa"));
        Assert.DoesNotContain(items, e => e.GetProperty("rota").GetString()!.StartsWith("/raporlar/", StringComparison.Ordinal));
        Assert.DoesNotContain(items, e => e.GetProperty("rota").GetString() is "/servisler" or "/servis-tanimlari" or "/regulasyon"
            or "/vade" or "/tarifeler" or "/tarife-matris" or "/tarife-gruplari" or "/tarife-aktar" or "/sigorta-urunleri"
            or "/kira-kurallari" or "/broker-yasaklari" or "/fiyat-hesapla" or "/maliyet-hesapla" or "/maliyet-teklifleri"
            or "/ek-hizmetler"); // F9.3
        Assert.Equal(3, items.Count(e => e.GetProperty("hizliBaglanti").GetBoolean()));
        var orders = items.Select(e => e.GetProperty("sira").GetInt32()).ToList();
        Assert.Equal(orders.Order(), orders);         // sıralı döner
        // Gelen Talepler CRM grubunda modülden bağımsız görünür; Web Sitesi grubundaki kopyası gizli.
        Assert.Single(items, e => e.GetProperty("rota").GetString() == "/app/gelen-talepler");
        Assert.True(adminBody.GetProperty("rozetler").TryGetProperty("okunmamis-bildirim", out _));
        Assert.False(adminBody.GetProperty("rozetler").TryGetProperty("yeni-talep", out _)); // modül kapalı → sorulmaz

        var (op, _) = await MenuAsync(await LoginAsync(o, Kim.OperatorA));
        foreach (var r in new[] { "/app/panel", "/app/kiralar/yeni", "/app/rezervasyonlar", "/app/musaitlik", "/app/takvim", "/app/araclar", "/app/arac-durum", "/app/baf", "/app/segmentler", "/app/kiralar", "/app/cariler", "/app/sikayetler", "/app/vade", "/app/servisler", "/app/tarifeler", "/app/fiyat-hesapla", "/app/dokumanlar", "/app/markalar", "/app/lokasyonlar", "/app/bildirimler" })
            Assert.Contains(r, op);
        foreach (var r in new[] { "/app/araclar/detayli", "/app/musteri-taksit", "/vehicles", "/app/crm", "/crm", "/maliyet-hesapla", "/app/maliyet-hesapla", "/app/maliyet-teklifleri", "/tarife-aktar", "/app/tarife-aktar", "/vade", "/servisler", "/kasa", "/kurlar", "/app/kasa", "/app/kurlar", "/app/cezalar", "/app/raporlar/gunluk", "/app/raporlar/personel-calisma", "/app/ayarlar", "/app/subeler", "/app/personel", "/app/belge-sablonlari", "/app/ice-aktar" })
            Assert.DoesNotContain(r, op);

        var (acct, _) = await MenuAsync(await LoginAsync(o, Kim.Muhasebe));
        foreach (var r in new[] { "/app/panel", "/app/kasa", "/app/faturalar", "/app/donem-kapanis", "/app/raporlar/gunluk", "/app/raporlar/kasa-banka", "/app/araclar/detayli", "/app/crm", "/app/maliyet-hesapla", "/app/maliyet-teklifleri", "/app/musteri-taksit", "/app/vade" })
            Assert.Contains(r, acct);
        foreach (var r in new[] { "/app/kiralar/yeni", "/app/kiralar", "/app/araclar", "/vehicles", "/raporlar/gunluk", "/app/cariler", "/cariler", "/tarife-aktar", "/app/tarife-aktar", "/app/tarifeler", "/app/servisler", "/app/ayarlar", "/app/markalar", "/markalar", "/kasa", "/faturalar" })
            Assert.DoesNotContain(r, acct);
        Assert.Contains("/app/bildirimler", acct);         // F11.3: grupsuz spa öğesi her rolde
    }

    [Fact]
    public async Task Menu_kullanici_bazli_istisna_ve_modul_bayragini_uygular()
    {
        var o = await SetUpEnvironmentAsync(website: true);

        var (extra, _) = await MenuAsync(await LoginAsync(o, Kim.OperatorEkRapor)); // Operatör + ek ViewReports
        Assert.Contains("/app/raporlar/gunluk", extra);   // F10.3: rapor öğeleri spa
        Assert.Contains("/app/crm", extra);               // F7.3: CRM Analiz spa
        Assert.DoesNotContain("/kasa", extra);
        Assert.DoesNotContain("/app/kasa", extra);        // F8.3: Finans spa, FinanceWrite ister

        var (banned, _) = await MenuAsync(await LoginAsync(o, Kim.AdminYasakli)); // Admin − ManageUsers
        Assert.DoesNotContain("/app/ayarlar", banned);
        Assert.DoesNotContain("/app/kullanicilar", banned);
        Assert.DoesNotContain("/app/tarife-aktar", banned);
        Assert.Contains("/app/kasa", banned);         // F8.3: Finans spa

        var (admin, body) = await MenuAsync(await LoginAsync(o, Kim.Admin));
        Assert.Contains("/app/web-sitesi", admin);        // F11.3: Web Sitesi grubu spa
        Assert.Contains("/app/site-icerik", admin);
        Assert.Equal(2, body.GetProperty("ogeler").EnumerateArray().Count(e => e.GetProperty("rota").GetString() == "/app/gelen-talepler"));
        // Modül açık: 127 + Web Sitesi grubu 4 = 131 spa öğe.
        Assert.Equal(131, body.GetProperty("ogeler").EnumerateArray().Count(e => e.GetProperty("sahip").GetString() == "spa"));
        Assert.Equal(0, body.GetProperty("rozetler").GetProperty("yeni-talep").GetInt32());
    }
}
