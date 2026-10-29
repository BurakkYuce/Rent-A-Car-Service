using System.Net;
using System.Net.Http.Json;
using System.Text;
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
/// <c>/api/ui/v1/hgs/*</c> GERÇEK Web boru hattında: çok parçalı yükleme (önizleme yazmaz, içe aktarma tek kayıt),
/// eşleme hataları <c>errors[eslesme]</c>, şube kapsamı (liste süzülür; başka şubenin geçişini yansıtma 403, başka
/// kiracı 404), şubeye bağlı kullanıcı içe aktaramaz (403), yansıtma tutarı ve kira formundaki geçiş listesi.
/// <para><b>BAĞIMSIZ ORACLE:</b> dosya ve beklenenler <see cref="HgsImportTests.ReferenceCsv"/> ile aynı elle kurulmuş
/// senaryo; 52,50 × 1,03 = 54,08 ve 995,00 × 1,03 = 1.024,85 elle. Kullanıcılar/parolalar çalışma anında rastgele.</para>
/// </summary>
[Collection("web")]
public sealed class UiHgsTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";

    private enum Who { Admin, OperatorA, OperatorB }

    private sealed record Env(Guid TenantId, string Code, string Password, Dictionary<Who, string> Users, HgsImportTests.World World);

    private sealed record Session(HttpClient C, string Xsrf);

    private static string Random10(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    private async Task<Env> SetupAsync()
    {
        var tenantId = Guid.NewGuid();
        var code = Random10("hgs");
        var password = WebFixture.RandomPassword();
        var users = Enum.GetValues<Who>().ToDictionary(k => k, _ => Random10("u"));
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = code, Name = code, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (who, name) in users)
            {
                var (role, branch) = who switch
                {
                    Who.Admin => (UserRole.Admin, (string?)null),
                    Who.OperatorA => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Operator, "SubeB"),
                };
                var u = new User { TenantId = tenantId, UserName = name, DisplayName = name, Rol = role, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, password);
                db.Users.Add(u);
                // Operatörlere FinanceWrite: şube kapsamı izin kapısından AYRI sınansın.
                if (who != Who.Admin)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceWrite", Ver = true });
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenantId, true);
        using var host = new TestHost(fx.Pg.AppConnectionString);
        var world = await HgsImportTests.SetUpAsync(host, tenantId);
        return new Env(tenantId, code, password, users, world);
    }

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
            if (v.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(v[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Session> LoginAsync(Env e, Who who)
    {
        var c = fx.Web.Client();
        var before = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", before);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({who}): {await r.Content.ReadAsStringAsync()}");
        return new Session(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> UploadAsync(Session s, string path, string csv, string? mapping = null)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "dosya", "gecisler.csv" } };
        if (mapping is not null) form.Add(new StringContent(mapping), "eslesme");
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + path) { Content = form };
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        return s.C.SendAsync(req);
    }

    private static Task<HttpResponseMessage> PostAsync(Session s, string path, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + path) { Content = JsonContent.Create(body) };
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task Problem(HttpResponseMessage r, HttpStatusCode status, string? code, string? field = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        var j = JsonDocument.Parse(text).RootElement;
        if (code is not null) Assert.Equal(code, j.GetProperty("kod").GetString());
        if (field is not null)
            Assert.True(j.TryGetProperty("errors", out var er) && er.TryGetProperty(field, out var m) && m.GetArrayLength() > 0,
                $"errors[{field}] yok: {text}");
    }

    private static async Task<List<JsonElement>> ListAsync(Session s, string query = "")
        => (await Ok(await s.C.GetAsync(V1 + "/hgs/gecisler?boyut=200" + query))).GetProperty("kayitlar").EnumerateArray().ToList();

    [Fact]
    public async Task Upload_preview_import_scope_and_reflection_over_http()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);

        // Önizleme: hiçbir şey yazmaz, önerilen eşleme döner.
        var preview = await Ok(await UploadAsync(admin, "/hgs/ice-aktar/onizle", HgsImportTests.ReferenceCsv()));
        Assert.Equal(12, preview.GetProperty("toplam").GetInt32());
        Assert.Equal(5, preview.GetProperty("eslesen").GetInt32());
        Assert.Equal(4, preview.GetProperty("kiraDisi").GetInt32());
        Assert.Equal(1, preview.GetProperty("mukerrer").GetInt32());
        Assert.Equal(2, preview.GetProperty("hatali").GetInt32());
        Assert.Equal(0, preview.GetProperty("eklenen").GetInt32());
        var fields = preview.GetProperty("alanlar").EnumerateArray().ToDictionary(
            f => f.GetProperty("kod").GetString()!, f => f.GetProperty("baslik").GetString());
        Assert.Equal("Plaka", fields["plaka"]);
        Assert.Equal("Geçiş Tarihi", fields["gecisZamani"]);
        Assert.Equal("HGS Etiket No", fields["etiketNo"]);
        Assert.Empty(await ListAsync(admin));

        // Eşleme hataları alan hatası.
        await Problem(await UploadAsync(admin, "/hgs/ice-aktar/onizle", HgsImportTests.ReferenceCsv(), "{\"plaka\":\"Yok\"}"),
            HttpStatusCode.BadRequest, null, "eslesme");
        await Problem(await UploadAsync(admin, "/hgs/ice-aktar", HgsImportTests.ReferenceCsv(), "{\"plaka\":\"Plaka\"}"),
            HttpStatusCode.BadRequest, null, "eslesme"); // zorunlu alanlar eksik → yazma yok
        await Problem(await UploadAsync(admin, "/hgs/ice-aktar/onizle", HgsImportTests.ReferenceCsv(), "{bozuk"),
            HttpStatusCode.BadRequest, null, "eslesme");

        // Şubeye bağlı kullanıcı içe aktaramaz.
        var opA = await LoginAsync(e, Who.OperatorA);
        await Problem(await UploadAsync(opA, "/hgs/ice-aktar", HgsImportTests.ReferenceCsv()), HttpStatusCode.Forbidden, "yetki_yok");

        // İçe aktar; ikinci kez aynı dosya tek kayıt.
        var imported = await Ok(await UploadAsync(admin, "/hgs/ice-aktar", HgsImportTests.ReferenceCsv()));
        Assert.Equal(9, imported.GetProperty("eklenen").GetInt32());
        var again = await Ok(await UploadAsync(admin, "/hgs/ice-aktar", HgsImportTests.ReferenceCsv()));
        Assert.Equal(0, again.GetProperty("eklenen").GetInt32());
        Assert.Equal(10, again.GetProperty("mukerrer").GetInt32());
        var all = await ListAsync(admin);
        Assert.Equal(9, all.Count);

        // Kapsam: SubeB operatörü yalnız SubeB kirasının geçişini görür; SubeA operatörü kiracı genelini (araç yok) görmez.
        var opB = await LoginAsync(e, Who.OperatorB);
        var bRows = await ListAsync(opB);
        Assert.Equal(e.World.R3, Assert.Single(bRows).GetProperty("kiraId").GetGuid());
        var aRows = await ListAsync(opA);
        Assert.Equal(7, aRows.Count); // V1'in 7 geçişi (kiralı + kira dışı); bilinmeyen plaka ve V2 görünmez
        Assert.DoesNotContain(aRows, r => r.GetProperty("plaka").GetString() == "99ZZ999");

        var r1Ids = all.Where(r => r.TryGetProperty("kiraId", out var k) && k.ValueKind == JsonValueKind.String && k.GetGuid() == e.World.R1)
            .Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, r1Ids.Count);

        // Başka şube 403 (durumdan önce), başka kiracı 404.
        await Problem(await PostAsync(opB, "/hgs/gecisler/yansit", new { ids = r1Ids, hizmetOrani = 1.03m }),
            HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync();
        var otherAdmin = await LoginAsync(other, Who.Admin);
        await Problem(await PostAsync(otherAdmin, "/hgs/gecisler/yansit", new { ids = r1Ids, hizmetOrani = 1.03m }),
            HttpStatusCode.NotFound, null);
        await Problem(await PostAsync(admin, "/hgs/gecisler/yansit", new { ids = r1Ids }), HttpStatusCode.BadRequest, null, "hizmetOrani");
        await Problem(await PostAsync(admin, "/hgs/gecisler/yansit", new { ids = r1Ids, hizmetOrani = 3m }),
            HttpStatusCode.BadRequest, null, "hizmetOrani");

        // Yansıt (PARA): 54,08 + 1.024,85.
        var reflected = await Ok(await PostAsync(admin, "/hgs/gecisler/yansit", new { ids = r1Ids, hizmetOrani = 1.03m }));
        Assert.Equal(2, reflected.GetProperty("yansitilan").GetInt32());
        Assert.Equal(1078.93m, reflected.GetProperty("yansitilanTutar").GetDecimal());
        var replay = await Ok(await PostAsync(admin, "/hgs/gecisler/yansit", new { ids = r1Ids, hizmetOrani = 1.03m }));
        Assert.Equal(0, replay.GetProperty("yansitilan").GetInt32());
        Assert.Equal(2, replay.GetProperty("zatenYansitilmis").GetInt32());

        // Kira formu: bu kiranın geçişleri, durum + yansıtılan tutar.
        var rental = await Ok(await admin.C.GetAsync($"{V1}/kiralar/{e.World.R1}/cezalar"));
        var passages = rental.GetProperty("hgsGecisleri").EnumerateArray().ToList();
        Assert.Equal(2, passages.Count);
        Assert.All(passages, p => Assert.Equal("Yansitildi", p.GetProperty("durum").GetString()));
        Assert.Equal(new[] { 54.08m, 1024.85m }, passages.Select(p => p.GetProperty("yansitilanTutar").GetDecimal()).Order().ToArray());
        Assert.Equal("İzmit → Bursa", passages.Single(p => p.GetProperty("tutar").GetDecimal() == 995m).GetProperty("gecis").GetString());

        // Liste süzgeçleri: durum ve kira.
        Assert.Equal(2, (await ListAsync(admin, "&durum=Yansitildi")).Count);
        Assert.Equal(2, (await ListAsync(admin, $"&kiraId={e.World.R2}")).Count);
        await Problem(await admin.C.GetAsync(V1 + "/hgs/gecisler?durum=Yok"), HttpStatusCode.BadRequest, null, "durum");

        // Yeniden eşleştir: değişiklik yok.
        Assert.Equal(0, (await Ok(await PostAsync(admin, "/hgs/gecisler/yeniden-eslestir", new { }))).GetProperty("degisen").GetInt32());
    }

    /// <summary>
    /// #407 M1-d: olası mükerrer satırı "ayrı geçiş olarak onayla" HTTP üzerinden. İki ayrı dökümde aynı tünelden 3 saat
    /// arayla geçiş (R2, SubeA) → ikincisi Belirsiz. Başka şube 403, başka kiracı 404, boş istek errors[ids]; onay
    /// Eslesti + onayZamani, para yazmaz; ikisi birden yansıtılınca 2 × 12,40 = 24,80.
    /// </summary>
    [Fact]
    public async Task Confirm_near_duplicate_over_http()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        const string header = "Plaka;Çıkış İstasyonu;Çıkış Zamanı;Tutar";
        string Day3(int h) => HgsImportTests.At(3, h).ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        await Ok(await UploadAsync(admin, "/hgs/ice-aktar", $"{header}\n34 HGS 01;Avrasya Tüneli;{Day3(9)};12,40"));
        var second = await Ok(await UploadAsync(admin, "/hgs/ice-aktar", $"{header}\n34 HGS 01;Avrasya Tüneli;{Day3(12)};12,40"));
        Assert.Equal(1, second.GetProperty("olasiMukerrer").GetInt32());
        var pending = (await ListAsync(admin, "&durum=Belirsiz")).Single().GetProperty("id").GetGuid();

        await Problem(await PostAsync(await LoginAsync(e, Who.OperatorB), "/hgs/gecisler/onayla", new { ids = new[] { pending } }),
            HttpStatusCode.Forbidden, "yetki_yok");
        var other = await SetupAsync();
        await Problem(await PostAsync(await LoginAsync(other, Who.Admin), "/hgs/gecisler/onayla", new { ids = new[] { pending } }),
            HttpStatusCode.NotFound, null);
        await Problem(await PostAsync(admin, "/hgs/gecisler/onayla", new { ids = Array.Empty<Guid>() }), HttpStatusCode.BadRequest, null, "ids");

        var confirmed = await Ok(await PostAsync(admin, "/hgs/gecisler/onayla", new { ids = new[] { pending } }));
        Assert.Equal(1, confirmed.GetProperty("onaylanan").GetInt32());
        var rows = await ListAsync(admin);
        Assert.All(rows, r => Assert.Equal("Eslesti", r.GetProperty("durum").GetString()));
        Assert.Equal(JsonValueKind.String, rows.Single(r => r.GetProperty("id").GetGuid() == pending).GetProperty("onayZamani").ValueKind);

        var reflected = await Ok(await PostAsync(admin, "/hgs/gecisler/yansit",
            new { ids = rows.Select(r => r.GetProperty("id").GetGuid()).ToArray(), hizmetOrani = 1.00m }));
        Assert.Equal(24.80m, reflected.GetProperty("yansitilanTutar").GetDecimal());
    }
}
