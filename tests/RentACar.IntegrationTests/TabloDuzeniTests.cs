using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RentACar.Application.Common;
using RentACar.Application.TabloDuzenleri;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F3.5 — kişisel tablo düzenleri (servis katmanı, racar_app + FORCE RLS): gidiş-dönüş, upsert,
/// KULLANICI izolasyonu (aynı tenant'ta başka kullanıcı görmez/ezmez), TENANT izolasyonu (ham SQL dahil),
/// doğrulama ve oturum guard'ı. BAĞIMSIZ ORACLE: beklenen düzenler elle yazılmış sabitlerdir.
/// </summary>
[Collection("postgres")]
public sealed class TabloDuzeniTests(PostgresFixture fx)
{
    private static readonly TabloDuzeniVerisi Sample = new(
        [new("plaka", true, 120), new("musteri", true, null), new("tutar", false, 96)],
        [new("tutar", true), new("plaka", false)]);

    private AppDbContext OwnerDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    /// <summary>Users platform tablosu (owner yazar) — TabloDuzenleri.UserId FK'si gerçek satır ister.</summary>
    private async Task<Guid> UserAsync(Guid tenant)
    {
        var u = new User
        {
            TenantId = tenant, UserName = "td" + Guid.NewGuid().ToString("N")[..10],
            DisplayName = "Tablo Test", PasswordHash = "x", Rol = UserRole.Operator,
        };
        await using var db = OwnerDb();
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    private static TableLayoutService Service(IServiceScope s) => s.ServiceProvider.GetRequiredService<TableLayoutService>();

    private static void LayoutEquals(TabloDuzeniVerisi expected, TabloDuzeniVerisi? incoming)
    {
        Assert.NotNull(incoming);
        Assert.Equal(expected.Sutunlar, incoming!.Sutunlar);   // record eşitliği: kod, görünür, genişlik + SIRA
        Assert.Equal(expected.Siralama, incoming.Siralama);
    }

    [Fact]
    public async Task Kaydet_getir_gidis_donus_ve_kayitsizken_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);
        using var s = host.ScopeFor(t, u, role: UserRole.Operator);

        var empty = await Service(s).FetchAsync("kiralar.liste");
        Assert.Equal("kiralar.liste", empty.TabloKodu);
        Assert.Null(empty.Duzen);
        Assert.Null(empty.GuncellemeUtc);

        var record = await Service(s).SaveAsync("kiralar.liste", Sample);
        LayoutEquals(Sample, record.Duzen);
        Assert.NotNull(record.GuncellemeUtc);

        var readValue = await Service(s).FetchAsync("kiralar.liste");
        LayoutEquals(Sample, readValue.Duzen);
        Assert.Equal(record.GuncellemeUtc, readValue.GuncellemeUtc); // µs'ye kırpıldı → DB'den aynı an döner

        // Başka tablo kodu ayrı düzendir.
        Assert.Null((await Service(s).FetchAsync("kiralar.liste-2")).Duzen);
    }

    [Fact]
    public async Task Ikinci_kayit_ustune_yazar_tek_satir_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);
        using var s = host.ScopeFor(t, u, role: UserRole.Operator);

        await Service(s).SaveAsync("araclar", Sample);
        var newItem = new TabloDuzeniVerisi([new("musteri", true, 200), new("plaka", false, null)], []);
        await Service(s).SaveAsync("araclar", newItem);

        LayoutEquals(newItem, (await Service(s).FetchAsync("araclar")).Duzen);
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.TabloDuzenleri.CountAsync(d => d.UserId == u && d.TabloKodu == "araclar"));
    }

    [Fact]
    public async Task Kullanici_izolasyonu_ayni_tenantta_baskasi_gormez_ve_ezemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var a = await UserAsync(t);
        var b = await UserAsync(t);

        using (var sa = host.ScopeFor(t, a, "a", UserRole.Admin))
            await Service(sa).SaveAsync("cari.liste", Sample);

        var bLayout = new TabloDuzeniVerisi([new("unvan", true, 300)], [new("unvan", false)]);
        using (var sb = host.ScopeFor(t, b, "b", UserRole.Operator))
        {
            Assert.Null((await Service(sb).FetchAsync("cari.liste")).Duzen); // Admin'in düzeni operatöre SIZMAZ
            await Service(sb).SaveAsync("cari.liste", bLayout);
            await Service(sb).ResetAsync("cari.liste");                    // B'nin sıfırlaması A'ya dokunmaz
            Assert.Null((await Service(sb).FetchAsync("cari.liste")).Duzen);
            await Service(sb).SaveAsync("cari.liste", bLayout);
        }

        using (var sa = host.ScopeFor(t, a, "a", UserRole.Admin))
            LayoutEquals(Sample, (await Service(sa).FetchAsync("cari.liste")).Duzen);
        using (var sb = host.ScopeFor(t, b, "b", UserRole.Operator))
            LayoutEquals(bLayout, (await Service(sb).FetchAsync("cari.liste")).Duzen);
    }

    [Fact]
    public async Task Tenant_izolasyonu_racar_app_ham_sql_dahil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var u1 = await UserAsync(t1);
        var u2 = await UserAsync(t2);

        using (var s1 = host.ScopeFor(t1, u1))
            await Service(s1).SaveAsync("rlstest", Sample);

        using (var s2 = host.ScopeFor(t2, u2))
        {
            Assert.Null((await Service(s2).FetchAsync("rlstest")).Duzen);
            var factory = s2.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            // EF filtresi atlansa da (ham SQL + IgnoreQueryFilters) RLS T1 satırını gizler.
            Assert.Equal(0, await db.TabloDuzenleri.FromSqlRaw("SELECT * FROM \"TabloDuzenleri\"").IgnoreQueryFilters().CountAsync());
            Assert.Equal(0L, await db.Database.SqlQueryRaw<long>("SELECT count(*)::bigint AS \"Value\" FROM \"TabloDuzenleri\"").SingleAsync());
            // T1'in kullanıcısı adına bile olsa T2 GUC'uyla T1 satırı yazılamaz (WITH CHECK).
            db.TabloDuzenleri.Add(new TabloDuzeni { TenantId = t1, UserId = u1, TabloKodu = "sizma", Duzen = "{}" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var conn = new NpgsqlConnection(fx.AppConnectionString);
        await conn.OpenAsync();
        await using (var kat = new NpgsqlCommand(
            "select relrowsecurity, relforcerowsecurity from pg_class where relname = 'TabloDuzenleri'", conn))
        await using (var r = await kat.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            Assert.True(r.GetBoolean(0), "ENABLE ROW LEVEL SECURITY yok");
            Assert.True(r.GetBoolean(1), "FORCE ROW LEVEL SECURITY yok");
        }
        // GUC yokken (varsayılan-ret) hiçbir satır görünmez; T2 GUC'uyla T1 satırı ne okunur ne silinir.
        await using (var say = new NpgsqlCommand("select count(*) from \"TabloDuzenleri\" where \"TenantId\" = @a", conn))
        {
            say.Parameters.AddWithValue("a", t1);
            Assert.Equal(0L, (long)(await say.ExecuteScalarAsync())!);
        }
        await using (var set = new NpgsqlCommand("select set_config('app.tenant_id', @t, false)", conn))
        {
            set.Parameters.AddWithValue("t", t2.ToString());
            await set.ExecuteScalarAsync();
        }
        await using (var remove = new NpgsqlCommand("delete from \"TabloDuzenleri\" where \"TenantId\" = @a", conn))
        {
            remove.Parameters.AddWithValue("a", t1);
            Assert.Equal(0, await remove.ExecuteNonQueryAsync());
        }

        using (var s1 = host.ScopeFor(t1, u1))
            LayoutEquals(Sample, (await Service(s1).FetchAsync("rlstest")).Duzen); // T1 düzeni sağlam
    }

    [Fact]
    public async Task Sifirla_varsayilana_doner_kayitsizken_de_hatasiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);
        using var s = host.ScopeFor(t, u);

        await Service(s).ResetAsync("hic-yok");       // kayıt yokken no-op
        await Service(s).SaveAsync("faturalar", Sample);
        await Service(s).ResetAsync("faturalar");
        var result = await Service(s).FetchAsync("faturalar");
        Assert.Null(result.Duzen);
        Assert.Null(result.GuncellemeUtc);
    }

    [Fact]
    public async Task Esz_zamanli_ilk_yazim_tek_satir_ve_hata_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);

        for (var type = 0; type < 5; type++)
        {
            var code = "yaris" + type;
            var tasks = Enumerable.Range(0, 4).Select(async i =>
            {
                using var s = host.ScopeFor(t, u);
                await Service(s).SaveAsync(code, new TabloDuzeniVerisi([new("k" + i, true, null)], []));
            });
            await Task.WhenAll(tasks); // UniqueViolation sızmaz: kaybeden güncellemeye döner

            using var read = host.ScopeFor(t, u);
            var layout = (await Service(read).FetchAsync(code)).Duzen;
            Assert.NotNull(layout);
            Assert.Single(layout!.Sutunlar);
            await using var db = await read.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            Assert.Equal(1, await db.TabloDuzenleri.CountAsync(d => d.TabloKodu == code));
        }
    }

    [Fact]
    public async Task Oturumsuz_ya_da_tenantsiz_cagri_yetki_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s = host.ScopeFor(Guid.NewGuid(), userId: null))
        {
            await Assert.ThrowsAsync<NoPermissionException>(() => Service(s).FetchAsync("kiralar"));
            await Assert.ThrowsAsync<NoPermissionException>(() => Service(s).SaveAsync("kiralar", Sample));
            await Assert.ThrowsAsync<NoPermissionException>(() => Service(s).ResetAsync("kiralar"));
        }
        using (var s = host.ScopeFor(tenantId: null, userId: Guid.NewGuid()))
            await Assert.ThrowsAsync<NoPermissionException>(() => Service(s).FetchAsync("kiralar"));
    }

    public static TheoryData<string, TabloDuzeniVerisi?, string> InvalidBodies()
    {
        TabloSutunDuzeni S(string code, int? g = null) => new(code, true, g);
        var cok = Enumerable.Range(0, 201).Select(i => S("s" + i)).ToList();
        return new()
        {
            { "govde-null", null, "duzen" },
            { "sutun-yok", new([], []), "sutunlar" },
            { "sutunlar-null", new(null!, []), "sutunlar" },
            { "siralama-null", new([S("a")], null!), "siralama" },
            { "201-sutun", new(cok, []), "sutunlar" },
            { "tekrar-kod", new([S("a"), S("a")], []), "sutunlar" },
            { "kod-bosluk", new([S("a b")], []), "sutunlar" },
            { "kod-bos", new([S("")], []), "sutunlar" },
            { "kod-65", new([S(new string('x', 65))], []), "sutunlar" },
            { "kod-sonda-satir", new([S("a\n")], []), "sutunlar" },
            { "genislik-23", new([S("a", 23)], []), "sutunlar" },
            { "genislik-2001", new([S("a", 2001)], []), "sutunlar" },
            { "genislik-negatif", new([S("a", -5)], []), "sutunlar" },
            { "siralama-bilinmeyen", new([S("a")], [new("b", false)]), "siralama" },
            { "siralama-tekrar", new([S("a")], [new("a", false), new("a", true)]), "siralama" },
            { "siralama-6", new(Enumerable.Range(0, 6).Select(i => S("s" + i)).ToList(),
                Enumerable.Range(0, 6).Select(i => new TabloSiralamaDuzeni("s" + i, false)).ToList()), "siralama" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Gecersiz_govde_400_alanli_ve_yazilmaz(string name, TabloDuzeniVerisi? body, string alan)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);
        using var s = host.ScopeFor(t, u);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service(s).SaveAsync("dogrulama", body));
        Assert.IsNotType<NoPermissionException>(ex);
        Assert.True(alan == ex.Alan, $"{name}: beklenen alan {alan}, gelen {ex.Alan}");
        Assert.Null((await Service(s).FetchAsync("dogrulama")).Duzen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Kiralar")]          // büyük harf
    [InlineData("kiralar liste")]
    [InlineData(".kiralar")]
    [InlineData("kiralar.")]
    [InlineData("kira..liste")]
    [InlineData("kiralar\n")]
    [InlineData("çıkış")]            // ASCII dışı
    public async Task Gecersiz_tablo_kodu_400(string code)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);
        using var s = host.ScopeFor(t, u);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Service(s).FetchAsync(code));
        Assert.Equal("tabloKodu", ex.Alan);
        await Assert.ThrowsAsync<ValidationException>(() => Service(s).SaveAsync(code, Sample));
    }

    [Fact]
    public async Task Sinirlar_kabul_edilir_200_sutun_5_siralama_24_ve_2000_px_64_karakter()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await UserAsync(t);
        using var s = host.ScopeFor(t, u);

        var columns = Enumerable.Range(0, 200).Select(i => new TabloSutunDuzeni("s" + i, i % 2 == 0, i == 0 ? 24 : i == 1 ? 2000 : null)).ToList();
        var sort = Enumerable.Range(0, 5).Select(i => new TabloSiralamaDuzeni("s" + i, i % 2 == 1)).ToList();
        var code = new string('a', 64);
        var layout = new TabloDuzeniVerisi(columns, sort);

        await Service(s).SaveAsync(code, layout);
        LayoutEquals(layout, (await Service(s).FetchAsync(code)).Duzen);
    }
}

/// <summary>
/// F3.5 — <c>/api/ui/v1/tablo-duzenleri/{tabloKodu}</c> GERÇEK Web boru hattında: JSON sözleşmesi (camelCase,
/// kayıtsızken <c>duzen: null</c> — 404 değil), PUT upsert + CSRF zorunluluğu, 400 <c>dogrulama</c>, DELETE 204,
/// kullanıcı ve firma izolasyonu, bilinmeyen alanların SAKLANMADIĞI. Beklenen değerler elle yazılmıştır.
/// </summary>
[Collection("web")]
public sealed class TabloDuzeniApiTests(WebFixture fx)
{
    private const string Root = "/api/ui/v1/tablo-duzenleri/";

    private static string Code() => "w" + Guid.NewGuid().ToString("N")[..12];

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static HttpRequestMessage Request(HttpMethod m, string url, string? xsrf, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>Giriş yapar; (istemci, girişten SONRAKİ XSRF belirteci) döner. Kimlik fixture'da çalışma anında üretilir.</summary>
    private async Task<(HttpClient C, string Xsrf)> Login(TestKimlik k)
    {
        var c = fx.Web.Client();
        var r0 = await c.GetAsync("/api/ui/v1/oturum/xsrf");
        var once = CookieValue(r0, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF-TOKEN yok");
        var r = await c.SendAsync(Request(HttpMethod.Post, "/api/ui/v1/oturum/giris", once,
            new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre }));
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (c, CookieValue(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("girişte XSRF yenilenmedi"));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task ExpectProblem(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, JsonDocument.Parse(text).RootElement.GetProperty("kod").GetString());
    }

    private static readonly object SampleBody = new
    {
        sutunlar = new object[]
        {
            new { kod = "plaka", gorunur = true, genislik = 120, html = "<script>" }, // bilinmeyen alan saklanmamalı
            new { kod = "tutar", gorunur = false, genislik = (int?)null },
        },
        siralama = new object[] { new { kod = "tutar", azalan = true } },
    };

    [Fact]
    public async Task Get_kayitsiz_null_put_sonra_get_ayni_duzen_ve_delete_204()
    {
        var (c, xsrf) = await Login(fx.PilotAdmin);
        var code = Code();

        var empty = await c.GetAsync(Root + code);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.True(empty.Headers.CacheControl?.NoStore == true);
        var bj = await Json(empty);
        Assert.Equal(code, bj.GetProperty("tabloKodu").GetString());
        Assert.Equal(JsonValueKind.Null, bj.GetProperty("duzen").ValueKind);
        Assert.Equal(JsonValueKind.Null, bj.GetProperty("guncellemeUtc").ValueKind);

        var put = await c.SendAsync(Request(HttpMethod.Put, Root + code, xsrf, SampleBody));
        Assert.True(put.StatusCode == HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var pj = await Json(put);

        var get = await c.GetAsync(Root + code);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var gj = await Json(get);
        Assert.Equal(pj.GetProperty("guncellemeUtc").GetDateTimeOffset(), gj.GetProperty("guncellemeUtc").GetDateTimeOffset());

        var layout = gj.GetProperty("duzen");
        var columns = layout.GetProperty("sutunlar").EnumerateArray().ToList();
        Assert.Equal(2, columns.Count);
        Assert.Equal("plaka", columns[0].GetProperty("kod").GetString());
        Assert.True(columns[0].GetProperty("gorunur").GetBoolean());
        Assert.Equal(120, columns[0].GetProperty("genislik").GetInt32());
        Assert.False(columns[0].TryGetProperty("html", out _)); // temiz kopya saklanır
        Assert.Equal("tutar", columns[1].GetProperty("kod").GetString());
        Assert.False(columns[1].GetProperty("gorunur").GetBoolean());
        Assert.Equal(JsonValueKind.Null, columns[1].GetProperty("genislik").ValueKind);
        var sort = layout.GetProperty("siralama").EnumerateArray().ToList();
        Assert.Single(sort);
        Assert.Equal("tutar", sort[0].GetProperty("kod").GetString());
        Assert.True(sort[0].GetProperty("azalan").GetBoolean());

        var remove = await c.SendAsync(Request(HttpMethod.Delete, Root + code, xsrf));
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await c.GetAsync(Root + code))).GetProperty("duzen").ValueKind);
        // Kayıt yokken DELETE de 204.
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(Request(HttpMethod.Delete, Root + code, xsrf))).StatusCode);
    }

    [Fact]
    public async Task Put_ve_delete_xsrf_basligi_ister()
    {
        var (c, _) = await Login(fx.PilotAdmin);
        var code = Code();
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Put, Root + code, null, SampleBody)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Delete, Root + code, null)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Equal(JsonValueKind.Null, (await Json(await c.GetAsync(Root + code))).GetProperty("duzen").ValueKind);
    }

    [Fact]
    public async Task Gecersiz_govde_ve_kod_400_dogrulama()
    {
        var (c, xsrf) = await Login(fx.PilotAdmin);
        var code = Code();

        var repeat = new { sutunlar = new[] { new { kod = "a", gorunur = true }, new { kod = "a", gorunur = true } }, siralama = Array.Empty<object>() };
        var r = await c.SendAsync(Request(HttpMethod.Put, Root + code, xsrf, repeat));
        await ExpectProblem(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.True((await Json(r)).GetProperty("errors").TryGetProperty("sutunlar", out _));

        var wide = new { sutunlar = new[] { new { kod = "a", gorunur = true, genislik = 5000 } }, siralama = Array.Empty<object>() };
        await ExpectProblem(await c.SendAsync(Request(HttpMethod.Put, Root + code, xsrf, wide)), HttpStatusCode.BadRequest, "dogrulama");

        await ExpectProblem(await c.GetAsync(Root + "Buyuk-Harf"), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(JsonValueKind.Null, (await Json(await c.GetAsync(Root + code))).GetProperty("duzen").ValueKind);
    }

    [Fact]
    public async Task Ayni_firmada_baska_kullanici_ve_baska_firma_duzeni_gormez()
    {
        var code = Code();
        var (admin, ax) = await Login(fx.PilotAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Request(HttpMethod.Put, Root + code, ax, SampleBody))).StatusCode);

        // Aynı firmada operatör: kendi düzeni yok → null; kendi PUT'u admin'inkini ezmez.
        var (op, ox) = await Login(fx.PilotOperator);
        Assert.Equal(JsonValueKind.Null, (await Json(await op.GetAsync(Root + code))).GetProperty("duzen").ValueKind);
        var opBody = new { sutunlar = new[] { new { kod = "unvan", gorunur = true } }, siralama = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.OK, (await op.SendAsync(Request(HttpMethod.Put, Root + code, ox, opBody))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await op.SendAsync(Request(HttpMethod.Delete, Root + code, ox))).StatusCode);

        var adminLayout = (await Json(await admin.GetAsync(Root + code))).GetProperty("duzen");
        Assert.Equal("plaka", adminLayout.GetProperty("sutunlar")[0].GetProperty("kod").GetString());

        // Başka pilot firma: aynı tablo kodu, düzen yok.
        var other = await fx.CompanyAndUserAsync("Tablo Diğer Pilot");
        await fx.MakePilotAsync(await fx.TenantIdAsync(other.Firma), true);
        var (d, _) = await Login(other);
        Assert.Equal(JsonValueKind.Null, (await Json(await d.GetAsync(Root + code))).GetProperty("duzen").ValueKind);
    }

    [Fact]
    public async Task Oturumsuz_401_pilot_kapisi_yok()
    {
        var anonymous = await fx.Web.Client().GetAsync(Root + "kiralar");
        await ExpectProblem(anonymous, HttpStatusCode.Unauthorized, "oturum_yok");

        // F13.1b: pilot kapısı kalktı — eski pilot olmayan firma da kendi düzenini okur.
        var (c, _) = await Login(fx.OtherAdmin);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Root + "kiralar")).StatusCode);
    }
}
