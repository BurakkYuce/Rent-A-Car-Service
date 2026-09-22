using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Kur;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.4 — <c>/api/ui/v1/finans/*</c> (sabit panel para işlemleri) GERÇEK Web boru hattında: defter dengesi,
/// kira Tahsilat/Bakiye etkisi, F1.4 envanterine göre çift gönderim (E01/E02 409, E09/E12 aynı içerik sessiz +
/// farklı içerik 409, E15/E34 400, E18/E19 sessiz), deterministik <c>tahsilatAnahtar</c> önceliği, dar izin
/// (FinanceReverse), şube kapsamı, çok dövizli tahsilat (açık ve çözülen kur) ve alan hataları.
/// <para><b>BAĞIMSIZ ORACLE:</b> beklenen tutarlar elle kurulmuş senaryodan (3 gün × 100 = 300 brüt → net 250 +
/// KDV 50; 10 USD × 32 = 320; 1000 bedel × %10 = 100 komisyon; 31/90 × 9000 = 3100) — servis/rapor kodundan
/// ÜRETİLMEZ. Kullanıcı adları ve parolalar ÇALIŞMA ANINDA rastgele üretilir.</para>
/// </summary>
[Collection("web")]
public sealed class UiFinansApiTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private static readonly DateTimeOffset KiraBas = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ ortam

    private enum Kim { Admin, Muhasebe, MuhasebeTersYasak, OperatorA, OperatorB, OperatorDuz }

    private sealed class Ortam
    {
        public required Guid TenantId { get; init; }
        public required string Kod { get; init; }
        public required string Sifre { get; init; }
        public required Dictionary<Kim, string> Kullanicilar { get; init; }
        public required Guid Musteri { get; init; }
        public required Guid Tedarikci { get; init; }
        public required Guid Kira { get; init; }       // SubeA, 3 gün × 100 = 300 TRY
        public required Guid KiraArac { get; init; }
    }

    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];
    private static string YeniAnahtar() => Guid.NewGuid().ToString("N"); // 32 görünür ASCII

    /// <summary>Firma + her rolden kullanıcı (rastgele ad/parola) + istisnalar; kiracı verisi racar_app ile.</summary>
    private async Task<Ortam> OrtamKurAsync(Func<IServiceProvider, Task>? ek = null)
    {
        var tenantId = Guid.NewGuid();
        var kod = Rastgele("f44");
        var sifre = WebFixture.RastgeleParola();
        var kullanicilar = Enum.GetValues<Kim>().ToDictionary(k => k, _ => Rastgele("u"));

        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = kod, Name = kod, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (kim, ad) in kullanicilar)
            {
                var (rol, sube) = kim switch
                {
                    Kim.Admin => (UserRole.Admin, (string?)null),
                    Kim.Muhasebe or Kim.MuhasebeTersYasak => (UserRole.Muhasebe, null),
                    Kim.OperatorA or Kim.OperatorDuz => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Operator, "SubeB"),
                };
                var u = new User { TenantId = tenantId, UserName = ad, DisplayName = ad, Rol = rol, AtanmisSube = sube, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, sifre);
                db.Users.Add(u);
                // Operatörlere kullanıcı-bazlı FinanceWrite: şube kapsamını izin kapısından AYRI sınamak için.
                if (kim is Kim.OperatorA or Kim.OperatorB)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceWrite", Ver = true });
                if (kim == Kim.MuhasebeTersYasak)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceReverse", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(tenantId, true);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(tenantId, role: UserRole.Admin);
        var sp = s.ServiceProvider;
        if (ek is not null) await ek(sp);
        var cariler = sp.GetRequiredService<CustomerService>();
        var musteri = await cariler.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Fin", Soyad = "Musteri" });
        var tedarikci = await cariler.CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Fin", Soyad = "Tedarikci" });
        var (kira, arac) = await KiraAsync(sp, musteri, KiraBas, gun: 3);
        return new Ortam
        {
            TenantId = tenantId, Kod = kod, Sifre = sifre, Kullanicilar = kullanicilar,
            Musteri = musteri, Tedarikci = tedarikci, Kira = kira, KiraArac = arac,
        };
    }

    /// <summary>Yeni araçla kira (günlük 100 TRY, çıkış ofisi SubeA).</summary>
    private static async Task<(Guid Kira, Guid Arac)> KiraAsync(IServiceProvider sp, Guid musteri, DateTimeOffset bas, int gun)
    {
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 FA " + Random.Shared.Next(1000, 9999) });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = musteri, VehicleId = arac, BasTar = bas, BitTar = bas.AddDays(gun),
            GunlukUcret = 100m, CikisOfisi = "SubeA",
        });
        return (kira, arac);
    }

    private async Task<T> OkuAsync<T>(Ortam o, Func<IServiceProvider, Task<T>> oku)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(o.TenantId, role: UserRole.Admin);
        return await oku(s.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Ortam o, Func<AppDbContext, Task<T>> oku)
        => OkuAsync(o, async sp =>
        {
            await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await oku(db);
        });

    private Task<List<AccountLedgerEntry>> DefterAsync(Ortam o, Guid sourceId)
        => DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == sourceId).ToListAsync());

    private Task<RentalContract> KiraOkuAsync(Ortam o, Guid kira)
        => OkuAsync(o, async sp => (await sp.GetRequiredService<RentalService>().GetAsync(kira))!);

    private Task<decimal> CariBakiyeAsync(Ortam o, Guid cari)
        => OkuAsync(o, sp => sp.GetRequiredService<CashService>().GetCariBalanceAsync(cari));

    /// <summary>Kiracının TÜM defter kümeleri dengeli: her (SourceType, SourceId) için Σ borç(baz) == Σ alacak(baz).</summary>
    private async Task TumDefterDengeliAsync(Ortam o)
    {
        var satirlar = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().ToListAsync());
        Assert.NotEmpty(satirlar);
        foreach (var kume in satirlar.GroupBy(e => (e.SourceType, e.SourceId)))
        {
            var borc = kume.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            var alacak = kume.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            Assert.True(borc == alacak, $"Dengesiz küme {kume.Key}: borç {borc} ≠ alacak {alacak}");
        }
    }

    private static void Satir(List<AccountLedgerEntry> kume, LedgerAccountType tur, Guid? referans, LedgerDirection yon,
        decimal tutar, string doviz = "TRY", decimal kur = 1m)
        => Assert.Single(kume, e => e.AccountType == tur && e.AccountRef == referans && e.Direction == yon
                                    && e.Amount.Amount == tutar && e.Amount.Currency == doviz && e.Amount.Rate == kur);

    // ------------------------------------------------------------ HTTP

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
        var once = CerezDegeri(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({kim}): {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CerezDegeri(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> PostAsync(Oturum s, string yol, object? govde, string? anahtar)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + yol);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (anahtar is not null) req.Headers.Add("Idempotency-Key", anahtar);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Tamam(HttpResponseMessage r)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {metin}");
        return JsonDocument.Parse(metin).RootElement.Clone();
    }

    private static async Task<Guid> Id(HttpResponseMessage r) => (await Tamam(r)).GetProperty("id").GetGuid();

    /// <summary>ProblemDetails + kod; <paramref name="alan"/> verilirse <c>errors[alan]</c> dolu olmalı.</summary>
    private static async Task<JsonElement> Problem(HttpResponseMessage r, HttpStatusCode durum, string kod, string? alan = null)
    {
        var metin = await r.Content.ReadAsStringAsync();
        Assert.True(durum == r.StatusCode, $"Beklenen {(int)durum}, gelen {(int)r.StatusCode}: {metin}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(metin).RootElement.Clone();
        Assert.Equal(kod, j.GetProperty("kod").GetString());
        if (alan is not null)
            Assert.True(j.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out var m) && m.GetArrayLength() > 0,
                $"errors[{alan}] yok: {metin}");
        return j;
    }

    private static object Tahsilat(Ortam o, decimal tutar, string hesap = "Kasa", Guid? kira = null, bool kirasiz = false,
        string? doviz = null, decimal? kur = null, Guid? tahsilatAnahtar = null, Guid? cari = null, Guid? hesapId = null)
        => new
        {
            cariId = cari ?? o.Musteri, kiraId = kirasiz ? (Guid?)null : kira ?? o.Kira, tutar, hesap,
            doviz, kur, tahsilatAnahtar, hesapId, kanal = "Masaüstü", aciklama = "F4.4 test",
        };

    private Task<int> TahsilatSayisiAsync(Ortam o, Guid kira)
        => DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == kira));

    // ============================================================ tahsilat (E01)

    [Fact]
    public async Task Tahsilat_defter_dengeli_kira_tahsilat_ve_bakiye_islenir()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), YeniAnahtar()));

        // Borç Kasa 300 / Alacak Cari(müşteri) 300 — elle oracle.
        var kume = await DefterAsync(o, id);
        Assert.Equal(2, kume.Count);
        Satir(kume, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 300m);
        Satir(kume, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Credit, 300m);
        Assert.All(kume, e => Assert.Equal("Tahsilat", e.SourceType));

        var kira = await KiraOkuAsync(o, o.Kira);
        Assert.Equal(300m, kira.GenelToplam);   // 3 gün × 100
        Assert.Equal(300m, kira.Tahsilat);
        Assert.Equal(0m, kira.Bakiye);
        Assert.Equal(-300m, await CariBakiyeAsync(o, o.Musteri)); // faturasız: cari alacaklı
        await TumDefterDengeliAsync(o);
    }

    [Fact]
    public async Task Tahsilat_ayni_anahtar_ayni_icerik_409_mukerrer_tek_kayit()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();

        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), anahtar));
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), anahtar), HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(300m, (await KiraOkuAsync(o, o.Kira)).Tahsilat); // 600 DEĞİL
        Assert.Equal(-300m, await CariBakiyeAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Tahsilat_ayni_anahtar_farkli_tutar_409_ilk_kayit_kalir()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();

        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m), anahtar));
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 250m), anahtar), HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
        var kira = await KiraOkuAsync(o, o.Kira);
        Assert.Equal(100m, kira.Tahsilat);
        Assert.Equal(200m, kira.Bakiye);
    }

    [Fact]
    public async Task Tahsilat_eszamanli_ayni_anahtar_tek_yazar()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();

        var yanitlar = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m), anahtar)));

        Assert.Equal(1, yanitlar.Count(r => r.StatusCode == HttpStatusCode.OK));
        foreach (var r in yanitlar.Where(r => r.StatusCode != HttpStatusCode.OK))
            await Problem(r, HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(100m, (await KiraOkuAsync(o, o.Kira)).Tahsilat); // 500 DEĞİL
    }

    [Fact]
    public async Task Tahsilat_yenilenen_anahtarla_iki_mesru_tahsilat_iki_kayit()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);

        var id1 = await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m), YeniAnahtar()));
        var id2 = await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 200m), YeniAnahtar())); // 2xx sonrası yeni anahtar

        Assert.NotEqual(id1, id2);
        Assert.Equal(2, await TahsilatSayisiAsync(o, o.Kira));
        var kira = await KiraOkuAsync(o, o.Kira);
        Assert.Equal(300m, kira.Tahsilat);
        Assert.Equal(0m, kira.Bakiye);
        await TumDefterDengeliAsync(o);
    }

    [Fact]
    public async Task Tahsilat_basliksiz_ve_deterministik_anahtarsiz_400_hicbir_sey_yazilmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);

        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), anahtar: null),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), anahtar: "kisa"),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(0, await TahsilatSayisiAsync(o, o.Kira));
    }

    // ------------------------------------------------------------ deterministik TahsilatAnahtar

    [Fact]
    public async Task TahsilatAnahtar_ikinci_sekme_ve_ikinci_kullanici_cift_tahsil_etmez()
    {
        var o = await OrtamKurAsync();
        var sekme1 = await GirisAsync(o, Kim.Muhasebe);
        var sekme2 = await GirisAsync(o, Kim.Muhasebe);
        var baskaKullanici = await GirisAsync(o, Kim.Admin);
        // Panel DTO'sunun taşıdığı anahtar (ekran yüklenirken: kira + bakiye 300 + işlem sayısı 0).
        var k = RentACar.Web.Finance.TahsilatAnahtar.Uret(o.Kira, 300m, 0);

        // Her sekme KENDİ Idempotency-Key başlığını üretir — deterministik anahtar başlıktan önceliklidir.
        await Id(await PostAsync(sekme1, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), YeniAnahtar()));
        await Problem(await PostAsync(sekme2, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), YeniAnahtar()),
            HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(baskaKullanici, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), YeniAnahtar()),
            HttpStatusCode.Conflict, "mukerrer");
        // Başlıksız da olsa aynı deterministik anahtar → 409 (başlık yalnız deterministik anahtar yoksa zorunlu).
        await Problem(await PostAsync(sekme2, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(300m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(-300m, await CariBakiyeAsync(o, o.Musteri)); // −600/−900 DEĞİL
    }

    [Fact]
    public async Task TahsilatAnahtar_basliksiz_kabul_kirasiz_ve_bozuk_baslikla_400()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var k = RentACar.Web.Finance.TahsilatAnahtar.Uret(o.Kira, 300m, 0);

        // Kirasız deterministik anahtar anlamsız → alan hatası.
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m, kirasiz: true, tahsilatAnahtar: k), YeniAnahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "tahsilatAnahtar");
        // Bozuk başlık deterministik anahtar olsa bile reddedilir.
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), anahtar: "ç-bozuk-başlık-değeri"),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(0, await TahsilatSayisiAsync(o, o.Kira));

        // Başlıksız + deterministik anahtar → yazılır.
        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), anahtar: null));
        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
    }

    // ------------------------------------------------------------ F4.4 SPA: kira detayındaki tahsilat anahtarı

    private static async Task<JsonElement> DetayAsync(Oturum s, Guid kira)
        => await Tamam(await s.C.GetAsync($"{V1}/kiralar/{kira}"));

    /// <summary>
    /// Sabit panel akışı (SPA): anahtar DETAYDAN okunur, başlıksız gönderilir. Aynı anahtarla ikinci gönderim
    /// (çift tık / bayat ekran) 409; detay tazelenince YENİ anahtar gelir ve ikinci MEŞRU tahsilat yazılır.
    /// Beklenenler elle: 300 − 100 = 200 kalan, sonra 200 tahsil → 0; iki kayıt.
    /// </summary>
    [Fact]
    public async Task Detay_tahsilat_anahtari_bayatlar_tazelenince_ikinci_mesru_tahsilat_yazilir()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);

        var t1 = (await DetayAsync(s, o.Kira)).GetProperty("tahsilat");
        Assert.Equal(o.Musteri, t1.GetProperty("cariId").GetGuid());
        Assert.Equal(o.Kira, t1.GetProperty("rentalId").GetGuid());
        Assert.Equal("TRY", t1.GetProperty("doviz").GetString());
        Assert.Equal(300m, t1.GetProperty("varsayilanTutar").GetDecimal());
        var k1 = t1.GetProperty("anahtar").GetGuid();

        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m, tahsilatAnahtar: k1), anahtar: null));
        // Aynı (artık bayat) anahtar: başlık yeni olsa da 409 — ikinci kayıt yok.
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m, tahsilatAnahtar: k1), YeniAnahtar()),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));

        var t2 = (await DetayAsync(s, o.Kira)).GetProperty("tahsilat");
        var k2 = t2.GetProperty("anahtar").GetGuid();
        Assert.NotEqual(k1, k2);
        Assert.Equal(200m, t2.GetProperty("varsayilanTutar").GetDecimal());
        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 200m, tahsilatAnahtar: k2), anahtar: null));

        Assert.Equal(2, await TahsilatSayisiAsync(o, o.Kira));
        var kira = await KiraOkuAsync(o, o.Kira);
        Assert.Equal(300m, kira.Tahsilat);
        Assert.Equal(0m, kira.Bakiye);
        // Bakiye 0'da da anahtar dolar (Blazor sabit paneli ön/fazla tahsilata açık); varsayılan tutar 0.
        var t3 = (await DetayAsync(s, o.Kira)).GetProperty("tahsilat");
        Assert.Equal(0m, t3.GetProperty("varsayilanTutar").GetDecimal());
        Assert.NotEqual(k2, t3.GetProperty("anahtar").GetGuid());
        await TumDefterDengeliAsync(o);
    }

    /// <summary>
    /// F4.4 adversarial HIGH-1: ilk istek YAZILDI ama yanıt kayboldu → istemci AYNI anahtar + AYNI gövdeyle tekrarlar.
    /// Önce: sunucu anahtarı güncel bakiye/işlem sayısıyla yeniden hesaplayıp "kayıt değişti … tekrar deneyin" 409'u
    /// veriyordu; kullanıcı yeni anahtarla İKİNCİ tahsilatı yazıyordu (gerçek DB'de iki kez 500). Artık 409
    /// "zaten kaydedildi" + <c>mevcut</c> (id, belge no, tutar, döviz); bayat anahtarın 409'unda <c>mevcut</c> YOK.
    /// Beklenenler elle: 500 tahsil, tek kayıt, kalan 300 − 500 = −200.
    /// </summary>
    [Fact]
    public async Task Tahsilat_kaybolan_yanit_sonrasi_ayni_anahtar_409_mevcut_dolu_bayat_anahtar_mevcut_yok()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var k1 = (await DetayAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        var govde = Tahsilat(o, 500m, tahsilatAnahtar: k1);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", govde, anahtar: null)); // yanıt "kayboldu"
        var tekrar = await Problem(await PostAsync(s, "/finans/tahsilat", govde, anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Contains("zaten kaydedildi", tekrar.GetProperty("detail").GetString());
        Assert.DoesNotContain("tekrar deneyin", tekrar.GetProperty("detail").GetString());
        var mevcut = tekrar.GetProperty("mevcut");
        Assert.True(mevcut.GetProperty("ayniIcerik").GetBoolean()); // birebir aynı gövde: kendi tekrarı
        Assert.Equal(id, mevcut.GetProperty("id").GetGuid());
        Assert.Equal(500m, mevcut.GetProperty("tutar").GetDecimal());
        Assert.Equal("TRY", mevcut.GetProperty("doviz").GetString());
        var no = await DbAsync(o, db => db.CashTransactions.AsNoTracking().Where(t => t.Id == id).Select(t => t.No).SingleAsync());
        Assert.Equal(no, mevcut.GetProperty("belgeNo").GetString());
        Assert.Contains(no, tekrar.GetProperty("detail").GetString());
        // 3. tur M-A / G1b: gövde FARKLI (kullanıcı tutarı değiştirdi ya da başka sekme) → kayıt yine tek, ama
        // "zaten kaydedildi" DEĞİL: "başka bir tahsilat yazıldı … girdiğiniz 100,00 TRY YAZILMADI", ayniIcerik=false.
        var farkli = await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m, tahsilatAnahtar: k1), YeniAnahtar()),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(id, farkli.GetProperty("mevcut").GetProperty("id").GetGuid());
        Assert.False(farkli.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Contains("girdiğiniz 100,00 TRY YAZILMADI", farkli.GetProperty("detail").GetString());
        Assert.Contains("500,00 TRY", farkli.GetProperty("detail").GetString());
        Assert.DoesNotContain("zaten kaydedildi", farkli.GetProperty("detail").GetString());
        // Aynı tutar ama başka hesap türü (Banka) de "aynı içerik" değildir.
        var bankadan = await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 500m, hesap: "Banka", tahsilatAnahtar: k1), anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(bankadan.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        // Ölçek farkı (500 vs "500.00") aynı içeriktir (decimal eşitliği).
        var olcek = await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 500.00m, tahsilatAnahtar: k1), anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(olcek.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(-200m, (await KiraOkuAsync(o, o.Kira)).Bakiye);

        // Bayat anahtar: kirada BAŞKA bir işlem oldu (bu anahtarla yazılmış kayıt YOK) → 409, mevcut yok.
        var k2 = (await DetayAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 50m, hesap = "Kasa" }, YeniAnahtar()));
        var bayat = await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 50m, tahsilatAnahtar: k2), anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(bayat.TryGetProperty("mevcut", out _));
        Assert.Contains("tutarı yeniden girin", bayat.GetProperty("detail").GetString());
        Assert.Equal(1, await DbAsync(o, db => db.CashTransactions.AsNoTracking()
            .CountAsync(t => t.RentalId == o.Kira && t.Tip == CashTransactionType.Tahsilat))); // + 1 ödeme, tahsilat hâlâ tek
        await TumDefterDengeliAsync(o);
    }

    /// <summary>
    /// 3. tur M-A (G2): iki kullanıcı AYNI detayla açık (aynı anahtar K). A 100 tahsil eder; B ön-dolu 300'ü gönderir
    /// → 409 mevcut, ayniIcerik=false, "girdiğiniz 300,00 TRY YAZILMADI" (B'nin parası kaydedilmiş sanılmasın).
    /// Yalnız A'nın 100'ü yazılı; B detayı tazeleyip yeni anahtarla bilinçli gönderince ikinci kayıt yazılır.
    /// </summary>
    [Fact]
    public async Task Tahsilat_iki_kullanici_ayni_anahtar_farkli_tutar_yazilmadi_der()
    {
        var o = await OrtamKurAsync();
        var a = await GirisAsync(o, Kim.Muhasebe);
        var b = await GirisAsync(o, Kim.Admin);
        var k = (await DetayAsync(a, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        Assert.Equal(k, (await DetayAsync(b, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid());

        var idA = await Id(await PostAsync(a, "/finans/tahsilat", Tahsilat(o, 100m, tahsilatAnahtar: k), anahtar: null));
        var r = await Problem(await PostAsync(b, "/finans/tahsilat", Tahsilat(o, 300m, tahsilatAnahtar: k), anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");
        var m = r.GetProperty("mevcut");
        Assert.Equal(idA, m.GetProperty("id").GetGuid());
        Assert.Equal(100m, m.GetProperty("tutar").GetDecimal());
        Assert.False(m.GetProperty("ayniIcerik").GetBoolean());
        Assert.Contains("girdiğiniz 300,00 TRY YAZILMADI", r.GetProperty("detail").GetString());
        Assert.Equal(1, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(100m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);

        var k2 = (await DetayAsync(b, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(b, "/finans/tahsilat", Tahsilat(o, 200m, tahsilatAnahtar: k2), anahtar: null));
        Assert.Equal(2, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(300m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
        await TumDefterDengeliAsync(o);
    }

    /// <summary>HIGH-1 kapsam: başka kiranın (aynı cari) tahsilatına ait anahtar bu kirayla gönderilirse o kaydın
    /// no/tutarı SIZMAZ — "zaten kaydedildi" yalnız aynı kiranın tahsilatı için; diğeri "ait değil" 409'u.</summary>
    [Fact]
    public async Task Tahsilat_baska_kiranin_anahtari_mevcut_sizdirmaz()
    {
        var o = await OrtamKurAsync();
        var (kiraB, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, KiraBas.AddDays(10), gun: 2));
        var s = await GirisAsync(o, Kim.Muhasebe);
        var kA = (await DetayAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m, tahsilatAnahtar: kA), anahtar: null));

        var r = await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m, kira: kiraB, tahsilatAnahtar: kA), anahtar: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(r.TryGetProperty("mevcut", out _));
        Assert.Equal(0, await TahsilatSayisiAsync(o, kiraB));
    }

    [Fact]
    public async Task Detay_tahsilat_finans_izni_yoksa_ve_iptal_kirada_null()
    {
        var o = await OrtamKurAsync();
        // Operatör (FinanceWrite yok): kira okunur, tahsilat verisi YOK.
        var op = await GirisAsync(o, Kim.OperatorDuz);
        var d = await DetayAsync(op, o.Kira);
        Assert.False(d.GetProperty("yetkiler").GetProperty("finans").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("tahsilat").ValueKind);

        // İptal kira (tahsilatsız → iptal edilebilir): finans iznine rağmen tahsilat verisi yok (uç da reddeder).
        Assert.True(await OkuAsync(o, sp => sp.GetRequiredService<RentalService>().CancelAsync(o.Kira)));
        var s = await GirisAsync(o, Kim.Muhasebe);
        Assert.Equal(JsonValueKind.Null, (await DetayAsync(s, o.Kira)).GetProperty("tahsilat").ValueKind);
    }

    // ------------------------------------------------------------ çok döviz

    [Fact]
    public async Task Dovizli_tahsilat_acik_kur_aynen_kullanilir()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, doviz: "USD", kur: 32m), YeniAnahtar()));

        var kume = await DefterAsync(o, id);
        Satir(kume, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 10m, "USD", 32m);
        Satir(kume, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Credit, 10m, "USD", 32m);
        var kira = await KiraOkuAsync(o, o.Kira); // TRY kira: delta = 10 × 32 = 320 baz
        Assert.Equal(320m, kira.Tahsilat);
        Assert.Equal(-20m, kira.Bakiye);
        Assert.Equal(-320m, await CariBakiyeAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Dovizli_tahsilat_bos_kur_firma_sabit_kurundan_cozulur()
    {
        var o = await OrtamKurAsync(sp => sp.GetRequiredService<SabitKurService>()
            .UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 30m, Aktif = true }));
        var s = await GirisAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, doviz: "USD"), YeniAnahtar()));

        var kume = await DefterAsync(o, id);
        Satir(kume, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 10m, "USD", 30m);
        Satir(kume, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Credit, 10m, "USD", 30m);
        var kira = await KiraOkuAsync(o, o.Kira); // 10 × 30 = 300 → bakiye kapanır
        Assert.Equal(300m, kira.Tahsilat);
        Assert.Equal(0m, kira.Bakiye);
    }

    [Fact]
    public async Task Dovizli_tahsilat_kur_bulunamazsa_red_sessiz_1_yok()
    {
        var o = await OrtamKurAsync(); // GBP için sabit kur/TCMB kaydı yok
        var s = await GirisAsync(o, Kim.Muhasebe);

        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, doviz: "GBP", kirasiz: true), YeniAnahtar()),
            HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));
    }

    // ------------------------------------------------------------ alan hataları + tutarlılık

    [Fact]
    public async Task Tahsilat_alan_hatalari_errors_alan_tasir_hicbir_sey_yazilmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var digerCari = await OkuAsync(o, sp => sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Baska", Soyad = "Cari" }));

        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 0m), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, hesap: "Pos"), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, hesap: ""), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, doviz: "XX"), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "doviz");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, kur: -1m), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "kur");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, cari: Guid.Empty), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, kira: Guid.NewGuid()), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        // Kira tahsilatı başka cari adına yazılamaz (kira faturası kiranın müşterisini borçlandırır).
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, cari: digerCari), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        var kanal = new { cariId = o.Musteri, kiraId = o.Kira, tutar = 10m, hesap = "Kasa", kanal = "Faks" };
        await Problem(await PostAsync(s, "/finans/tahsilat", kanal, YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "kanal");
        var gelecek = new { cariId = o.Musteri, kiraId = o.Kira, tutar = 10m, hesap = "Kasa", tarih = DateTimeOffset.UtcNow.AddDays(10) };
        await Problem(await PostAsync(s, "/finans/tahsilat", gelecek, YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "tarih");

        Assert.Equal(0, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(0m, await CariBakiyeAsync(o, digerCari));
    }

    [Fact]
    public async Task Iptal_kiraya_tahsilat_400_iade_odemesi_baglanabilir()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 100m), YeniAnahtar()));
        // F4.1 adversarial M3: tahsilatlı kira artık servisten İPTAL EDİLEMEZ (önce iade). "İptal + tahsilat"
        // yalnız ESKİ veride vardır → reddi doğrula, durumu eski kayıt benzetimi olarak doğrudan yaz.
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => OkuAsync(o, sp => sp.GetRequiredService<RentalService>().CancelAsync(o.Kira)));
        await DbAsync(o, async db =>
        {
            var kira = await db.Rentals.SingleAsync(x => x.Id == o.Kira);
            kira.Durum = RentalStatus.Iptal;
            return await db.SaveChangesAsync();
        });

        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 50m), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        Assert.Equal(100m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);

        // Alınan 100'ün iadesi kiraya bağlanır: kira Tahsilat 0'a döner, cari sıfırlanır.
        await Id(await PostAsync(s, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 100m, hesap = "Kasa" }, YeniAnahtar()));
        Assert.Equal(0m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Tahsilat_spesifik_kasa_hesabina_yazilir()
    {
        Guid kasa = Guid.Empty;
        var o = await OrtamKurAsync(async sp => kasa = await sp.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = true }));
        var s = await GirisAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m, hesapId: kasa), YeniAnahtar()));
        Satir(await DefterAsync(o, id), LedgerAccountType.Kasa, kasa, LedgerDirection.Debit, 300m);
        // Tür çelişkisi (Kasa hesabı, Banka işlemi) servisten reddedilir.
        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 10m, hesap: "Banka", hesapId: kasa), YeniAnahtar()),
            HttpStatusCode.BadRequest, "dogrulama");
    }

    // ============================================================ ödeme (E02)

    [Fact]
    public async Task Odeme_defter_dengeli_ayni_anahtar_409()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();
        var govde = new { cariId = o.Musteri, tutar = 50m, hesap = "Banka", aciklama = "Giden havale" };

        var id = await Id(await PostAsync(s, "/finans/odeme", govde, anahtar));
        var kume = await DefterAsync(o, id);
        Assert.Equal(2, kume.Count);
        Satir(kume, LedgerAccountType.Banka, null, LedgerDirection.Credit, 50m);
        Satir(kume, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Debit, 50m);
        Assert.Equal(50m, await CariBakiyeAsync(o, o.Musteri));

        await Problem(await PostAsync(s, "/finans/odeme", govde, anahtar), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/finans/odeme", govde, anahtar: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(50m, await CariBakiyeAsync(o, o.Musteri)); // 100 DEĞİL
        await TumDefterDengeliAsync(o);
    }

    // ============================================================ fatura (E15)

    [Fact]
    public async Task Fatura_kiradan_kesilir_ikinci_kesim_400_tek_fatura()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, anahtar: null));

        var fatura = await DbAsync(o, db => db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id));
        Assert.Equal(300m, fatura.GenelToplam);  // 3 × 100 brüt
        Assert.Equal(250m, fatura.NetTutar);     // 300 / 1,20
        Assert.Equal(50m, fatura.KdvTutar);
        var kume = await DefterAsync(o, id);
        Assert.Equal(3, kume.Count);
        Satir(kume, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Debit, 300m);
        Satir(kume, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 250m);
        Satir(kume, LedgerAccountType.Kdv, null, LedgerDirection.Credit, 50m);
        Assert.Equal(300m, await CariBakiyeAsync(o, o.Musteri));

        var ikinci = await Problem(await PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, YeniAnahtar()),
            HttpStatusCode.BadRequest, "dogrulama");
        Assert.Contains("zaten tam faturalanmış", ikinci.GetProperty("detail").GetString());
        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(300m, await CariBakiyeAsync(o, o.Musteri));

        // Tahsilat faturalı cariyi sıfırlar.
        await Id(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), YeniAnahtar()));
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));
        await TumDefterDengeliAsync(o);
    }

    [Fact]
    public async Task Fatura_kira_yok_ya_da_bos_400_kiraId()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        await Problem(await PostAsync(s, "/finans/fatura", new { kiraId = Guid.NewGuid() }, null), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        await Problem(await PostAsync(s, "/finans/fatura", new { kiraId = Guid.Empty }, null), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
    }

    // ============================================================ dönem faturası (E18/E19)

    [Fact]
    public async Task Donem_faturasi_kes_ve_tahsil_cift_gonderim_sessiz_tek_fatura_tek_tahsilat()
    {
        var o = await OrtamKurAsync();
        // 15 Oca 2027 + 90 gün × 100 = 9000; dönem 1 = 15 Oca–15 Şub = 31 gün → 31/90 × 9000 = 3100.
        var (kira, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), gun: 90));
        var s = await GirisAsync(o, Kim.Muhasebe);
        var govde = new { kiraId = kira, donemSira = 1, tahsilat = true, hesap = "Kasa" };

        var ilk = await Tamam(await PostAsync(s, "/finans/donem-fatura", govde, null));
        var faturaId = ilk.GetProperty("faturaId").GetGuid();
        Assert.True(ilk.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(JsonValueKind.Null, ilk.GetProperty("bilgi").ValueKind);

        Assert.Equal(3100m, await DbAsync(o, db => db.Invoices.AsNoTracking().Where(i => i.Id == faturaId).Select(i => i.GenelToplam).SingleAsync()));
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));      // fatura borcu 3100 == tahsilat 3100
        Assert.Equal(3100m, (await KiraOkuAsync(o, kira)).Tahsilat);

        // Çift gönderim (başka başlıkla da): sessiz, aynı fatura, tahsilat YAZILMAZ ve bu gizlenmez.
        var ikinci = await Tamam(await PostAsync(s, "/finans/donem-fatura", govde, YeniAnahtar()));
        Assert.Equal(faturaId, ikinci.GetProperty("faturaId").GetGuid());
        Assert.False(ikinci.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal("Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.", ikinci.GetProperty("bilgi").GetString());

        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync(i => i.KaynakKiraId == kira || i.RentalId == kira)));
        Assert.Equal(1, await TahsilatSayisiAsync(o, kira));
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));      // −3100 DEĞİL
        Assert.Equal(3100m, (await KiraOkuAsync(o, kira)).Tahsilat);
        await TumDefterDengeliAsync(o);
    }

    [Fact]
    public async Task Donem_faturasi_tahsilatsiz_yalniz_fatura_tahsilatta_hesap_zorunlu()
    {
        var o = await OrtamKurAsync();
        var (kira, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), gun: 90));
        var s = await GirisAsync(o, Kim.Muhasebe);

        await Problem(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kira, donemSira = 1, tahsilat = true }, null),
            HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kira, donemSira = 0 }, null),
            HttpStatusCode.BadRequest, "dogrulama", "donemSira");

        var y = await Tamam(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kira, donemSira = 1 }, null));
        Assert.False(y.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(JsonValueKind.Null, y.GetProperty("bilgi").ValueKind); // tahsilat istenmedi → bilgi yok
        Assert.Equal(3100m, await CariBakiyeAsync(o, o.Musteri));
        Assert.Equal(0, await TahsilatSayisiAsync(o, kira));
    }

    // ============================================================ dış hizmet (E33) + iptal (E34)

    [Fact]
    public async Task Dis_hizmet_defterli_ayni_anahtar_409_iptal_FinanceReverse_ister()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();
        var govde = new
        {
            kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Çekici", hizmetBedeli = 1000m, komisyonOran = 10m,
            hizmetAlinanFirma = "Yol Yardım", komisyonFaturaNo = "KF-1",
        };

        var id = await Id(await PostAsync(s, "/finans/dis-hizmet", govde, anahtar));
        var kume = await DefterAsync(o, id);
        Assert.Equal(4, kume.Count);
        Satir(kume, LedgerAccountType.Gider, o.KiraArac, LedgerDirection.Debit, 1000m);  // gider ARAÇTA
        Satir(kume, LedgerAccountType.Cari, o.Tedarikci, LedgerDirection.Credit, 1000m);
        Satir(kume, LedgerAccountType.Cari, o.Tedarikci, LedgerDirection.Debit, 100m);   // 1000 × %10
        Satir(kume, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 100m);
        Assert.Equal(-900m, await CariBakiyeAsync(o, o.Tedarikci));

        await Problem(await PostAsync(s, "/finans/dis-hizmet", govde, anahtar), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", govde, anahtar: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(1, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));

        // FinanceWrite VAR, FinanceReverse YASAK → 403; kayıt iptal edilmez.
        var yasakli = await GirisAsync(o, Kim.MuhasebeTersYasak);
        await Problem(await PostAsync(yasakli, $"/finans/dis-hizmet/{id}/iptal", null, null), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(DisHizmetDurum.Kayitli, (await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().SingleAsync(d => d.Id == id))).Durum);

        var iptal = await PostAsync(s, $"/finans/dis-hizmet/{id}/iptal", null, null);
        Assert.True(iptal.StatusCode == HttpStatusCode.NoContent, await iptal.Content.ReadAsStringAsync());
        Assert.Equal(DisHizmetDurum.Iptal, (await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().SingleAsync(d => d.Id == id))).Durum);
        Assert.Equal(8, (await DefterAsync(o, id)).Count);             // ters kayıt: silme yok
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Tedarikci));        // net sıfır

        var ikinci = await Problem(await PostAsync(s, $"/finans/dis-hizmet/{id}/iptal", null, null), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal("Kayıt zaten iptal edilmiş.", ikinci.GetProperty("detail").GetString());
        Assert.Equal(8, (await DefterAsync(o, id)).Count);
        await TumDefterDengeliAsync(o);
    }

    [Fact]
    public async Task Dis_hizmet_alan_hatalari()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        object G(decimal bedel = 100m, decimal? oran = 5m, string? hizmet = "Vale", Guid? cari = null, Guid? kira = null)
            => new { kiraId = kira ?? o.Kira, cariId = cari ?? o.Tedarikci, alinanHizmet = hizmet, hizmetBedeli = bedel, komisyonOran = oran };

        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(bedel: 0m), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "hizmetBedeli");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(oran: 150m), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "komisyonOran");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(hizmet: " "), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "alinanHizmet");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(cari: Guid.Empty), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(kira: Guid.NewGuid()), YeniAnahtar()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        Assert.Equal(0, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));
    }

    // ============================================================ depozito al (E09) / irat (E12)

    [Fact]
    public async Task Depozito_al_ayni_icerik_sessiz_ayni_id_farkli_icerik_409()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();
        var govde = new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" };

        var id = await Id(await PostAsync(s, "/finans/depozito/al", govde, anahtar));
        var kume = await DefterAsync(o, id);
        Assert.Equal(2, kume.Count);
        Satir(kume, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 500m);
        Satir(kume, LedgerAccountType.Depozito, o.Musteri, LedgerDirection.Credit, 500m);
        Assert.Equal(500m, await OkuAsync(o, sp => sp.GetRequiredService<DepozitoService>().GetBakiyeAsync(o.Musteri)));

        // Birebir aynı tekrar: sessiz başarı, AYNI id, ikinci yazım yok.
        Assert.Equal(id, await Id(await PostAsync(s, "/finans/depozito/al", govde, anahtar)));
        // Aynı anahtar, farklı tutar: 409 farklı içerik, hiçbir şey yazılmaz.
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 600m, hesap = "Kasa" }, anahtar),
            HttpStatusCode.Conflict, "mukerrer");
        // Aynı anahtar, farklı hesap: 409.
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 500m, hesap = "Banka" }, anahtar),
            HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(2, (await DefterAsync(o, id)).Count);
        Assert.Equal(500m, await OkuAsync(o, sp => sp.GetRequiredService<DepozitoService>().GetBakiyeAsync(o.Musteri)));
        await Problem(await PostAsync(s, "/finans/depozito/al", govde, anahtar: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 5m, hesap = "Pos" }, YeniAnahtar()),
            HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await TumDefterDengeliAsync(o);
    }

    [Fact]
    public async Task Depozito_irat_gelir_yazar_kiraya_atfedilir_tekrar_sessiz_farkli_409()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" }, YeniAnahtar()));
        var anahtar = YeniAnahtar();
        var govde = new { cariId = o.Musteri, tutar = 200m, kiraId = o.Kira, aciklama = "Hasar kesintisi" };

        var id = await Id(await PostAsync(s, "/finans/depozito/irat", govde, anahtar));
        var kume = await DefterAsync(o, id);
        Assert.Equal(2, kume.Count);
        Satir(kume, LedgerAccountType.Depozito, o.Musteri, LedgerDirection.Debit, 200m);
        Satir(kume, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 200m);
        Assert.Equal(o.Kira, await DbAsync(o, db => db.DepozitoIratlar.AsNoTracking().Where(d => d.Id == id).Select(d => d.RentalId).SingleAsync()));
        Assert.Equal(300m, await OkuAsync(o, sp => sp.GetRequiredService<DepozitoService>().GetBakiyeAsync(o.Musteri)));

        Assert.Equal(id, await Id(await PostAsync(s, "/finans/depozito/irat", govde, anahtar)));
        await Problem(await PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 150m, kiraId = o.Kira }, anahtar),
            HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 200m }, anahtar), // kira atfı farklı
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(300m, await OkuAsync(o, sp => sp.GetRequiredService<DepozitoService>().GetBakiyeAsync(o.Musteri)));

        // Tutulanı aşan irat reddedilir (iş kuralı servis/repo'da).
        await Problem(await PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 301m }, YeniAnahtar()),
            HttpStatusCode.BadRequest, "dogrulama");
        await TumDefterDengeliAsync(o);
    }

    // ============================================================ izin + şube kapsamı

    [Fact]
    public async Task FinanceWrite_olmayan_operator_403_hicbir_sey_yazilmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.OperatorDuz);

        await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), YeniAnahtar()), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 5m, hesap = "Kasa" }, YeniAnahtar()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await s.C.GetAsync(V1 + "/finans/hesaplar"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, await TahsilatSayisiAsync(o, o.Kira));
    }

    [Fact]
    public async Task Baska_subenin_operatoru_403_kendi_subesindeki_yazar()
    {
        var o = await OrtamKurAsync();
        var b = await GirisAsync(o, Kim.OperatorB); // SubeB + kullanıcı-bazlı FinanceWrite; kira SubeA'da

        await Problem(await PostAsync(b, "/finans/tahsilat", Tahsilat(o, 300m), YeniAnahtar()), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 5m, hesap = "Kasa" }, YeniAnahtar()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/fatura", new { kiraId = o.Kira }, null), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/donem-fatura", new { kiraId = o.Kira, donemSira = 1 }, null), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/dis-hizmet",
                new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m }, YeniAnahtar()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 1m, kiraId = o.Kira }, YeniAnahtar()),
            HttpStatusCode.Forbidden, "yetki_yok");

        Assert.Equal(0, await TahsilatSayisiAsync(o, o.Kira));
        Assert.Equal(0, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().CountAsync()));

        var a = await GirisAsync(o, Kim.OperatorA); // SubeA — kapsamda
        await Id(await PostAsync(a, "/finans/tahsilat", Tahsilat(o, 300m), YeniAnahtar()));
        Assert.Equal(300m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
    }

    [Fact]
    public async Task Pilot_olmayan_firmada_finans_uclari_403_pilot_degil()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        await fx.PilotYapAsync(o.TenantId, false);
        try
        {
            await Problem(await PostAsync(s, "/finans/tahsilat", Tahsilat(o, 300m), YeniAnahtar()), HttpStatusCode.Forbidden, "pilot_degil");
            Assert.Equal(0, await TahsilatSayisiAsync(o, o.Kira));
        }
        finally { await fx.PilotYapAsync(o.TenantId, true); }
    }

    // ============================================================ hesap seçimi

    [Fact]
    public async Task Hesaplar_yalniz_aktif_ture_gore_suzulur_iban_donmez()
    {
        var o = await OrtamKurAsync(async sp =>
        {
            var h = sp.GetRequiredService<FinancialAccountService>();
            await h.CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = true });
            await h.CreateAsync(new FinancialAccountInput { Kod = "BNK", Ad = "Ana Banka", Tur = "Banka", Doviz = "USD", Iban = "TR000000000000000000000001", Aktif = true });
            await h.CreateAsync(new FinancialAccountInput { Kod = "ESK", Ad = "Eski Kasa", Tur = "Kasa", Aktif = false });
        });
        var s = await GirisAsync(o, Kim.Muhasebe);

        var hepsi = await Tamam(await s.C.GetAsync(V1 + "/finans/hesaplar"));
        Assert.Equal(new[] { "Ana Banka", "Merkez Kasa" }, hepsi.EnumerateArray().Select(e => e.GetProperty("ad").GetString()).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var e in hepsi.EnumerateArray())
            Assert.Equal(new[] { "ad", "doviz", "etiket", "id", "kod", "tur" }, e.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));

        var banka = await Tamam(await s.C.GetAsync(V1 + "/finans/hesaplar?tur=Banka"));
        var tek = Assert.Single(banka.EnumerateArray());
        Assert.Equal("BNK", tek.GetProperty("kod").GetString());
        Assert.Equal("Banka", tek.GetProperty("tur").GetString());
        Assert.Equal("USD", tek.GetProperty("doviz").GetString());
        Assert.Equal("Banka · Ana Banka (BNK · USD)", tek.GetProperty("etiket").GetString());

        await Problem(await s.C.GetAsync(V1 + "/finans/hesaplar?tur=Pos"), HttpStatusCode.BadRequest, "dogrulama", "tur");
    }
}
