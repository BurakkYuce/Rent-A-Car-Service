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
    private static readonly TabloDuzeniVerisi Ornek = new(
        [new("plaka", true, 120), new("musteri", true, null), new("tutar", false, 96)],
        [new("tutar", true), new("plaka", false)]);

    private AppDbContext OwnerDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.OwnerConnectionString).Options,
        NullTenantContext.Instance, NullCurrentUser.Instance);

    /// <summary>Users platform tablosu (owner yazar) — TabloDuzenleri.UserId FK'si gerçek satır ister.</summary>
    private async Task<Guid> KullaniciAsync(Guid tenant)
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

    private static TableLayoutService Servis(IServiceScope s) => s.ServiceProvider.GetRequiredService<TableLayoutService>();

    private static void DuzenEsit(TabloDuzeniVerisi beklenen, TabloDuzeniVerisi? gelen)
    {
        Assert.NotNull(gelen);
        Assert.Equal(beklenen.Sutunlar, gelen!.Sutunlar);   // record eşitliği: kod, görünür, genişlik + SIRA
        Assert.Equal(beklenen.Siralama, gelen.Siralama);
    }

    [Fact]
    public async Task Kaydet_getir_gidis_donus_ve_kayitsizken_null()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);
        using var s = host.ScopeFor(t, u, role: UserRole.Operator);

        var bos = await Servis(s).FetchAsync("kiralar.liste");
        Assert.Equal("kiralar.liste", bos.TabloKodu);
        Assert.Null(bos.Duzen);
        Assert.Null(bos.GuncellemeUtc);

        var kayit = await Servis(s).SaveAsync("kiralar.liste", Ornek);
        DuzenEsit(Ornek, kayit.Duzen);
        Assert.NotNull(kayit.GuncellemeUtc);

        var okunan = await Servis(s).FetchAsync("kiralar.liste");
        DuzenEsit(Ornek, okunan.Duzen);
        Assert.Equal(kayit.GuncellemeUtc, okunan.GuncellemeUtc); // µs'ye kırpıldı → DB'den aynı an döner

        // Başka tablo kodu ayrı düzendir.
        Assert.Null((await Servis(s).FetchAsync("kiralar.liste-2")).Duzen);
    }

    [Fact]
    public async Task Ikinci_kayit_ustune_yazar_tek_satir_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);
        using var s = host.ScopeFor(t, u, role: UserRole.Operator);

        await Servis(s).SaveAsync("araclar", Ornek);
        var yeni = new TabloDuzeniVerisi([new("musteri", true, 200), new("plaka", false, null)], []);
        await Servis(s).SaveAsync("araclar", yeni);

        DuzenEsit(yeni, (await Servis(s).FetchAsync("araclar")).Duzen);
        await using var db = await s.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.TabloDuzenleri.CountAsync(d => d.UserId == u && d.TabloKodu == "araclar"));
    }

    [Fact]
    public async Task Kullanici_izolasyonu_ayni_tenantta_baskasi_gormez_ve_ezemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var a = await KullaniciAsync(t);
        var b = await KullaniciAsync(t);

        using (var sa = host.ScopeFor(t, a, "a", UserRole.Admin))
            await Servis(sa).SaveAsync("cari.liste", Ornek);

        var bDuzeni = new TabloDuzeniVerisi([new("unvan", true, 300)], [new("unvan", false)]);
        using (var sb = host.ScopeFor(t, b, "b", UserRole.Operator))
        {
            Assert.Null((await Servis(sb).FetchAsync("cari.liste")).Duzen); // Admin'in düzeni operatöre SIZMAZ
            await Servis(sb).SaveAsync("cari.liste", bDuzeni);
            await Servis(sb).ResetAsync("cari.liste");                    // B'nin sıfırlaması A'ya dokunmaz
            Assert.Null((await Servis(sb).FetchAsync("cari.liste")).Duzen);
            await Servis(sb).SaveAsync("cari.liste", bDuzeni);
        }

        using (var sa = host.ScopeFor(t, a, "a", UserRole.Admin))
            DuzenEsit(Ornek, (await Servis(sa).FetchAsync("cari.liste")).Duzen);
        using (var sb = host.ScopeFor(t, b, "b", UserRole.Operator))
            DuzenEsit(bDuzeni, (await Servis(sb).FetchAsync("cari.liste")).Duzen);
    }

    [Fact]
    public async Task Tenant_izolasyonu_racar_app_ham_sql_dahil()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var u1 = await KullaniciAsync(t1);
        var u2 = await KullaniciAsync(t2);

        using (var s1 = host.ScopeFor(t1, u1))
            await Servis(s1).SaveAsync("rlstest", Ornek);

        using (var s2 = host.ScopeFor(t2, u2))
        {
            Assert.Null((await Servis(s2).FetchAsync("rlstest")).Duzen);
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
        await using (var sil = new NpgsqlCommand("delete from \"TabloDuzenleri\" where \"TenantId\" = @a", conn))
        {
            sil.Parameters.AddWithValue("a", t1);
            Assert.Equal(0, await sil.ExecuteNonQueryAsync());
        }

        using (var s1 = host.ScopeFor(t1, u1))
            DuzenEsit(Ornek, (await Servis(s1).FetchAsync("rlstest")).Duzen); // T1 düzeni sağlam
    }

    [Fact]
    public async Task Sifirla_varsayilana_doner_kayitsizken_de_hatasiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);
        using var s = host.ScopeFor(t, u);

        await Servis(s).ResetAsync("hic-yok");       // kayıt yokken no-op
        await Servis(s).SaveAsync("faturalar", Ornek);
        await Servis(s).ResetAsync("faturalar");
        var sonuc = await Servis(s).FetchAsync("faturalar");
        Assert.Null(sonuc.Duzen);
        Assert.Null(sonuc.GuncellemeUtc);
    }

    [Fact]
    public async Task Esz_zamanli_ilk_yazim_tek_satir_ve_hata_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);

        for (var tur = 0; tur < 5; tur++)
        {
            var kod = "yaris" + tur;
            var gorevler = Enumerable.Range(0, 4).Select(async i =>
            {
                using var s = host.ScopeFor(t, u);
                await Servis(s).SaveAsync(kod, new TabloDuzeniVerisi([new("k" + i, true, null)], []));
            });
            await Task.WhenAll(gorevler); // UniqueViolation sızmaz: kaybeden güncellemeye döner

            using var oku = host.ScopeFor(t, u);
            var duzen = (await Servis(oku).FetchAsync(kod)).Duzen;
            Assert.NotNull(duzen);
            Assert.Single(duzen!.Sutunlar);
            await using var db = await oku.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            Assert.Equal(1, await db.TabloDuzenleri.CountAsync(d => d.TabloKodu == kod));
        }
    }

    [Fact]
    public async Task Oturumsuz_ya_da_tenantsiz_cagri_yetki_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using (var s = host.ScopeFor(Guid.NewGuid(), userId: null))
        {
            await Assert.ThrowsAsync<NoPermissionException>(() => Servis(s).FetchAsync("kiralar"));
            await Assert.ThrowsAsync<NoPermissionException>(() => Servis(s).SaveAsync("kiralar", Ornek));
            await Assert.ThrowsAsync<NoPermissionException>(() => Servis(s).ResetAsync("kiralar"));
        }
        using (var s = host.ScopeFor(tenantId: null, userId: Guid.NewGuid()))
            await Assert.ThrowsAsync<NoPermissionException>(() => Servis(s).FetchAsync("kiralar"));
    }

    public static TheoryData<string, TabloDuzeniVerisi?, string> GecersizGovdeler()
    {
        TabloSutunDuzeni S(string kod, int? g = null) => new(kod, true, g);
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
    [MemberData(nameof(GecersizGovdeler))]
    public async Task Gecersiz_govde_400_alanli_ve_yazilmaz(string ad, TabloDuzeniVerisi? govde, string alan)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);
        using var s = host.ScopeFor(t, u);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Servis(s).SaveAsync("dogrulama", govde));
        Assert.IsNotType<NoPermissionException>(ex);
        Assert.True(alan == ex.Alan, $"{ad}: beklenen alan {alan}, gelen {ex.Alan}");
        Assert.Null((await Servis(s).FetchAsync("dogrulama")).Duzen);
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
    public async Task Gecersiz_tablo_kodu_400(string kod)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);
        using var s = host.ScopeFor(t, u);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Servis(s).FetchAsync(kod));
        Assert.Equal("tabloKodu", ex.Alan);
        await Assert.ThrowsAsync<ValidationException>(() => Servis(s).SaveAsync(kod, Ornek));
    }

    [Fact]
    public async Task Sinirlar_kabul_edilir_200_sutun_5_siralama_24_ve_2000_px_64_karakter()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t = Guid.NewGuid();
        var u = await KullaniciAsync(t);
        using var s = host.ScopeFor(t, u);

        var sutunlar = Enumerable.Range(0, 200).Select(i => new TabloSutunDuzeni("s" + i, i % 2 == 0, i == 0 ? 24 : i == 1 ? 2000 : null)).ToList();
        var siralama = Enumerable.Range(0, 5).Select(i => new TabloSiralamaDuzeni("s" + i, i % 2 == 1)).ToList();
        var kod = new string('a', 64);
        var duzen = new TabloDuzeniVerisi(sutunlar, siralama);

        await Servis(s).SaveAsync(kod, duzen);
        DuzenEsit(duzen, (await Servis(s).FetchAsync(kod)).Duzen);
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
    private const string Kok = "/api/ui/v1/tablo-duzenleri/";

    private static string Kod() => "w" + Guid.NewGuid().ToString("N")[..12];

    private static string? CerezDegeri(HttpResponseMessage r, string ad)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var degerler)) return null;
        foreach (var d in degerler)
            if (d.StartsWith(ad + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(ad.Length + 1)..].Split(';')[0]);
        return null;
    }

    private static HttpRequestMessage Istek(HttpMethod m, string url, string? xsrf, object? govde = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        return req;
    }

    /// <summary>Giriş yapar; (istemci, girişten SONRAKİ XSRF belirteci) döner. Kimlik fixture'da çalışma anında üretilir.</summary>
    private async Task<(HttpClient C, string Xsrf)> GirisYap(TestKimlik k)
    {
        var c = fx.Web.Istemci();
        var r0 = await c.GetAsync("/api/ui/v1/oturum/xsrf");
        var once = CerezDegeri(r0, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("XSRF-TOKEN yok");
        var r = await c.SendAsync(Istek(HttpMethod.Post, "/api/ui/v1/oturum/giris", once,
            new { firma = k.Firma, kullanici = k.Kullanici, sifre = k.Sifre }));
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (c, CerezDegeri(r, "XSRF-TOKEN") ?? throw new Xunit.Sdk.XunitException("girişte XSRF yenilenmedi"));
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task ProblemBekle(HttpResponseMessage r, HttpStatusCode durum, string kod)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal(kod, JsonDocument.Parse(metin).RootElement.GetProperty("kod").GetString());
    }

    private static readonly object OrnekGovde = new
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
        var (c, xsrf) = await GirisYap(fx.PilotAdmin);
        var kod = Kod();

        var bos = await c.GetAsync(Kok + kod);
        Assert.Equal(HttpStatusCode.OK, bos.StatusCode);
        Assert.True(bos.Headers.CacheControl?.NoStore == true);
        var bj = await Json(bos);
        Assert.Equal(kod, bj.GetProperty("tabloKodu").GetString());
        Assert.Equal(JsonValueKind.Null, bj.GetProperty("duzen").ValueKind);
        Assert.Equal(JsonValueKind.Null, bj.GetProperty("guncellemeUtc").ValueKind);

        var put = await c.SendAsync(Istek(HttpMethod.Put, Kok + kod, xsrf, OrnekGovde));
        Assert.True(put.StatusCode == HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var pj = await Json(put);

        var get = await c.GetAsync(Kok + kod);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var gj = await Json(get);
        Assert.Equal(pj.GetProperty("guncellemeUtc").GetDateTimeOffset(), gj.GetProperty("guncellemeUtc").GetDateTimeOffset());

        var duzen = gj.GetProperty("duzen");
        var sutunlar = duzen.GetProperty("sutunlar").EnumerateArray().ToList();
        Assert.Equal(2, sutunlar.Count);
        Assert.Equal("plaka", sutunlar[0].GetProperty("kod").GetString());
        Assert.True(sutunlar[0].GetProperty("gorunur").GetBoolean());
        Assert.Equal(120, sutunlar[0].GetProperty("genislik").GetInt32());
        Assert.False(sutunlar[0].TryGetProperty("html", out _)); // temiz kopya saklanır
        Assert.Equal("tutar", sutunlar[1].GetProperty("kod").GetString());
        Assert.False(sutunlar[1].GetProperty("gorunur").GetBoolean());
        Assert.Equal(JsonValueKind.Null, sutunlar[1].GetProperty("genislik").ValueKind);
        var siralama = duzen.GetProperty("siralama").EnumerateArray().ToList();
        Assert.Single(siralama);
        Assert.Equal("tutar", siralama[0].GetProperty("kod").GetString());
        Assert.True(siralama[0].GetProperty("azalan").GetBoolean());

        var sil = await c.SendAsync(Istek(HttpMethod.Delete, Kok + kod, xsrf));
        Assert.Equal(HttpStatusCode.NoContent, sil.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Json(await c.GetAsync(Kok + kod))).GetProperty("duzen").ValueKind);
        // Kayıt yokken DELETE de 204.
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(Istek(HttpMethod.Delete, Kok + kod, xsrf))).StatusCode);
    }

    [Fact]
    public async Task Put_ve_delete_xsrf_basligi_ister()
    {
        var (c, _) = await GirisYap(fx.PilotAdmin);
        var kod = Kod();
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Put, Kok + kod, null, OrnekGovde)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Delete, Kok + kod, null)),
            HttpStatusCode.BadRequest, "xsrf_gecersiz");
        Assert.Equal(JsonValueKind.Null, (await Json(await c.GetAsync(Kok + kod))).GetProperty("duzen").ValueKind);
    }

    [Fact]
    public async Task Gecersiz_govde_ve_kod_400_dogrulama()
    {
        var (c, xsrf) = await GirisYap(fx.PilotAdmin);
        var kod = Kod();

        var tekrar = new { sutunlar = new[] { new { kod = "a", gorunur = true }, new { kod = "a", gorunur = true } }, siralama = Array.Empty<object>() };
        var r = await c.SendAsync(Istek(HttpMethod.Put, Kok + kod, xsrf, tekrar));
        await ProblemBekle(r, HttpStatusCode.BadRequest, "dogrulama");
        Assert.True((await Json(r)).GetProperty("errors").TryGetProperty("sutunlar", out _));

        var genis = new { sutunlar = new[] { new { kod = "a", gorunur = true, genislik = 5000 } }, siralama = Array.Empty<object>() };
        await ProblemBekle(await c.SendAsync(Istek(HttpMethod.Put, Kok + kod, xsrf, genis)), HttpStatusCode.BadRequest, "dogrulama");

        await ProblemBekle(await c.GetAsync(Kok + "Buyuk-Harf"), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(JsonValueKind.Null, (await Json(await c.GetAsync(Kok + kod))).GetProperty("duzen").ValueKind);
    }

    [Fact]
    public async Task Ayni_firmada_baska_kullanici_ve_baska_firma_duzeni_gormez()
    {
        var kod = Kod();
        var (admin, ax) = await GirisYap(fx.PilotAdmin);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(Istek(HttpMethod.Put, Kok + kod, ax, OrnekGovde))).StatusCode);

        // Aynı firmada operatör: kendi düzeni yok → null; kendi PUT'u admin'inkini ezmez.
        var (op, ox) = await GirisYap(fx.PilotOperator);
        Assert.Equal(JsonValueKind.Null, (await Json(await op.GetAsync(Kok + kod))).GetProperty("duzen").ValueKind);
        var opGovde = new { sutunlar = new[] { new { kod = "unvan", gorunur = true } }, siralama = Array.Empty<object>() };
        Assert.Equal(HttpStatusCode.OK, (await op.SendAsync(Istek(HttpMethod.Put, Kok + kod, ox, opGovde))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await op.SendAsync(Istek(HttpMethod.Delete, Kok + kod, ox))).StatusCode);

        var adminDuzeni = (await Json(await admin.GetAsync(Kok + kod))).GetProperty("duzen");
        Assert.Equal("plaka", adminDuzeni.GetProperty("sutunlar")[0].GetProperty("kod").GetString());

        // Başka pilot firma: aynı tablo kodu, düzen yok.
        var diger = await fx.FirmaVeKullaniciAsync("Tablo Diğer Pilot");
        await fx.PilotYapAsync(await fx.TenantIdAsync(diger.Firma), true);
        var (d, _) = await GirisYap(diger);
        Assert.Equal(JsonValueKind.Null, (await Json(await d.GetAsync(Kok + kod))).GetProperty("duzen").ValueKind);
    }

    [Fact]
    public async Task Oturumsuz_401_pilot_olmayan_403()
    {
        var anonim = await fx.Web.Istemci().GetAsync(Kok + "kiralar");
        await ProblemBekle(anonim, HttpStatusCode.Unauthorized, "oturum_yok");

        var (c, _) = await GirisYap(fx.DigerAdmin);
        await ProblemBekle(await c.GetAsync(Kok + "kiralar"), HttpStatusCode.Forbidden, "pilot_degil");
    }
}
