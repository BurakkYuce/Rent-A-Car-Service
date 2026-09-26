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
    private static readonly DateTimeOffset RentalStart = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

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

    private static string RandomText(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string NewKey() => Guid.NewGuid().ToString("N"); // 32 görünür ASCII

    /// <summary>Firma + her rolden kullanıcı (rastgele ad/parola) + istisnalar; kiracı verisi racar_app ile.</summary>
    private async Task<Ortam> SetUpEnvironmentAsync(Func<IServiceProvider, Task>? extra = null)
    {
        var tenantId = Guid.NewGuid();
        var code = RandomText("f44");
        var password = WebFixture.RandomPassword();
        var users = Enum.GetValues<Kim>().ToDictionary(k => k, _ => RandomText("u"));

        var opts = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(fx.Pg.OwnerConnectionString).Options;
        await using (var db = new AppDbContext(opts, NullTenantContext.Instance, NullCurrentUser.Instance))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Code = code, Name = code, IsActive = true });
            var hasher = fx.Web.Services.GetRequiredService<IPasswordHasher<User>>();
            foreach (var (kim, name) in users)
            {
                var (rol, branch) = kim switch
                {
                    Kim.Admin => (UserRole.Admin, (string?)null),
                    Kim.Muhasebe or Kim.MuhasebeTersYasak => (UserRole.Muhasebe, null),
                    Kim.OperatorA or Kim.OperatorDuz => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Operator, "SubeB"),
                };
                var u = new User { TenantId = tenantId, UserName = name, DisplayName = name, Rol = rol, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, password);
                db.Users.Add(u);
                // Operatörlere kullanıcı-bazlı FinanceWrite: şube kapsamını izin kapısından AYRI sınamak için.
                if (kim is Kim.OperatorA or Kim.OperatorB)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceWrite", Ver = true });
                if (kim == Kim.MuhasebeTersYasak)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceReverse", Ver = false });
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenantId, true);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(tenantId, role: UserRole.Admin);
        var sp = s.ServiceProvider;
        if (extra is not null) await extra(sp);
        var customers = sp.GetRequiredService<CustomerService>();
        var customer = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Fin", Soyad = "Musteri" });
        var supplier = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Fin", Soyad = "Tedarikci" });
        var (rental, vehicle) = await RentalAsync(sp, customer, RentalStart, day: 3);
        return new Ortam
        {
            TenantId = tenantId, Kod = code, Sifre = password, Kullanicilar = users,
            Musteri = customer, Tedarikci = supplier, Kira = rental, KiraArac = vehicle,
        };
    }

    /// <summary>Yeni araçla kira (günlük 100 TRY, çıkış ofisi SubeA).</summary>
    private static async Task<(Guid Kira, Guid Arac)> RentalAsync(IServiceProvider sp, Guid customer, DateTimeOffset start, int day)
    {
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 FA " + Random.Shared.Next(1000, 9999) });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(day),
            GunlukUcret = 100m, CikisOfisi = "SubeA",
        });
        return (rental, vehicle);
    }

    private async Task<T> ReadAsync<T>(Ortam o, Func<IServiceProvider, Task<T>> read)
    {
        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(o.TenantId, role: UserRole.Admin);
        return await read(s.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Ortam o, Func<AppDbContext, Task<T>> read)
        => ReadAsync(o, async sp =>
        {
            await using var db = await sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
            return await read(db);
        });

    private Task<List<AccountLedgerEntry>> LedgerAsync(Ortam o, Guid sourceId)
        => DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.SourceId == sourceId).ToListAsync());

    private Task<RentalContract> ReadRentalAsync(Ortam o, Guid rental)
        => ReadAsync(o, async sp => (await sp.GetRequiredService<RentalService>().GetAsync(rental))!);

    private Task<decimal> AccountBalanceAsync(Ortam o, Guid account)
        => ReadAsync(o, sp => sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account));

    /// <summary>Kiracının TÜM defter kümeleri dengeli: her (SourceType, SourceId) için Σ borç(baz) == Σ alacak(baz).</summary>
    private async Task IsWholeLedgerBalancedAsync(Ortam o)
    {
        var rows = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().ToListAsync());
        Assert.NotEmpty(rows);
        foreach (var set in rows.GroupBy(e => (e.SourceType, e.SourceId)))
        {
            var debit = set.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            var credit = set.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            Assert.True(debit == credit, $"Dengesiz küme {set.Key}: borç {debit} ≠ alacak {credit}");
        }
    }

    private static void Row(List<AccountLedgerEntry> set, LedgerAccountType type, Guid? reference, LedgerDirection yon,
        decimal amount, string currency = "TRY", decimal exchangeRate = 1m)
        => Assert.Single(set, e => e.AccountType == type && e.AccountRef == reference && e.Direction == yon
                                    && e.Amount.Amount == amount && e.Amount.Currency == currency && e.Amount.Rate == exchangeRate);

    // ------------------------------------------------------------ HTTP

    private static string? CookieValue(HttpResponseMessage r, string name)
    {
        if (!r.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var d in values)
            if (d.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(d[(name.Length + 1)..].Split(';')[0]);
        return null;
    }

    private async Task<Oturum> LoginAsync(Ortam o, Kim kim)
    {
        var c = fx.Web.Client();
        var once = CookieValue(await c.GetAsync(V1 + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = o.Kod, kullanici = o.Kullanicilar[kim], sifre = o.Sifre }),
        };
        req.Headers.Add("X-XSRF-TOKEN", once);
        var r = await c.SendAsync(req);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"giriş başarısız ({kim}): {await r.Content.ReadAsStringAsync()}");
        return new Oturum(c, CookieValue(r, "XSRF-TOKEN")!);
    }

    private static Task<HttpResponseMessage> PostAsync(Oturum s, string path, object? body, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return s.C.SendAsync(req);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<Guid> Id(HttpResponseMessage r) => (await Ok(r)).GetProperty("id").GetGuid();

    /// <summary>ProblemDetails + kod; <paramref name="alan"/> verilirse <c>errors[alan]</c> dolu olmalı.</summary>
    private static async Task<JsonElement> Problem(HttpResponseMessage r, HttpStatusCode status, string code, string? alan = null)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(status == r.StatusCode, $"Beklenen {(int)status}, gelen {(int)r.StatusCode}: {text}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var j = JsonDocument.Parse(text).RootElement.Clone();
        Assert.Equal(code, j.GetProperty("kod").GetString());
        if (alan is not null)
            Assert.True(j.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out var m) && m.GetArrayLength() > 0,
                $"errors[{alan}] yok: {text}");
        return j;
    }

    private static object Collection(Ortam o, decimal amount, string account = "Kasa", Guid? rental = null, bool withoutRental = false,
        string? currency = null, decimal? exchangeRate = null, Guid? collectionKey = null, Guid? customerAccount = null, Guid? accountId = null,
        string? description = "F4.4 test", string? channel = "Masaüstü", DateTimeOffset? date = null)
        => new
        {
            cariId = customerAccount ?? o.Musteri, kiraId = withoutRental ? (Guid?)null : rental ?? o.Kira, tutar = amount, hesap = account,
            doviz = currency, kur = exchangeRate, tahsilatAnahtar = collectionKey, hesapId = accountId, kanal = channel, aciklama = description, tarih = date,
        };

    private Task<int> CollectionCountAsync(Ortam o, Guid rental)
        => DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == rental));

    // ============================================================ tahsilat (E01)

    [Fact]
    public async Task Tahsilat_defter_dengeli_kira_tahsilat_ve_bakiye_islenir()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), NewKey()));

        // Borç Kasa 300 / Alacak Cari(müşteri) 300 — elle oracle.
        var set = await LedgerAsync(o, id);
        Assert.Equal(2, set.Count);
        Row(set, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 300m);
        Row(set, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Credit, 300m);
        Assert.All(set, e => Assert.Equal("Tahsilat", e.SourceType));

        var rental = await ReadRentalAsync(o, o.Kira);
        Assert.Equal(300m, rental.GenelToplam);   // 3 gün × 100
        Assert.Equal(300m, rental.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);
        Assert.Equal(-300m, await AccountBalanceAsync(o, o.Musteri)); // faturasız: cari alacaklı
        await IsWholeLedgerBalancedAsync(o);
    }

    [Fact]
    public async Task Tahsilat_ayni_anahtar_ayni_icerik_409_mukerrer_tek_kayit()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();

        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), key));
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), key), HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(300m, (await ReadRentalAsync(o, o.Kira)).Tahsilat); // 600 DEĞİL
        Assert.Equal(-300m, await AccountBalanceAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Tahsilat_ayni_anahtar_farkli_tutar_409_ilk_kayit_kalir()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();

        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m), key));
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 250m), key), HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        var rental = await ReadRentalAsync(o, o.Kira);
        Assert.Equal(100m, rental.Tahsilat);
        Assert.Equal(200m, rental.Bakiye);
    }

    [Fact]
    public async Task Tahsilat_eszamanli_ayni_anahtar_tek_yazar()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => PostAsync(s, "/finans/tahsilat", Collection(o, 100m), key)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        foreach (var r in responses.Where(r => r.StatusCode != HttpStatusCode.OK))
            await Problem(r, HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(100m, (await ReadRentalAsync(o, o.Kira)).Tahsilat); // 500 DEĞİL
    }

    [Fact]
    public async Task Tahsilat_yenilenen_anahtarla_iki_mesru_tahsilat_iki_kayit()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);

        var id1 = await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m), NewKey()));
        var id2 = await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 200m), NewKey())); // 2xx sonrası yeni anahtar

        Assert.NotEqual(id1, id2);
        Assert.Equal(2, await CollectionCountAsync(o, o.Kira));
        var rental = await ReadRentalAsync(o, o.Kira);
        Assert.Equal(300m, rental.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);
        await IsWholeLedgerBalancedAsync(o);
    }

    [Fact]
    public async Task Tahsilat_basliksiz_ve_deterministik_anahtarsiz_400_hicbir_sey_yazilmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);

        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), key: null),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), key: "kisa"),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(0, await CollectionCountAsync(o, o.Kira));
    }

    // ------------------------------------------------------------ deterministik TahsilatAnahtar

    [Fact]
    public async Task TahsilatAnahtar_ikinci_sekme_ve_ikinci_kullanici_cift_tahsil_etmez()
    {
        var o = await SetUpEnvironmentAsync();
        var tab1 = await LoginAsync(o, Kim.Muhasebe);
        var tab2 = await LoginAsync(o, Kim.Muhasebe);
        var otherUser = await LoginAsync(o, Kim.Admin);
        // Panel DTO'sunun taşıdığı anahtar (ekran yüklenirken: kira + bakiye 300 + işlem sayısı 0).
        var k = RentACar.Web.Finance.CollectionKey.Generate(o.Kira, 300m, 0);

        // Her sekme KENDİ Idempotency-Key başlığını üretir — deterministik anahtar başlıktan önceliklidir.
        await Id(await PostAsync(tab1, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), NewKey()));
        await Problem(await PostAsync(tab2, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), NewKey()),
            HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(otherUser, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), NewKey()),
            HttpStatusCode.Conflict, "mukerrer");
        // Başlıksız da olsa aynı deterministik anahtar → 409 (başlık yalnız deterministik anahtar yoksa zorunlu).
        await Problem(await PostAsync(tab2, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), key: null),
            HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(300m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(-300m, await AccountBalanceAsync(o, o.Musteri)); // −600/−900 DEĞİL
    }

    [Fact]
    public async Task TahsilatAnahtar_basliksiz_kabul_kirasiz_ve_bozuk_baslikla_400()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var k = RentACar.Web.Finance.CollectionKey.Generate(o.Kira, 300m, 0);

        // Kirasız deterministik anahtar anlamsız → alan hatası.
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m, withoutRental: true, collectionKey: k), NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "tahsilatAnahtar");
        // Bozuk başlık deterministik anahtar olsa bile reddedilir.
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), key: "ç-bozuk-başlık-değeri"),
            HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(0, await CollectionCountAsync(o, o.Kira));

        // Başlıksız + deterministik anahtar → yazılır.
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), key: null));
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
    }

    // ------------------------------------------------------------ F4.4 SPA: kira detayındaki tahsilat anahtarı

    private static async Task<JsonElement> DetailAsync(Oturum s, Guid rental)
        => await Ok(await s.C.GetAsync($"{V1}/kiralar/{rental}"));

    /// <summary>
    /// Sabit panel akışı (SPA): anahtar DETAYDAN okunur, başlıksız gönderilir. Aynı anahtarla ikinci gönderim
    /// (çift tık / bayat ekran) 409; detay tazelenince YENİ anahtar gelir ve ikinci MEŞRU tahsilat yazılır.
    /// Beklenenler elle: 300 − 100 = 200 kalan, sonra 200 tahsil → 0; iki kayıt.
    /// </summary>
    [Fact]
    public async Task Detay_tahsilat_anahtari_bayatlar_tazelenince_ikinci_mesru_tahsilat_yazilir()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);

        var t1 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat");
        Assert.Equal(o.Musteri, t1.GetProperty("cariId").GetGuid());
        Assert.Equal(o.Kira, t1.GetProperty("rentalId").GetGuid());
        Assert.Equal("TRY", t1.GetProperty("doviz").GetString());
        Assert.Equal(300m, t1.GetProperty("varsayilanTutar").GetDecimal());
        var k1 = t1.GetProperty("anahtar").GetGuid();

        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m, collectionKey: k1), key: null));
        // Aynı (artık bayat) anahtar: başlık yeni olsa da 409 — ikinci kayıt yok.
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m, collectionKey: k1), NewKey()),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));

        var t2 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat");
        var k2 = t2.GetProperty("anahtar").GetGuid();
        Assert.NotEqual(k1, k2);
        Assert.Equal(200m, t2.GetProperty("varsayilanTutar").GetDecimal());
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 200m, collectionKey: k2), key: null));

        Assert.Equal(2, await CollectionCountAsync(o, o.Kira));
        var rental = await ReadRentalAsync(o, o.Kira);
        Assert.Equal(300m, rental.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);
        // Bakiye 0'da da anahtar dolar (Blazor sabit paneli ön/fazla tahsilata açık); varsayılan tutar 0.
        var t3 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat");
        Assert.Equal(0m, t3.GetProperty("varsayilanTutar").GetDecimal());
        Assert.NotEqual(k2, t3.GetProperty("anahtar").GetGuid());
        await IsWholeLedgerBalancedAsync(o);
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
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var k1 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        var body = Collection(o, 500m, collectionKey: k1);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", body, key: null)); // yanıt "kayboldu"
        var repeat = await Problem(await PostAsync(s, "/finans/tahsilat", body, key: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Contains("zaten kaydedildi", repeat.GetProperty("detail").GetString());
        Assert.DoesNotContain("tekrar deneyin", repeat.GetProperty("detail").GetString());
        var existing = repeat.GetProperty("mevcut");
        Assert.True(existing.GetProperty("ayniIcerik").GetBoolean()); // birebir aynı gövde: kendi tekrarı
        Assert.Equal(id, existing.GetProperty("id").GetGuid());
        Assert.Equal(500m, existing.GetProperty("tutar").GetDecimal());
        Assert.Equal("TRY", existing.GetProperty("doviz").GetString());
        var no = await DbAsync(o, db => db.CashTransactions.AsNoTracking().Where(t => t.Id == id).Select(t => t.No).SingleAsync());
        Assert.Equal(no, existing.GetProperty("belgeNo").GetString());
        Assert.Contains(no, repeat.GetProperty("detail").GetString());
        // 3. tur M-A / G1b: gövde FARKLI (kullanıcı tutarı değiştirdi ya da başka sekme) → kayıt yine tek, ama
        // "zaten kaydedildi" DEĞİL: "başka bir tahsilat yazıldı … girdiğiniz 100,00 TRY YAZILMADI", ayniIcerik=false.
        var different = await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m, collectionKey: k1), NewKey()),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(id, different.GetProperty("mevcut").GetProperty("id").GetGuid());
        Assert.False(different.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Contains("girdiğiniz 100,00 TRY YAZILMADI", different.GetProperty("detail").GetString());
        Assert.Contains("500,00 TRY", different.GetProperty("detail").GetString());
        Assert.DoesNotContain("zaten kaydedildi", different.GetProperty("detail").GetString());
        // Aynı tutar ama başka hesap türü (Banka) de "aynı içerik" değildir.
        var fromBank = await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 500m, account: "Banka", collectionKey: k1), key: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(fromBank.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        // Ölçek farkı (500 vs "500.00") aynı içeriktir (decimal eşitliği).
        var scale = await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 500.00m, collectionKey: k1), key: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.True(scale.GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean());
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(-200m, (await ReadRentalAsync(o, o.Kira)).Bakiye);

        // Bayat anahtar: kirada BAŞKA bir işlem oldu (bu anahtarla yazılmış kayıt YOK) → 409, mevcut yok.
        var k2 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 50m, hesap = "Kasa" }, NewKey()));
        var stale = await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 50m, collectionKey: k2), key: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(stale.TryGetProperty("mevcut", out _));
        Assert.Contains("tutarı yeniden girin", stale.GetProperty("detail").GetString());
        Assert.Equal(1, await DbAsync(o, db => db.CashTransactions.AsNoTracking()
            .CountAsync(t => t.RentalId == o.Kira && t.Tip == CashTransactionType.Tahsilat))); // + 1 ödeme, tahsilat hâlâ tek
        await IsWholeLedgerBalancedAsync(o);
    }

    /// <summary>
    /// F4.4 L-1: "aynı içerik" yalnız tutar/döviz/hesap değil; kur, açıklama ve kanal da birebir olmalı. Kurunu,
    /// açıklamasını ya da kanalını değiştirmiş tekrar "zaten kaydedildi" DEMEZ (form silinmesin). Açıklamanın kenar
    /// boşlukları, açık kur 1 ≡ boş kur (TRY) ve boş kur ≡ firmanın sabit kuru (USD 30) aynı içeriktir.
    /// Beklenenler elle: TRY 500 (açıklama "F4.4 test", kanal Masaüstü, kur boş=1) ve USD 10 @ sabit 30 yazıldı;
    /// her tekrar 409, kayıt sayısı sabit (TRY 1 + USD 1 = 2).
    /// </summary>
    [Fact]
    public async Task Tahsilat_ayni_icerik_kur_aciklama_kanal_da_karsilastirilir()
    {
        var o = await SetUpEnvironmentAsync(sp => sp.GetRequiredService<FixedExchangeRateService>()
            .UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 30m, Aktif = true }));
        var s = await LoginAsync(o, Kim.Muhasebe);

        async Task<bool> SameContent(object body)
            => (await Problem(await PostAsync(s, "/finans/tahsilat", body, key: null), HttpStatusCode.Conflict, "mukerrer"))
                .GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean();

        var k1 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 500m, collectionKey: k1), key: null));
        Assert.True(await SameContent(Collection(o, 500m, collectionKey: k1)));
        Assert.True(await SameContent(Collection(o, 500m, collectionKey: k1, description: "  F4.4 test ")));
        Assert.True(await SameContent(Collection(o, 500m, collectionKey: k1, exchangeRate: 1m)));
        Assert.False(await SameContent(Collection(o, 500m, collectionKey: k1, description: "kapora")));
        Assert.False(await SameContent(Collection(o, 500m, collectionKey: k1, description: null)));
        Assert.False(await SameContent(Collection(o, 500m, collectionKey: k1, channel: "Mobil")));
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));

        var k2 = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, currency: "USD", collectionKey: k2), key: null));
        Assert.True(await SameContent(Collection(o, 10m, currency: "USD", collectionKey: k2)));
        Assert.True(await SameContent(Collection(o, 10m, currency: "USD", exchangeRate: 30m, collectionKey: k2)));
        Assert.False(await SameContent(Collection(o, 10m, currency: "USD", exchangeRate: 31m, collectionKey: k2)));
        Assert.Equal(2, await CollectionCountAsync(o, o.Kira));
        await IsWholeLedgerBalancedAsync(o);
    }

    /// <summary>
    /// 5. tur LOW-3: açık işlem tarihi de "aynı içerik"in parçası (boş tarih = "şimdi", karşılaştırılamaz → farksız);
    /// TRY'de açık kur ≠ 1 olan tekrar mükerrer 409'u DEĞİL, 400 dogrulama (kur) alır — kural anahtardan önce.
    /// Beklenenler elle: 2 gün önce 10:00 UTC'li 250 TRY yazıldı; tekrarlar 409/400, kayıt sayısı hep 1.
    /// </summary>
    [Fact]
    public async Task Tahsilat_ayni_icerik_tarihi_karsilastirir_try_kur_hatasi_dogrulama_doner()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var date = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-2).AddHours(10), TimeSpan.Zero);

        async Task<bool> SameContent(object body)
            => (await Problem(await PostAsync(s, "/finans/tahsilat", body, key: null), HttpStatusCode.Conflict, "mukerrer"))
                .GetProperty("mevcut").GetProperty("ayniIcerik").GetBoolean();

        var k = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 250m, collectionKey: k, date: date), key: null));
        Assert.True(await SameContent(Collection(o, 250m, collectionKey: k, date: date)));
        Assert.True(await SameContent(Collection(o, 250m, collectionKey: k)));
        Assert.False(await SameContent(Collection(o, 250m, collectionKey: k, date: date.AddDays(1))));
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 250m, collectionKey: k, date: date, exchangeRate: 5m), key: null),
            HttpStatusCode.BadRequest, "dogrulama", "kur");
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(250m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
        await IsWholeLedgerBalancedAsync(o);
    }

    /// <summary>
    /// 3. tur M-A (G2): iki kullanıcı AYNI detayla açık (aynı anahtar K). A 100 tahsil eder; B ön-dolu 300'ü gönderir
    /// → 409 mevcut, ayniIcerik=false, "girdiğiniz 300,00 TRY YAZILMADI" (B'nin parası kaydedilmiş sanılmasın).
    /// Yalnız A'nın 100'ü yazılı; B detayı tazeleyip yeni anahtarla bilinçli gönderince ikinci kayıt yazılır.
    /// </summary>
    [Fact]
    public async Task Tahsilat_iki_kullanici_ayni_anahtar_farkli_tutar_yazilmadi_der()
    {
        var o = await SetUpEnvironmentAsync();
        var a = await LoginAsync(o, Kim.Muhasebe);
        var b = await LoginAsync(o, Kim.Admin);
        var k = (await DetailAsync(a, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        Assert.Equal(k, (await DetailAsync(b, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid());

        var idA = await Id(await PostAsync(a, "/finans/tahsilat", Collection(o, 100m, collectionKey: k), key: null));
        var r = await Problem(await PostAsync(b, "/finans/tahsilat", Collection(o, 300m, collectionKey: k), key: null),
            HttpStatusCode.Conflict, "mukerrer");
        var m = r.GetProperty("mevcut");
        Assert.Equal(idA, m.GetProperty("id").GetGuid());
        Assert.Equal(100m, m.GetProperty("tutar").GetDecimal());
        Assert.False(m.GetProperty("ayniIcerik").GetBoolean());
        Assert.Contains("girdiğiniz 300,00 TRY YAZILMADI", r.GetProperty("detail").GetString());
        Assert.Equal(1, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(100m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);

        var k2 = (await DetailAsync(b, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(b, "/finans/tahsilat", Collection(o, 200m, collectionKey: k2), key: null));
        Assert.Equal(2, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(300m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
        await IsWholeLedgerBalancedAsync(o);
    }

    /// <summary>HIGH-1 kapsam: başka kiranın (aynı cari) tahsilatına ait anahtar bu kirayla gönderilirse o kaydın
    /// no/tutarı SIZMAZ — "zaten kaydedildi" yalnız aynı kiranın tahsilatı için; diğeri "ait değil" 409'u.</summary>
    [Fact]
    public async Task Tahsilat_baska_kiranin_anahtari_mevcut_sizdirmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var (rentalB, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, RentalStart.AddDays(10), day: 2));
        var s = await LoginAsync(o, Kim.Muhasebe);
        var kA = (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").GetProperty("anahtar").GetGuid();
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m, collectionKey: kA), key: null));

        var r = await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m, rental: rentalB, collectionKey: kA), key: null),
            HttpStatusCode.Conflict, "mukerrer");
        Assert.False(r.TryGetProperty("mevcut", out _));
        Assert.Equal(0, await CollectionCountAsync(o, rentalB));
    }

    [Fact]
    public async Task Detay_tahsilat_finans_izni_yoksa_ve_iptal_kirada_null()
    {
        var o = await SetUpEnvironmentAsync();
        // Operatör (FinanceWrite yok): kira okunur, tahsilat verisi YOK.
        var op = await LoginAsync(o, Kim.OperatorDuz);
        var d = await DetailAsync(op, o.Kira);
        Assert.False(d.GetProperty("yetkiler").GetProperty("finans").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("tahsilat").ValueKind);

        // İptal kira (tahsilatsız → iptal edilebilir): finans iznine rağmen tahsilat verisi yok (uç da reddeder).
        Assert.True(await ReadAsync(o, sp => sp.GetRequiredService<RentalService>().CancelAsync(o.Kira)));
        var s = await LoginAsync(o, Kim.Muhasebe);
        Assert.Equal(JsonValueKind.Null, (await DetailAsync(s, o.Kira)).GetProperty("tahsilat").ValueKind);
    }

    // ------------------------------------------------------------ çok döviz

    [Fact]
    public async Task Dovizli_tahsilat_acik_kur_aynen_kullanilir()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, currency: "USD", exchangeRate: 32m), NewKey()));

        var set = await LedgerAsync(o, id);
        Row(set, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 10m, "USD", 32m);
        Row(set, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Credit, 10m, "USD", 32m);
        var rental = await ReadRentalAsync(o, o.Kira); // TRY kira: delta = 10 × 32 = 320 baz
        Assert.Equal(320m, rental.Tahsilat);
        Assert.Equal(-20m, rental.Bakiye);
        Assert.Equal(-320m, await AccountBalanceAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Dovizli_tahsilat_bos_kur_firma_sabit_kurundan_cozulur()
    {
        var o = await SetUpEnvironmentAsync(sp => sp.GetRequiredService<FixedExchangeRateService>()
            .UpsertAsync(new SabitKurInput { Kod = "USD", Kur = 30m, Aktif = true }));
        var s = await LoginAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, currency: "USD"), NewKey()));

        var set = await LedgerAsync(o, id);
        Row(set, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 10m, "USD", 30m);
        Row(set, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Credit, 10m, "USD", 30m);
        var rental = await ReadRentalAsync(o, o.Kira); // 10 × 30 = 300 → bakiye kapanır
        Assert.Equal(300m, rental.Tahsilat);
        Assert.Equal(0m, rental.Bakiye);
    }

    [Fact]
    public async Task Dovizli_tahsilat_kur_bulunamazsa_red_sessiz_1_yok()
    {
        var o = await SetUpEnvironmentAsync(); // GBP için sabit kur/TCMB kaydı yok
        var s = await LoginAsync(o, Kim.Muhasebe);

        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, currency: "GBP", withoutRental: true), NewKey()),
            HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));
    }

    // ------------------------------------------------------------ alan hataları + tutarlılık

    [Fact]
    public async Task Tahsilat_alan_hatalari_errors_alan_tasir_hicbir_sey_yazilmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var otherAccount = await ReadAsync(o, sp => sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Baska", Soyad = "Cari" }));

        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 0m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "tutar");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, account: "Pos"), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, account: ""), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, currency: "XX"), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "doviz");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, exchangeRate: -1m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kur");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, customerAccount: Guid.Empty), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, rental: Guid.NewGuid()), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        // Kira tahsilatı başka cari adına yazılamaz (kira faturası kiranın müşterisini borçlandırır).
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, customerAccount: otherAccount), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        var channel = new { cariId = o.Musteri, kiraId = o.Kira, tutar = 10m, hesap = "Kasa", kanal = "Faks" };
        await Problem(await PostAsync(s, "/finans/tahsilat", channel, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kanal");
        var future = new { cariId = o.Musteri, kiraId = o.Kira, tutar = 10m, hesap = "Kasa", tarih = DateTimeOffset.UtcNow.AddDays(10) };
        await Problem(await PostAsync(s, "/finans/tahsilat", future, NewKey()), HttpStatusCode.BadRequest, "dogrulama", "tarih");

        Assert.Equal(0, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(0m, await AccountBalanceAsync(o, otherAccount));
    }

    [Fact]
    public async Task Iptal_kiraya_tahsilat_400_iade_odemesi_baglanabilir()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 100m), NewKey()));
        // F4.1 adversarial M3: tahsilatlı kira artık servisten İPTAL EDİLEMEZ (önce iade). "İptal + tahsilat"
        // yalnız ESKİ veride vardır → reddi doğrula, durumu eski kayıt benzetimi olarak doğrudan yaz.
        await Assert.ThrowsAsync<RentACar.Application.Common.ValidationException>(
            () => ReadAsync(o, sp => sp.GetRequiredService<RentalService>().CancelAsync(o.Kira)));
        await DbAsync(o, async db =>
        {
            var rental = await db.Rentals.SingleAsync(x => x.Id == o.Kira);
            rental.Durum = RentalStatus.Iptal;
            return await db.SaveChangesAsync();
        });

        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 50m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        Assert.Equal(100m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);

        // Alınan 100'ün iadesi kiraya bağlanır: kira Tahsilat 0'a döner, cari sıfırlanır.
        await Id(await PostAsync(s, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 100m, hesap = "Kasa" }, NewKey()));
        Assert.Equal(0m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Tahsilat_spesifik_kasa_hesabina_yazilir()
    {
        Guid cash = Guid.Empty;
        var o = await SetUpEnvironmentAsync(async sp => cash = await sp.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = true }));
        var s = await LoginAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m, accountId: cash), NewKey()));
        Row(await LedgerAsync(o, id), LedgerAccountType.Kasa, cash, LedgerDirection.Debit, 300m);
        // Tür çelişkisi (Kasa hesabı, Banka işlemi) servisten reddedilir.
        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 10m, account: "Banka", accountId: cash), NewKey()),
            HttpStatusCode.BadRequest, "dogrulama");
    }

    // ============================================================ ödeme (E02)

    [Fact]
    public async Task Odeme_defter_dengeli_ayni_anahtar_409()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();
        var body = new { cariId = o.Musteri, tutar = 50m, hesap = "Banka", aciklama = "Giden havale" };

        var id = await Id(await PostAsync(s, "/finans/odeme", body, key));
        var set = await LedgerAsync(o, id);
        Assert.Equal(2, set.Count);
        Row(set, LedgerAccountType.Banka, null, LedgerDirection.Credit, 50m);
        Row(set, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Debit, 50m);
        Assert.Equal(50m, await AccountBalanceAsync(o, o.Musteri));

        await Problem(await PostAsync(s, "/finans/odeme", body, key), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/finans/odeme", body, key: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(50m, await AccountBalanceAsync(o, o.Musteri)); // 100 DEĞİL
        await IsWholeLedgerBalancedAsync(o);
    }

    // ============================================================ fatura (E15)

    [Fact]
    public async Task Fatura_kiradan_kesilir_ikinci_kesim_400_tek_fatura()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);

        var id = await Id(await PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, key: null));

        var invoice = await DbAsync(o, db => db.Invoices.AsNoTracking().SingleAsync(i => i.Id == id));
        Assert.Equal(300m, invoice.GenelToplam);  // 3 × 100 brüt
        Assert.Equal(250m, invoice.NetTutar);     // 300 / 1,20
        Assert.Equal(50m, invoice.KdvTutar);
        var set = await LedgerAsync(o, id);
        Assert.Equal(3, set.Count);
        Row(set, LedgerAccountType.Cari, o.Musteri, LedgerDirection.Debit, 300m);
        Row(set, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 250m);
        Row(set, LedgerAccountType.Kdv, null, LedgerDirection.Credit, 50m);
        Assert.Equal(300m, await AccountBalanceAsync(o, o.Musteri));

        var second = await Problem(await PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama");
        Assert.Contains("zaten tam faturalanmış", second.GetProperty("detail").GetString());
        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(300m, await AccountBalanceAsync(o, o.Musteri));

        // Tahsilat faturalı cariyi sıfırlar.
        await Id(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), NewKey()));
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));
        await IsWholeLedgerBalancedAsync(o);
    }

    [Fact]
    public async Task Fatura_kira_yok_ya_da_bos_400_kiraId()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        await Problem(await PostAsync(s, "/finans/fatura", new { kiraId = Guid.NewGuid() }, null), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        await Problem(await PostAsync(s, "/finans/fatura", new { kiraId = Guid.Empty }, null), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
    }

    // ============================================================ dönem faturası (E18/E19)

    [Fact]
    public async Task Donem_faturasi_kes_ve_tahsil_cift_gonderim_sessiz_tek_fatura_tek_tahsilat()
    {
        var o = await SetUpEnvironmentAsync();
        // 15 Oca 2027 + 90 gün × 100 = 9000; dönem 1 = 15 Oca–15 Şub = 31 gün → 31/90 × 9000 = 3100.
        var (rental, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), day: 90));
        var s = await LoginAsync(o, Kim.Muhasebe);
        var body = new { kiraId = rental, donemSira = 1, tahsilat = true, hesap = "Kasa" };

        var first = await Ok(await PostAsync(s, "/finans/donem-fatura", body, null));
        var invoiceId = first.GetProperty("faturaId").GetGuid();
        Assert.True(first.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("bilgi").ValueKind);

        Assert.Equal(3100m, await DbAsync(o, db => db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => i.GenelToplam).SingleAsync()));
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));      // fatura borcu 3100 == tahsilat 3100
        Assert.Equal(3100m, (await ReadRentalAsync(o, rental)).Tahsilat);

        // Çift gönderim (başka başlıkla da): sessiz, aynı fatura, tahsilat YAZILMAZ ve bu gizlenmez.
        var second = await Ok(await PostAsync(s, "/finans/donem-fatura", body, NewKey()));
        Assert.Equal(invoiceId, second.GetProperty("faturaId").GetGuid());
        Assert.False(second.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal("Bu dönemin tahsilatı daha önce alınmış; yeni tahsilat yazılmadı.", second.GetProperty("bilgi").GetString());

        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync(i => i.KaynakKiraId == rental || i.RentalId == rental)));
        Assert.Equal(1, await CollectionCountAsync(o, rental));
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));      // −3100 DEĞİL
        Assert.Equal(3100m, (await ReadRentalAsync(o, rental)).Tahsilat);
        await IsWholeLedgerBalancedAsync(o);
    }

    [Fact]
    public async Task Donem_faturasi_tahsilatsiz_yalniz_fatura_tahsilatta_hesap_zorunlu()
    {
        var o = await SetUpEnvironmentAsync();
        var (rental, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), day: 90));
        var s = await LoginAsync(o, Kim.Muhasebe);

        await Problem(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rental, donemSira = 1, tahsilat = true }, null),
            HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await Problem(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rental, donemSira = 0 }, null),
            HttpStatusCode.BadRequest, "dogrulama", "donemSira");

        var y = await Ok(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rental, donemSira = 1 }, null));
        Assert.False(y.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(JsonValueKind.Null, y.GetProperty("bilgi").ValueKind); // tahsilat istenmedi → bilgi yok
        Assert.Equal(3100m, await AccountBalanceAsync(o, o.Musteri));
        Assert.Equal(0, await CollectionCountAsync(o, rental));
    }

    // ============================================================ dış hizmet (E33) + iptal (E34)

    [Fact]
    public async Task Dis_hizmet_defterli_ayni_anahtar_409_iptal_FinanceReverse_ister()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();
        var body = new
        {
            kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Çekici", hizmetBedeli = 1000m, komisyonOran = 10m,
            hizmetAlinanFirma = "Yol Yardım", komisyonFaturaNo = "KF-1",
        };

        var id = await Id(await PostAsync(s, "/finans/dis-hizmet", body, key));
        var set = await LedgerAsync(o, id);
        Assert.Equal(4, set.Count);
        Row(set, LedgerAccountType.Gider, o.KiraArac, LedgerDirection.Debit, 1000m);  // gider ARAÇTA
        Row(set, LedgerAccountType.Cari, o.Tedarikci, LedgerDirection.Credit, 1000m);
        Row(set, LedgerAccountType.Cari, o.Tedarikci, LedgerDirection.Debit, 100m);   // 1000 × %10
        Row(set, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 100m);
        Assert.Equal(-900m, await AccountBalanceAsync(o, o.Tedarikci));

        await Problem(await PostAsync(s, "/finans/dis-hizmet", body, key), HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", body, key: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        Assert.Equal(1, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));

        // FinanceWrite VAR, FinanceReverse YASAK → 403; kayıt iptal edilmez.
        var banned = await LoginAsync(o, Kim.MuhasebeTersYasak);
        await Problem(await PostAsync(banned, $"/finans/dis-hizmet/{id}/iptal", null, null), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(DisHizmetDurum.Kayitli, (await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().SingleAsync(d => d.Id == id))).Durum);

        var cancel = await PostAsync(s, $"/finans/dis-hizmet/{id}/iptal", null, null);
        Assert.True(cancel.StatusCode == HttpStatusCode.NoContent, await cancel.Content.ReadAsStringAsync());
        Assert.Equal(DisHizmetDurum.Iptal, (await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().SingleAsync(d => d.Id == id))).Durum);
        Assert.Equal(8, (await LedgerAsync(o, id)).Count);             // ters kayıt: silme yok
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Tedarikci));        // net sıfır

        var second = await Problem(await PostAsync(s, $"/finans/dis-hizmet/{id}/iptal", null, null), HttpStatusCode.BadRequest, "dogrulama");
        Assert.Equal("Kayıt zaten iptal edilmiş.", second.GetProperty("detail").GetString());
        Assert.Equal(8, (await LedgerAsync(o, id)).Count);
        await IsWholeLedgerBalancedAsync(o);
    }

    [Fact]
    public async Task Dis_hizmet_alan_hatalari()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        object G(decimal charge = 100m, decimal? rate = 5m, string? service = "Vale", Guid? account = null, Guid? rental = null)
            => new { kiraId = rental ?? o.Kira, cariId = account ?? o.Tedarikci, alinanHizmet = service, hizmetBedeli = charge, komisyonOran = rate };

        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(charge: 0m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "hizmetBedeli");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(rate: 150m), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "komisyonOran");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(service: " "), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "alinanHizmet");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(account: Guid.Empty), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "cariId");
        await Problem(await PostAsync(s, "/finans/dis-hizmet", G(rental: Guid.NewGuid()), NewKey()), HttpStatusCode.BadRequest, "dogrulama", "kiraId");
        Assert.Equal(0, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));
    }

    // ============================================================ depozito al (E09) / irat (E12)

    [Fact]
    public async Task Depozito_al_ayni_icerik_sessiz_ayni_id_farkli_icerik_409()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();
        var body = new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" };

        var id = await Id(await PostAsync(s, "/finans/depozito/al", body, key));
        var set = await LedgerAsync(o, id);
        Assert.Equal(2, set.Count);
        Row(set, LedgerAccountType.Kasa, null, LedgerDirection.Debit, 500m);
        Row(set, LedgerAccountType.Depozito, o.Musteri, LedgerDirection.Credit, 500m);
        Assert.Equal(500m, await ReadAsync(o, sp => sp.GetRequiredService<DepositService>().GetBalanceAsync(o.Musteri)));

        // Birebir aynı tekrar: sessiz başarı, AYNI id, ikinci yazım yok.
        Assert.Equal(id, await Id(await PostAsync(s, "/finans/depozito/al", body, key)));
        // Aynı anahtar, farklı tutar: 409 farklı içerik, hiçbir şey yazılmaz.
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 600m, hesap = "Kasa" }, key),
            HttpStatusCode.Conflict, "mukerrer");
        // Aynı anahtar, farklı hesap: 409.
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 500m, hesap = "Banka" }, key),
            HttpStatusCode.Conflict, "mukerrer");

        Assert.Equal(2, (await LedgerAsync(o, id)).Count);
        Assert.Equal(500m, await ReadAsync(o, sp => sp.GetRequiredService<DepositService>().GetBalanceAsync(o.Musteri)));
        await Problem(await PostAsync(s, "/finans/depozito/al", body, key: null), HttpStatusCode.BadRequest, "dogrulama", "Idempotency-Key");
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 5m, hesap = "Pos" }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama", "hesap");
        await IsWholeLedgerBalancedAsync(o);
    }

    [Fact]
    public async Task Depozito_irat_gelir_yazar_kiraya_atfedilir_tekrar_sessiz_farkli_409()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" }, NewKey()));
        var key = NewKey();
        var body = new { cariId = o.Musteri, tutar = 200m, kiraId = o.Kira, aciklama = "Hasar kesintisi" };

        var id = await Id(await PostAsync(s, "/finans/depozito/irat", body, key));
        var set = await LedgerAsync(o, id);
        Assert.Equal(2, set.Count);
        Row(set, LedgerAccountType.Depozito, o.Musteri, LedgerDirection.Debit, 200m);
        Row(set, LedgerAccountType.Gelir, null, LedgerDirection.Credit, 200m);
        Assert.Equal(o.Kira, await DbAsync(o, db => db.DepozitoIratlar.AsNoTracking().Where(d => d.Id == id).Select(d => d.RentalId).SingleAsync()));
        Assert.Equal(300m, await ReadAsync(o, sp => sp.GetRequiredService<DepositService>().GetBalanceAsync(o.Musteri)));

        Assert.Equal(id, await Id(await PostAsync(s, "/finans/depozito/irat", body, key)));
        await Problem(await PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 150m, kiraId = o.Kira }, key),
            HttpStatusCode.Conflict, "mukerrer");
        await Problem(await PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 200m }, key), // kira atfı farklı
            HttpStatusCode.Conflict, "mukerrer");
        Assert.Equal(300m, await ReadAsync(o, sp => sp.GetRequiredService<DepositService>().GetBalanceAsync(o.Musteri)));

        // Tutulanı aşan irat reddedilir (iş kuralı servis/repo'da).
        await Problem(await PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 301m }, NewKey()),
            HttpStatusCode.BadRequest, "dogrulama");
        await IsWholeLedgerBalancedAsync(o);
    }

    // ============================================================ izin + şube kapsamı

    [Fact]
    public async Task FinanceWrite_olmayan_operator_403_hicbir_sey_yazilmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.OperatorDuz);

        await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), NewKey()), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 5m, hesap = "Kasa" }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await s.C.GetAsync(V1 + "/finans/hesaplar"), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.Equal(0, await CollectionCountAsync(o, o.Kira));
    }

    [Fact]
    public async Task Baska_subenin_operatoru_403_kendi_subesindeki_yazar()
    {
        var o = await SetUpEnvironmentAsync();
        var b = await LoginAsync(o, Kim.OperatorB); // SubeB + kullanıcı-bazlı FinanceWrite; kira SubeA'da

        await Problem(await PostAsync(b, "/finans/tahsilat", Collection(o, 300m), NewKey()), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 5m, hesap = "Kasa" }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/fatura", new { kiraId = o.Kira }, null), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/donem-fatura", new { kiraId = o.Kira, donemSira = 1 }, null), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/dis-hizmet",
                new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await PostAsync(b, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 1m, kiraId = o.Kira }, NewKey()),
            HttpStatusCode.Forbidden, "yetki_yok");

        Assert.Equal(0, await CollectionCountAsync(o, o.Kira));
        Assert.Equal(0, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().CountAsync()));

        var a = await LoginAsync(o, Kim.OperatorA); // SubeA — kapsamda
        await Id(await PostAsync(a, "/finans/tahsilat", Collection(o, 300m), NewKey()));
        Assert.Equal(300m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
    }

    [Fact]
    public async Task Pilot_olmayan_firmada_finans_uclari_403_pilot_degil()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        await fx.MakePilotAsync(o.TenantId, false);
        try
        {
            await Problem(await PostAsync(s, "/finans/tahsilat", Collection(o, 300m), NewKey()), HttpStatusCode.Forbidden, "pilot_degil");
            Assert.Equal(0, await CollectionCountAsync(o, o.Kira));
        }
        finally { await fx.MakePilotAsync(o.TenantId, true); }
    }

    // ============================================================ hesap seçimi

    [Fact]
    public async Task Hesaplar_yalniz_aktif_ture_gore_suzulur_iban_donmez()
    {
        var o = await SetUpEnvironmentAsync(async sp =>
        {
            var h = sp.GetRequiredService<FinancialAccountService>();
            await h.CreateAsync(new FinancialAccountInput { Kod = "MRK", Ad = "Merkez Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = true });
            await h.CreateAsync(new FinancialAccountInput { Kod = "BNK", Ad = "Ana Banka", Tur = "Banka", Doviz = "USD", Iban = "TR000000000000000000000001", Aktif = true });
            await h.CreateAsync(new FinancialAccountInput { Kod = "ESK", Ad = "Eski Kasa", Tur = "Kasa", Aktif = false });
        });
        var s = await LoginAsync(o, Kim.Muhasebe);

        var all = await Ok(await s.C.GetAsync(V1 + "/finans/hesaplar"));
        Assert.Equal(new[] { "Ana Banka", "Merkez Kasa" }, all.EnumerateArray().Select(e => e.GetProperty("ad").GetString()).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var e in all.EnumerateArray())
            Assert.Equal(new[] { "ad", "doviz", "etiket", "id", "kod", "tur" }, e.EnumerateObject().Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal));

        var bank = await Ok(await s.C.GetAsync(V1 + "/finans/hesaplar?tur=Banka"));
        var tek = Assert.Single(bank.EnumerateArray());
        Assert.Equal("BNK", tek.GetProperty("kod").GetString());
        Assert.Equal("Banka", tek.GetProperty("tur").GetString());
        Assert.Equal("USD", tek.GetProperty("doviz").GetString());
        Assert.Equal("Banka · Ana Banka (BNK · USD)", tek.GetProperty("etiket").GetString());

        await Problem(await s.C.GetAsync(V1 + "/finans/hesaplar?tur=Pos"), HttpStatusCode.BadRequest, "dogrulama", "tur");
    }
}
