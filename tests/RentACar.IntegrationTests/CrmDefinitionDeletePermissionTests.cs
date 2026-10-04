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

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul C-HUKUK / C-CRMSIL / D-4: silme işlemleri <c>OperationsDelete</c> ister (cari silmeyle aynı rol matrisi —
/// operatör siler DEĞİL) ve hukuk dosyası üst kaydının (carinin işlem şubesi) şube kapsamından geçer.
/// ORACLE: senaryo elle kurulur — Merkez operatörü; Merkez carili, Kadıköy carili ve carisiz hukuk dosyası;
/// şubesiz anket/şikayet/assistans; seed dışı marka ve gider türü. Beklenen durum kodları ve görünürlük elle yazılı.
/// </summary>
[Collection("web")]
public sealed class CrmDefinitionDeletePermissionTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    private sealed record Session(HttpClient C, string Xsrf);

    private sealed class Env
    {
        public Guid TenantId;
        public string Code = "";
        public string Password = "";
        public string Operator = "";
        public string Admin = "";
        public string Manager = "";
        public Guid OperatorId, ManagerId, OtherComplaint, Kdv, Penalty, Insurer, Group, Document;
        public string LegalOwnNo = "", LegalOtherNo = "", LegalNoneNo = "";
        public Guid LegalOwn, LegalOther, LegalNone, Survey, Complaint, Assistance, Brand, ExpenseType, OtherCustomer;
    }

    private static string Rnd(string p) => p + Guid.NewGuid().ToString("N")[..8];

    private async Task<Env> SetUpAsync()
    {
        var e = new Env
        {
            TenantId = Guid.NewGuid(), Code = Rnd("sil"), Password = WebFixture.RandomPassword(), Operator = Rnd("op"),
            Admin = Rnd("ad"), Manager = Rnd("yo"),
        };
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = e.TenantId, Code = e.Code, Name = e.Code, IsActive = true });
            await db.SaveChangesAsync();
        }

        var merkez = new Branch { Kod = "MRK", Ad = "Merkez" };
        var kadikoy = new Branch { Kod = "KDK", Ad = "Kadıköy" };
        var own = new Customer { Tip = CustomerType.Bireysel, Ad = "Merkez", Soyad = "Carisi", IslemSubeId = merkez.Id };
        var other = new Customer { Tip = CustomerType.Bireysel, Ad = "Kadıköy", Soyad = "Carisi", IslemSubeId = kadikoy.Id };
        var legalOwn = new HukukDosya { DosyaNo = Rnd("H-MRK-").ToUpperInvariant(), CariId = own.Id, Tutar = 100m };
        var legalOther = new HukukDosya { DosyaNo = Rnd("H-KDK-").ToUpperInvariant(), CariId = other.Id, Tutar = 100m };
        var legalNone = new HukukDosya { DosyaNo = Rnd("H-YOK-").ToUpperInvariant(), Tutar = 100m };
        var survey = new Anket { Puan = 5 };
        var complaint = new Sikayet { Konu = "Gecikme" };
        var assistance = new AssistansTalep { Mesaj = "Lastik patladı" };
        var brand = new Brand { Kod = Rnd("M").ToUpperInvariant(), Ad = Rnd("Marka") };
        var expense = new ExpenseCategory { Kod = Rnd("G").ToUpperInvariant(), Ad = Rnd("Gider") };
        // Kadıköy ofisine bağlı şikayet (harici API kapsam süzmesi için) ve tanım silme probe'unun tanımları.
        var otherOffice = new Location { Kod = "K1", Ad = "K1 Kadıköy", Sube = "Kadıköy" };
        var otherComplaint = new Sikayet { Konu = "KDK-GIZLI", CikisOfisi = otherOffice.Ad };
        var kdv = new KdvRate { Kod = Rnd("K"), Ad = Rnd("Kdv"), Oran = 0.1m };
        var penalty = new PenaltyType { Kod = Rnd("C"), Ad = Rnd("Ceza") };
        var insurer = new InsuranceCompany { Kod = Rnd("S"), Ad = Rnd("Sig") };
        var group = new VehicleGroup { Kod = Rnd("G"), Ad = Rnd("Grup") };
        var document = new FirmaDokuman { Baslik = "Gizli", DosyaAdi = "a.pdf", Bytes = [1, 2, 3], Boyut = 3, Sira = 1 };
        using (var host = new TestHost(fx.Pg.AppConnectionString))
        using (var scope = host.ScopeFor(e.TenantId))
        {
            var f = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await f.CreateDbContextAsync();
            db.Branches.AddRange(merkez, kadikoy);
            await db.SaveChangesAsync();
            db.Customers.AddRange(own, other);
            await db.SaveChangesAsync();
            db.HukukDosyalari.AddRange(legalOwn, legalOther, legalNone);
            db.Anketler.Add(survey);
            db.Sikayetler.Add(complaint);
            db.AssistansTalepleri.Add(assistance);
            db.Brands.Add(brand);
            db.ExpenseCategories.Add(expense);
            db.Locations.Add(otherOffice);
            db.Sikayetler.Add(otherComplaint);
            db.KdvOranlari.Add(kdv);
            db.CezaTurleri.Add(penalty);
            db.InsuranceCompanies.Add(insurer);
            db.VehicleGroups.Add(group);
            db.FirmaDokumanlari.Add(document);
            await db.SaveChangesAsync();
        }
        (e.OtherComplaint, e.Kdv, e.Penalty, e.Insurer, e.Group, e.Document) =
            (otherComplaint.Id, kdv.Id, penalty.Id, insurer.Id, group.Id, document.Id);
        (e.LegalOwnNo, e.LegalOtherNo, e.LegalNoneNo) = (legalOwn.DosyaNo, legalOther.DosyaNo, legalNone.DosyaNo);

        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (name, role) in new[] { (e.Operator, UserRole.Operator), (e.Admin, UserRole.Admin), (e.Manager, UserRole.Yonetici) })
            {
                var u = new User
                {
                    TenantId = e.TenantId, UserName = name, DisplayName = name, Rol = role, IsActive = true,
                    AtanmisSube = role == UserRole.Operator ? "Merkez" : null,
                    AtanmisSubeId = role == UserRole.Operator ? merkez.Id : null,
                };
                u.PasswordHash = hasher.HashPassword(u, e.Password);
                db.Users.Add(u);
                if (role == UserRole.Operator) e.OperatorId = u.Id;
                if (role == UserRole.Yonetici) e.ManagerId = u.Id;
            }
            await db.SaveChangesAsync();
        }

        (e.LegalOwn, e.LegalOther, e.LegalNone) = (legalOwn.Id, legalOther.Id, legalNone.Id);
        (e.Survey, e.Complaint, e.Assistance, e.Brand, e.ExpenseType) = (survey.Id, complaint.Id, assistance.Id, brand.Id, expense.Id);
        e.OtherCustomer = other.Id;
        return e;
    }

    [Fact]
    public async Task Operator_silemez_403_kayit_yerinde_kalir_admin_siler_204()
    {
        var e = await SetUpAsync();
        var op = await LoginAsync(e.Code, e.Operator, e.Password);
        var admin = await LoginAsync(e.Code, e.Admin, e.Password);
        var urls = new[]
        {
            $"{V1}/anketler/{e.Survey}", $"{V1}/sikayetler/{e.Complaint}", $"{V1}/assistans-talepleri/{e.Assistance}",
            $"{V1}/hukuk-dosyalari/{e.LegalOwn}", $"{V1}/markalar/{e.Brand}", $"{V1}/gider-turleri/{e.ExpenseType}",
        };

        foreach (var url in urls)
        {
            var r = await Send(op, HttpMethod.Delete, url);
            var text = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"operatör DELETE {url}: beklenen 403, gelen {(int)r.StatusCode}: {text}");
            Assert.Equal("yetki_yok", JsonDocument.Parse(text).RootElement.GetProperty("kod").GetString());
            Assert.Equal(HttpStatusCode.OK, (await admin.C.GetAsync(url)).StatusCode); // kayıt yerinde
        }

        foreach (var url in urls)
        {
            var r = await Send(admin, HttpMethod.Delete, url);
            Assert.True(r.StatusCode == HttpStatusCode.NoContent, $"admin DELETE {url}: beklenen 204, gelen {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            Assert.Equal(HttpStatusCode.NotFound, (await admin.C.GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task Hukuk_dosyasi_carinin_islem_subesiyle_kapsamli_baska_sube_404()
    {
        var e = await SetUpAsync();
        var op = await LoginAsync(e.Code, e.Operator, e.Password);
        var admin = await LoginAsync(e.Code, e.Admin, e.Password);

        // Liste: operatör kendi şubesinin ve carisiz dosyayı görür, Kadıköy'ünkini görmez; admin üçünü de.
        Assert.Equal(new[] { e.LegalOwn, e.LegalNone }.OrderBy(x => x), await LegalIdsAsync(op));
        Assert.Equal(new[] { e.LegalOwn, e.LegalOther, e.LegalNone }.OrderBy(x => x), await LegalIdsAsync(admin));

        // Tekil: başka şubenin dosyası operatöre YOK (404; varlığı da sızmaz), kendi şubesininki 200.
        Assert.Equal(HttpStatusCode.NotFound, (await op.C.GetAsync($"{V1}/hukuk-dosyalari/{e.LegalOther}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.C.GetAsync($"{V1}/hukuk-dosyalari/{e.LegalOwn}")).StatusCode);
        var put = await Send(op, HttpMethod.Put, $"{V1}/hukuk-dosyalari/{e.LegalOther}",
            new { dosyaNo = "H-ELE", tur = "Dava", durum = "Acik", tutar = 1m, aktif = true, surum = "1" });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        // Yeni dosyayı başka şubenin carisine bağlayamaz (403); admin bağlayabilir.
        var create = await Send(op, HttpMethod.Post, $"{V1}/hukuk-dosyalari",
            new { dosyaNo = Rnd("H-YENI-"), cariId = e.OtherCustomer, tur = "Dava", durum = "Acik", tutar = 1m, aktif = true });
        var createText = await create.Content.ReadAsStringAsync();
        Assert.True(create.StatusCode == HttpStatusCode.Forbidden, $"beklenen 403, gelen {(int)create.StatusCode}: {createText}");
        Assert.Equal(HttpStatusCode.OK, (await admin.C.GetAsync($"{V1}/hukuk-dosyalari/{e.LegalOther}")).StatusCode);
    }

    // ---- güvenlik incelemesi F3/F4 (probe'lar kalıcı teste çevrildi) ----

    [Fact]
    public async Task F3_operator_diger_tanimlari_da_silemez_admin_siler()
    {
        var e = await SetUpAsync();
        var op = await LoginAsync(e.Code, e.Operator, e.Password);
        var admin = await LoginAsync(e.Code, e.Admin, e.Password);
        var urls = new[]
        {
            $"{V1}/kdv-oranlari/{e.Kdv}", $"{V1}/ceza-turleri/{e.Penalty}", $"{V1}/sigorta-sirketleri/{e.Insurer}",
            $"{V1}/arac-gruplari/{e.Group}", $"{V1}/dokumanlar/{e.Document}",
        };
        foreach (var url in urls)
        {
            var r = await Send(op, HttpMethod.Delete, url);
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"operatör DELETE {url}: beklenen 403, gelen {(int)r.StatusCode}");
        }
        foreach (var url in urls)
        {
            var r = await Send(admin, HttpMethod.Delete, url);
            Assert.True(r.StatusCode == HttpStatusCode.NoContent, $"admin DELETE {url}: beklenen 204, gelen {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task F3_kullanici_bazli_istisna_ve_yasak_silmede_gecerli_kapsam_korunur()
    {
        var e = await SetUpAsync();
        await GrantAsync(e, e.OperatorId, "OperationsDelete", true);
        await GrantAsync(e, e.ManagerId, "OperationsDelete", false);
        var op = await LoginAsync(e.Code, e.Operator, e.Password);
        var manager = await LoginAsync(e.Code, e.Manager, e.Password);

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, HttpMethod.Delete, $"{V1}/hukuk-dosyalari/{e.LegalOwn}")).StatusCode);
        // İstisnalı operatör: başka şubenin dosyası "yok" (404), kendi şubesininkini siler.
        Assert.Equal(HttpStatusCode.NotFound, (await Send(op, HttpMethod.Delete, $"{V1}/hukuk-dosyalari/{e.LegalOther}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(op, HttpMethod.Delete, $"{V1}/hukuk-dosyalari/{e.LegalOwn}")).StatusCode);
    }

    [Fact]
    public async Task F4_hukuk_exportu_sube_kapsamli()
    {
        var e = await SetUpAsync();
        await GrantAsync(e, e.OperatorId, "ViewReports", true);
        var op = await LoginAsync(e.Code, e.Operator, e.Password);
        var r = await op.C.GetAsync("/listeler/export/hukuk?format=csv");
        var body = (await r.Content.ReadAsStringAsync()).ToUpperInvariant();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"export {(int)r.StatusCode}: {body}");
        Assert.Contains(e.LegalOwnNo, body);
        Assert.Contains(e.LegalNoneNo, body);
        Assert.DoesNotContain(e.LegalOtherNo, body);
    }

    [Fact]
    public async Task F4_harici_api_hukuk_ve_sikayet_listeleri_sube_kapsamli()
    {
        var e = await SetUpAsync();
        using var api = new ApiFactory(fx.Pg.AppConnectionString);
        var op = await api.LoginClientAsync(e.Code, e.Operator, e.Password);
        var admin = await api.LoginClientAsync(e.Code, e.Admin, e.Password);

        static async Task<List<Guid>> Ids(HttpClient c, string url)
        {
            var r = await c.GetAsync(url);
            var text = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.OK, $"{url} {(int)r.StatusCode}: {text}");
            return JsonDocument.Parse(text).RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Order().ToList();
        }

        Assert.Equal(new[] { e.LegalOwn, e.LegalNone }.Order(), await Ids(op, "/api/v1/legal"));
        Assert.Equal(new[] { e.LegalOwn, e.LegalOther, e.LegalNone }.Order(), await Ids(admin, "/api/v1/legal"));
        Assert.Equal(new[] { e.Complaint }, await Ids(op, "/api/v1/crm/sikayetler"));
        Assert.Equal(new[] { e.Complaint, e.OtherComplaint }.Order(), await Ids(admin, "/api/v1/crm/sikayetler"));
    }

    private async Task GrantAsync(Env e, Guid userId, string permission, bool grant)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance);
        db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = e.TenantId, UserId = userId, Izin = permission, Ver = grant });
        await db.SaveChangesAsync();
    }

    private static async Task<IEnumerable<Guid>> LegalIdsAsync(Session s)
    {
        var r = await s.C.GetAsync($"{V1}/hukuk-dosyalari?boyut=100");
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("kayitlar").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).OrderBy(x => x).ToList();
    }

    private static Task<HttpResponseMessage> Send(Session s, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return s.C.SendAsync(req);
    }

    private async Task<Session> LoginAsync(string company, string user, string password)
    {
        var c = fx.Web.Client();
        var once = Cookie(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = company, kullanici = user, sifre = password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız: {await r.Content.ReadAsStringAsync()}");
        return new Session(c, Cookie(r, "XSRF-TOKEN")!);
    }

    private static string? Cookie(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }
}
