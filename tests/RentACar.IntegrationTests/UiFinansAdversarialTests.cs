using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.FaturaDonemleri;
using RentACar.Application.Finance;
using RentACar.Application.FinancialAccounts;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// F4.4a (PR #257) bağımsız adversarial incelemesinin probe'ları, KALICI test olarak. Her test DOĞRU davranışı
/// tarif eder; bulgular: HIGH-1 (TRY'de açık kur ≠ 1 bazı şişiriyordu), MEDIUM-1 (büyük/uzun girdi 500),
/// MEDIUM-2 (var olmayan/başka kiracının carisine para), MEDIUM-3 (tahsilatAnahtar kiraya bağlı değildi; dönem
/// tahsilatı başka işlemin anahtarıyla bastırılabiliyordu), L1 (depozitoda hesap-döviz çiti), L2 (4 ondalıkta
/// sıfır kalan tutar), L5 (iptalde kapsam durumdan önce). Yeşil probe'lar da (eşzamanlılık, çapraz kiracı,
/// CSRF, dövizli kira, işaret oracle'ı) gerileme kilidi olarak burada.
/// <para>BAĞIMSIZ ORACLE: beklenen tutarlar elle; kimlikler çalışma anında rastgele (sabit parola yok).</para>
/// </summary>
[Collection("web")]
public sealed class UiFinansAdversarialTests(WebFixture fx)
{
    private const string V1 = "/api/ui/v1";
    private static readonly DateTimeOffset KiraBas = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    private enum Kim { Admin, Muhasebe, OperatorA, OperatorB, OperatorBTers }

    private sealed class Ortam
    {
        public required Guid TenantId { get; init; }
        public required string Kod { get; init; }
        public required string Sifre { get; init; }
        public required Dictionary<Kim, string> Kullanicilar { get; init; }
        public required Guid Musteri { get; init; }
        public required Guid Tedarikci { get; init; }
        public required Guid Kira { get; init; }
        public required Guid KiraArac { get; init; }
    }

    private sealed record Oturum(HttpClient C, string Xsrf);

    private static string Rastgele(string onek) => onek + Guid.NewGuid().ToString("N")[..10];
    private static string YeniAnahtar() => Guid.NewGuid().ToString("N");

    private async Task<Ortam> OrtamKurAsync(Func<IServiceProvider, Task>? ek = null)
    {
        var tenantId = Guid.NewGuid();
        var kod = Rastgele("adv");
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
                    Kim.Muhasebe => (UserRole.Muhasebe, null),
                    Kim.OperatorA => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Operator, "SubeB"),
                };
                var u = new User { TenantId = tenantId, UserName = ad, DisplayName = ad, Rol = rol, AtanmisSube = sube, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, sifre);
                db.Users.Add(u);
                if (kim is Kim.OperatorA or Kim.OperatorB or Kim.OperatorBTers)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceWrite", Ver = true });
                if (kim is Kim.OperatorBTers)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceReverse", Ver = true });
            }
            await db.SaveChangesAsync();
        }
        await fx.PilotYapAsync(tenantId, true);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(tenantId, role: UserRole.Admin);
        var sp = s.ServiceProvider;
        if (ek is not null) await ek(sp);
        var cariler = sp.GetRequiredService<CustomerService>();
        var musteri = await cariler.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Adv", Soyad = "Musteri" });
        var tedarikci = await cariler.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Adv", Soyad = "Tedarikci" });
        var (kira, arac) = await KiraAsync(sp, musteri, KiraBas, gun: 3);
        return new Ortam
        {
            TenantId = tenantId, Kod = kod, Sifre = sifre, Kullanicilar = kullanicilar,
            Musteri = musteri, Tedarikci = tedarikci, Kira = kira, KiraArac = arac,
        };
    }

    private static async Task<(Guid Kira, Guid Arac)> KiraAsync(IServiceProvider sp, Guid musteri, DateTimeOffset bas, int gun,
        string ofis = "SubeA", string? doviz = null)
    {
        var arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 AD " + Random.Shared.Next(1000, 9999) });
        var kira = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = musteri, VehicleId = arac, BasTar = bas, BitTar = bas.AddDays(gun),
            GunlukUcret = 100m, CikisOfisi = ofis, Doviz = doviz,
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

    private Task<int> DefterSayisiAsync(Ortam o)
        => DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().CountAsync());

    private Task<RentalContract> KiraOkuAsync(Ortam o, Guid kira)
        => OkuAsync(o, async sp => (await sp.GetRequiredService<RentalService>().GetAsync(kira))!);

    private Task<decimal> CariBakiyeAsync(Ortam o, Guid cari)
        => OkuAsync(o, sp => sp.GetRequiredService<CashService>().GetAccountBalanceAsync(cari));

    private Task<decimal> DepozitoBakiyeAsync(Ortam o, Guid cari)
        => OkuAsync(o, sp => sp.GetRequiredService<DepositService>().GetBalanceAsync(cari));

    private async Task<List<string>> DengesizKumelerAsync(Ortam o)
    {
        var satirlar = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().ToListAsync());
        var hatalar = new List<string>();
        foreach (var kume in satirlar.GroupBy(e => (e.SourceType, e.SourceId)))
        {
            var borc = kume.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            var alacak = kume.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            if (borc != alacak) hatalar.Add($"Dengesiz küme {kume.Key}: {borc} ≠ {alacak}");
        }
        return hatalar;
    }

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

    private static Task<HttpResponseMessage> PostAsync(Oturum s, string yol, object? govde, string? anahtar, string? xsrf = "__oturum")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + yol);
        if (govde is not null) req.Content = JsonContent.Create(govde);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf == "__oturum" ? s.Xsrf : xsrf);
        if (anahtar is not null) req.Headers.Add("Idempotency-Key", anahtar);
        return s.C.SendAsync(req);
    }

    private static async Task<(int Status, string? Kod, string Govde)> Oku(HttpResponseMessage r)
    {
        var metin = await r.Content.ReadAsStringAsync();
        string? kod = null;
        try
        {
            var j = JsonDocument.Parse(metin).RootElement;
            if (j.ValueKind == JsonValueKind.Object && j.TryGetProperty("kod", out var k)) kod = k.GetString();
        }
        catch (JsonException) { }
        return ((int)r.StatusCode, kod, metin);
    }

    private static async Task<Guid> Id(HttpResponseMessage r)
    {
        var (st, _, g) = await Oku(r);
        Assert.True(st == 200, $"{st}: {g}");
        return JsonDocument.Parse(g).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(int Status, string? Kod, string Govde)> Problem400(HttpResponseMessage r, string alan)
    {
        var sonuc = await Oku(r);
        Assert.True(sonuc.Status == 400 && sonuc.Kod == "dogrulama", $"beklenen 400 dogrulama, gelen {sonuc.Status}: {sonuc.Govde}");
        var j = JsonDocument.Parse(sonuc.Govde).RootElement;
        Assert.True(j.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out _), $"errors[{alan}] yok: {sonuc.Govde}");
        return sonuc;
    }

    // =================================================================== HIGH-1 TRY + açık kur ≠ 1

    [Fact]
    public async Task HIGH1_TRY_islemde_acik_kur_1_degilse_400_kur_hicbir_sey_yazilmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var cari2 = await OkuAsync(o, sp => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Irat", Soyad = "Kur" }));
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = cari2, tutar = 100m, hesap = "Kasa" }, YeniAnahtar()));
        var once = await DefterSayisiAsync(o);

        // Kira 300 TRY; müşteri 100 TRY ödüyor, istemci önceki USD seçiminden kalma kur=5 gönderiyor.
        await Problem400(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 100m, hesap = "Kasa", doviz = "TRY", kur = 5m }, YeniAnahtar()), "kur");
        await Problem400(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 100m, hesap = "Kasa", kur = 5m }, YeniAnahtar()), "kur");
        await Problem400(await PostAsync(s, "/finans/dis-hizmet",
            new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m, kur = 5m }, YeniAnahtar()), "kur");
        await Problem400(await PostAsync(s, "/finans/odeme", new { cariId = o.Tedarikci, tutar = 10m, hesap = "Kasa", kur = 5m }, YeniAnahtar()), "kur");
        await Problem400(await PostAsync(s, "/finans/depozito/irat", new { cariId = cari2, tutar = 20m, kur = 5m }, YeniAnahtar()), "kur");
        await Problem400(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, tutar = 1m, hesap = "Kasa", doviz = "TL", kur = 0.5m }, YeniAnahtar()), "kur");

        Assert.Equal(once, await DefterSayisiAsync(o));
        var kira = await KiraOkuAsync(o, o.Kira);
        Assert.Equal(0m, kira.Tahsilat);
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Tedarikci));
        Assert.Equal(0m, await DepozitoBakiyeAsync(o, o.Musteri));
        Assert.Equal(100m, await DepozitoBakiyeAsync(o, cari2));

        // Açık kur = 1 (TRY) meşrudur: 100 TRY → kira Tahsilat 100, cari −100.
        var id = await Id(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 100m, hesap = "Kasa", doviz = "TRY", kur = 1m }, YeniAnahtar()));
        Assert.All(await DefterAsync(o, id), e => Assert.Equal(1m, e.Amount.Rate));
        Assert.Equal(100m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(-100m, await CariBakiyeAsync(o, o.Musteri));
        Assert.Empty(await DengesizKumelerAsync(o));
    }

    [Fact]
    public async Task HIGH1_servis_duzeyinde_de_kapali_Blazor_yolu()
    {
        var o = await OrtamKurAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => OkuAsync(o, sp => sp.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = o.Musteri, Tutar = 100m, Doviz = "TRY", Kur = 5m, Hesap = LedgerAccountType.Kasa })));
        Assert.Equal("kur", ex.Alan);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => OkuAsync(o, sp => sp.GetRequiredService<DepositService>()
            .GetAsync(o.Musteri, 100m, LedgerAccountType.Kasa, currency: null, exchangeRate: 5m)));
        Assert.Equal("kur", ex2.Alan);
        Assert.Equal(0, await DefterSayisiAsync(o));
    }

    // =================================================================== MEDIUM-1 büyüklük/uzunluk → 400 (500 değil)

    [Fact]
    public async Task MEDIUM1_asiri_buyuk_ya_da_uzun_girdi_400_alanli_500_yok()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var uzun = new string('x', 600);

        var denemeler = new (string Yol, object Govde, bool Anahtar, string Alan)[]
        {
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 10000000000000000m, hesap = "Kasa" }, true, "tutar"),
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", doviz = "USD", kur = 100000000000000m }, true, "kur"),
            ("/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 1000000000000m, hesap = "Kasa", doviz = "USD", kur = 1000000m }, true, "tutar"),
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 1m, hesap = "Kasa", aciklama = uzun }, true, "aciklama"),
            ("/finans/odeme", new { cariId = o.Musteri, tutar = 10000000000000000m, hesap = "Kasa" }, true, "tutar"),
            ("/finans/depozito/al", new { cariId = o.Musteri, tutar = 10000000000000000m, hesap = "Kasa" }, true, "tutar"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10000000000000000m }, true, "hizmetBedeli"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10m, komisyonFaturaNo = new string('9', 100) }, true, "komisyonFaturaNo"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = new string('v', 300), hizmetBedeli = 10m }, true, "alinanHizmet"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10m, hizmetAlinanFirma = new string('f', 300) }, true, "hizmetAlinanFirma"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10m, aciklama = uzun }, true, "aciklama"),
            ("/finans/fatura", new { kiraId = o.Kira, otv = 10000000000000000m }, false, "otv"),
            ("/finans/fatura", new { kiraId = o.Kira, tevkifatTutar = 10000000000000000m }, false, "tevkifatTutar"),
            ("/finans/fatura", new { kiraId = o.Kira, damgaVergisi = 10000000000000000m }, false, "damgaVergisi"),
            ("/finans/depozito/irat", new { cariId = o.Musteri, tutar = 5m, aciklama = uzun }, true, "aciklama"),
        };
        foreach (var (yol, govde, anahtar, alan) in denemeler)
            await Problem400(await PostAsync(s, yol, govde, anahtar ? YeniAnahtar() : null), alan);

        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DefterSayisiAsync(o));
    }

    [Fact]
    public async Task MEDIUM1_uc_sinirini_gecip_kolonu_tasiran_turetilmis_deger_400_veri_tasmasi()
    {
        // Otomatik kurla (uç çarpımı bilemez) 9e14 USD × 40 = 3,6e16 baz → kira Tahsilat numeric(19,4) taşar (22003).
        var o = await OrtamKurAsync(sp => sp.GetRequiredService<RentACar.Application.Kur.FixedExchangeRateService>()
            .UpsertAsync(new RentACar.Application.Kur.SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true }));
        var s = await GirisAsync(o, Kim.Muhasebe);

        var (st, kod, g) = await Oku(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 900000000000000m, hesap = "Kasa", doviz = "USD" }, YeniAnahtar()));
        Assert.True(st == 400 && kod == "dogrulama", $"{st}: {g}");
        // F8.1a adversarial M1: baz sınırı artık ÇÖZÜLEN kura da uçta uygulanır — 22003 ağına ulaşmadan alanlı 400
        // (errors.tutar). Sözleşme aynı: 400 dogrulama, iç ayrıntı sızmaz, hiçbir şey yazılmaz.
        var detay = JsonDocument.Parse(g).RootElement.GetProperty("detail").GetString()!;
        Assert.Equal("Tutar × kur çok büyük.", detay);
        Assert.DoesNotContain("Rentals", g, StringComparison.Ordinal); // iç ayrıntı sızmaz
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync())); // işlem geri alındı
        Assert.Equal(0m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
    }

    [Theory]
    [InlineData("22001", true)]
    [InlineData("22003", true)]
    [InlineData("23505", false)] // benzersizlik ihlali ağın kapsamında DEĞİL (hata gizlenmez)
    [InlineData("40001", false)]
    public void MEDIUM1_veri_tasmasi_agi_yalniz_22001_ve_22003(string sqlState, bool beklenen)
    {
        var pg = new Npgsql.PostgresException("iç ayrıntı", "ERROR", "ERROR", sqlState);
        var sarili = new DbUpdateException("kayıt", pg);
        Assert.Equal(beklenen, RentACar.Web.Api.UiHata.VeriTasmasi(sarili));
        var esleme = RentACar.Web.Api.UiHata.Esle(sarili);
        Assert.Equal(beklenen, esleme is not null);
        if (esleme is { } e)
        {
            Assert.Equal(400, e.Status);
            Assert.Equal("dogrulama", e.Kod);
        }
    }

    // =================================================================== L2 4 ondalıkta sıfır

    [Fact]
    public async Task L2_dort_ondalikta_sifir_kalan_tutar_400_belge_no_tuketmez()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        await Problem400(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 0.00004m, hesap = "Kasa" }, YeniAnahtar()), "tutar");
        await Problem400(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 0.00004m, hesap = "Kasa" }, YeniAnahtar()), "tutar");
        await Problem400(await PostAsync(s, "/finans/dis-hizmet",
            new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "V", hizmetBedeli = 0.00004m }, YeniAnahtar()), "hizmetBedeli");
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DefterSayisiAsync(o));
        // Sınırın hemen üstü (0,0001) yazılır.
        await Id(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 0.0001m, hesap = "Kasa" }, YeniAnahtar()));
    }

    // =================================================================== MEDIUM-2 hayalet / yabancı cari

    [Fact]
    public async Task MEDIUM2_var_olmayan_ya_da_baska_kiracinin_carisine_para_yazilmaz()
    {
        var o = await OrtamKurAsync();
        var o2 = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var hayalet = Guid.NewGuid();

        foreach (var cari in new[] { hayalet, o2.Musteri })
        foreach (var (yol, govde) in new (string, object)[]
                 {
                     ("/finans/tahsilat", new { cariId = cari, tutar = 10m, hesap = "Kasa" }),
                     ("/finans/odeme", new { cariId = cari, tutar = 10m, hesap = "Kasa" }),
                     ("/finans/depozito/al", new { cariId = cari, tutar = 10m, hesap = "Kasa" }),
                     ("/finans/depozito/irat", new { cariId = cari, tutar = 10m }),
                 })
            await Problem400(await PostAsync(s, yol, govde, YeniAnahtar()), "cariId");

        Assert.Equal(0m, await CariBakiyeAsync(o2, o2.Musteri));
        Assert.Equal(0, await DefterSayisiAsync(o));
        Assert.Equal(0, await DefterSayisiAsync(o2));
    }

    // =================================================================== MEDIUM-3 tahsilatAnahtar kiraya bağlı

    [Fact]
    public async Task MEDIUM3_baska_subenin_operatoru_donem_tahsilatini_tahsilatAnahtar_ile_bastiramaz()
    {
        var o = await OrtamKurAsync();
        var (kiraA, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), gun: 90));
        var m2 = await OkuAsync(o, sp => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "SubeB", Soyad = "Musteri" }));
        var (kiraB, _) = await OkuAsync(o, sp => KiraAsync(sp, m2, KiraBas, gun: 2, ofis: "SubeB"));

        var b = await GirisAsync(o, Kim.OperatorB);
        // B kiraA'yı göremez; kendi kirasına kiraA'nın dönem-1 RowKey'ini "tahsilat anahtarı" diye verir → 409.
        var (st, kod, g) = await Oku(await PostAsync(b, "/finans/tahsilat",
            new { cariId = m2, kiraId = kiraB, tutar = 1m, hesap = "Kasa", tahsilatAnahtar = CashService.RowKey(kiraA, 1) }, null));
        Assert.True(st == 409 && kod == "mukerrer", $"{st}: {g}");
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));

        var m = await GirisAsync(o, Kim.Muhasebe);
        var y = await Oku(await PostAsync(m, "/finans/donem-fatura", new { kiraId = kiraA, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null));
        Assert.Equal(200, y.Status);
        Assert.True(JsonDocument.Parse(y.Govde).RootElement.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(3100m, (await KiraOkuAsync(o, kiraA)).Tahsilat); // 31/90 × 9000
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));
    }

    [Fact]
    public async Task MEDIUM3_tahsilatAnahtar_sunucuda_yeniden_hesaplanir_baska_kiranin_ve_uydurma_anahtar_409()
    {
        var o = await OrtamKurAsync();
        var (kira2, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, KiraBas.AddDays(10), gun: 2));
        var s = await GirisAsync(o, Kim.Muhasebe);
        var k1 = RentACar.Web.Finance.TahsilatAnahtar.Uret(o.Kira, 300m, 0); // kira1 panelinin anahtarı

        var (st, kod, _) = await Oku(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = kira2, tutar = 200m, hesap = "Kasa", tahsilatAnahtar = k1 }, null));
        Assert.True(st == 409 && kod == "mukerrer", $"başka kiranın anahtarı: {st}");
        var uydurma = RentACar.Web.Finance.TahsilatAnahtar.Uret(kira2, 12345m, 99);
        var (st3, kod3, _) = await Oku(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = kira2, tutar = 1m, hesap = "Kasa", tahsilatAnahtar = uydurma }, null));
        Assert.True(st3 == 409 && kod3 == "mukerrer", $"uydurma anahtar: {st3}");
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));

        // Meşru: kira1 kendi anahtarıyla yazılır (DB ölçeği "300.0000" ile de, sade "300" ile de aynı kira durumu).
        await Id(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 300m, hesap = "Kasa", tahsilatAnahtar = k1 }, null));
        // Sonraki meşru tahsilat YENİ durumun anahtarıyla (bakiye 0, işlem sayısı 1) — ikinci tahsilat engellenmez.
        var k2 = RentACar.Web.Finance.TahsilatAnahtar.Uret(o.Kira, 0m, 1);
        await Id(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 50m, hesap = "Kasa", tahsilatAnahtar = k2 }, null));
        // Bayat k1 tekrar gelirse → 409.
        var (st4, kod4, _) = await Oku(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 300m, hesap = "Kasa", tahsilatAnahtar = k1 }, null));
        Assert.True(st4 == 409 && kod4 == "mukerrer", $"bayat anahtar: {st4}");
        Assert.Equal(350m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
    }

    [Fact]
    public async Task MEDIUM3_donem_tahsilat_anahtari_baska_kayitta_kullanildiysa_sessiz_bastirma_yok_gurultulu_hata()
    {
        // Blazor'un ham islemAnahtari yolu hâlâ istemci Guid'i alır: RowKey(kiraA, 1) başka bir tahsilatta
        // önceden kullanılmışsa dönem ucu bunu "daha önce alınmış" SAYMAMALI.
        var o = await OrtamKurAsync();
        var (kiraA, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), gun: 90));
        await OkuAsync(o, sp => sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        {
            CariId = o.Musteri, RentalId = o.Kira, Tutar = 1m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = CashService.RowKey(kiraA, 1),
        }));

        var s = await GirisAsync(o, Kim.Muhasebe);
        var (st, kod, g) = await Oku(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kiraA, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null));
        Assert.True(st == 400 && kod == "dogrulama", $"{st}: {g}");
        Assert.Contains("tahsilat yazılamadı", g);
        Assert.Equal(0m, (await KiraOkuAsync(o, kiraA)).Tahsilat);

        // Meşru tekrar (aynı kiranın kendi dönem tahsilatı) hâlâ sessiz no-op.
        var (kiraC, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 6, 1, 10, 0, 0, TimeSpan.Zero), gun: 60));
        var ilk = await Oku(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kiraC, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null));
        var tekrar = await Oku(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kiraC, donemSira = 1, tahsilat = true, hesap = "Banka" }, null));
        Assert.Equal(200, ilk.Status);
        Assert.Equal(200, tekrar.Status);
        Assert.False(JsonDocument.Parse(tekrar.Govde).RootElement.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(1, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == kiraC)));
    }

    // =================================================================== L1 depozitoda hesap uyumu

    [Fact]
    public async Task L1_pasif_hesap_ve_hesap_dovizi_depozitoda_da_denetlenir()
    {
        Guid pasif = Guid.Empty, tryBanka = Guid.Empty;
        var o = await OrtamKurAsync(async sp =>
        {
            var h = sp.GetRequiredService<FinancialAccountService>();
            pasif = await h.CreateAsync(new FinancialAccountInput { Kod = "ESK", Ad = "Eski Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = false });
            tryBanka = await h.CreateAsync(new FinancialAccountInput { Kod = "TRB", Ad = "TL Banka", Tur = "Banka", Doviz = "TRY", Aktif = true });
        });
        var s = await GirisAsync(o, Kim.Muhasebe);

        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = pasif }, YeniAnahtar()))).Status);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = pasif }, YeniAnahtar()))).Status);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Banka", hesapId = tryBanka, doviz = "USD", kur = 30m }, YeniAnahtar()))).Status);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Banka", hesapId = tryBanka, doviz = "USD", kur = 30m }, YeniAnahtar()))).Status);
        Assert.Equal(0, await DefterSayisiAsync(o));
        // TRY depozito TRY banka hesabına yazılır.
        var id = await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Banka", hesapId = tryBanka }, YeniAnahtar()));
        Assert.Contains(await DefterAsync(o, id), e => e.AccountType == LedgerAccountType.Banka && e.AccountRef == tryBanka);
    }

    // =================================================================== L5 iptalde kapsam önce

    [Fact]
    public async Task L5_FinanceReverse_sahibi_baska_sube_operatoru_iptal_edemez_iptal_edilmis_durum_sizmaz()
    {
        var o = await OrtamKurAsync();
        var m = await GirisAsync(o, Kim.Muhasebe);
        var id = await Id(await PostAsync(m, "/finans/dis-hizmet",
            new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m }, YeniAnahtar()));
        var b = await GirisAsync(o, Kim.OperatorBTers);
        var r = await Oku(await PostAsync(b, $"/finans/dis-hizmet/{id}/iptal", null, null));
        Assert.Equal((403, "yetki_yok"), (r.Status, r.Kod));
        var iptal = await PostAsync(m, $"/finans/dis-hizmet/{id}/iptal", null, null);
        Assert.Equal(HttpStatusCode.NoContent, iptal.StatusCode);
        var r2 = await Oku(await PostAsync(b, $"/finans/dis-hizmet/{id}/iptal", null, null));
        Assert.True(r2.Status == 403 && r2.Kod == "yetki_yok", $"iptal edilmiş başka-şube kaydında durum sızdı: {r2.Status} {r2.Govde}");
    }

    // =================================================================== gerileme kilitleri (yeşil probe'lar)

    [Fact]
    public async Task Capraz_kiraci_kira_hesap_ve_dis_hizmet_kimlikleri_kabul_edilmez_500_yok()
    {
        Guid hesap2 = Guid.Empty;
        var o = await OrtamKurAsync();
        var o2 = await OrtamKurAsync(async sp => hesap2 = await sp.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "K2", Ad = "Yabancı Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = true }));
        var s = await GirisAsync(o, Kim.Muhasebe);
        var s2 = await GirisAsync(o2, Kim.Muhasebe);
        var dh2 = await Id(await PostAsync(s2, "/finans/dis-hizmet",
            new { kiraId = o2.Kira, cariId = o2.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m }, YeniAnahtar()));

        var denemeler = new (string Yol, object? Govde, bool Anahtar)[]
        {
            ("/finans/tahsilat", new { cariId = o.Musteri, kiraId = o2.Kira, tutar = 10m, hesap = "Kasa" }, true),
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = hesap2 }, true),
            ("/finans/fatura", new { kiraId = o2.Kira }, false),
            ("/finans/donem-fatura", new { kiraId = o2.Kira, donemSira = 1 }, false),
            ("/finans/dis-hizmet", new { kiraId = o2.Kira, cariId = o.Tedarikci, alinanHizmet = "V", hizmetBedeli = 10m }, true),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o2.Tedarikci, alinanHizmet = "V", hizmetBedeli = 10m }, true),
            ($"/finans/dis-hizmet/{dh2}/iptal", null, false),
            ("/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = hesap2 }, true),
            ("/finans/depozito/irat", new { cariId = o.Musteri, tutar = 1m, kiraId = o2.Kira }, true),
        };
        foreach (var (yol, govde, anahtar) in denemeler)
        {
            var (st, _, g) = await Oku(await PostAsync(s, yol, govde, anahtar ? YeniAnahtar() : null));
            Assert.True(st is >= 400 and < 500, $"{yol} → {st}: {g}");
        }
        Assert.Equal(DisHizmetDurum.Kayitli, await DbAsync(o2, db => db.DisHizmetAlimlari.AsNoTracking().Where(d => d.Id == dh2).Select(d => d.Durum).SingleAsync()));
        Assert.Equal(0, await DbAsync(o2, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DefterSayisiAsync(o));
    }

    [Fact]
    public async Task Eszamanli_fatura_tek_fatura_500_yok()
    {
        var o = await OrtamKurAsync();
        var oturumlar = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GirisAsync(o, Kim.Muhasebe)));
        var sonuc = await Task.WhenAll((await Task.WhenAll(oturumlar.Select(s => PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, null)))).Select(Oku));
        Assert.DoesNotContain(sonuc, x => x.Status >= 500);
        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(300m, await CariBakiyeAsync(o, o.Musteri));
        Assert.Empty(await DengesizKumelerAsync(o));
    }

    [Fact]
    public async Task Eszamanli_donem_kes_ve_tahsil_tek_fatura_tek_tahsilat()
    {
        var o = await OrtamKurAsync();
        var (kira, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), gun: 90));
        var oturumlar = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GirisAsync(o, Kim.Muhasebe)));
        var govde = new { kiraId = kira, donemSira = 1, tahsilat = true, hesap = "Kasa" };
        var sonuc = await Task.WhenAll((await Task.WhenAll(oturumlar.Select(s => PostAsync(s, "/finans/donem-fatura", govde, null)))).Select(Oku));
        Assert.DoesNotContain(sonuc, x => x.Status >= 500);
        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync(i => i.RentalId == kira || i.KaynakKiraId == kira)));
        Assert.Equal(1, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == kira)));
        Assert.Equal(3100m, (await KiraOkuAsync(o, kira)).Tahsilat);
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));
        Assert.Empty(await DengesizKumelerAsync(o));
    }

    [Fact]
    public async Task Eszamanli_depozito_al_ayni_anahtar_ayni_icerik_tek_kume_hepsi_ayni_id()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var anahtar = YeniAnahtar();
        var govde = new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" };
        var sonuc = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(s, "/finans/depozito/al", govde, anahtar)))).Select(Oku));
        Assert.All(sonuc, x => Assert.Equal(200, x.Status));
        Assert.Single(sonuc.Select(x => x.Govde).Distinct());
        Assert.Equal(500m, await DepozitoBakiyeAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Eszamanli_irat_tutulani_asamaz_ve_ayni_anahtar_tek_kayit()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" }, YeniAnahtar()));

        var anahtar = YeniAnahtar();
        var ayni = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 100m, kiraId = o.Kira }, anahtar)))).Select(Oku));
        Assert.DoesNotContain(ayni, x => x.Status >= 500);
        Assert.Equal(400m, await DepozitoBakiyeAsync(o, o.Musteri));

        var farkli = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 150m }, YeniAnahtar())))).Select(Oku));
        Assert.DoesNotContain(farkli, x => x.Status >= 500);
        Assert.Equal(2, farkli.Count(x => x.Status == 200)); // 400 tutuluyor → 2 × 150
        Assert.Equal(100m, await DepozitoBakiyeAsync(o, o.Musteri));
        Assert.Empty(await DengesizKumelerAsync(o));
    }

    [Fact]
    public async Task Eszamanli_dis_hizmet_ayni_anahtar_tek_kayit_iptal_tek_ters_kayit()
    {
        var o = await OrtamKurAsync();
        var oturumlar = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => GirisAsync(o, Kim.Muhasebe)));
        var anahtar = YeniAnahtar();
        var govde = new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Çekici", hizmetBedeli = 1000m, komisyonOran = 10m };
        var olustur = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(oturumlar[0], "/finans/dis-hizmet", govde, anahtar)))).Select(Oku));
        Assert.DoesNotContain(olustur, x => x.Status >= 500);
        Assert.Equal(1, olustur.Count(x => x.Status == 200));
        var id = JsonDocument.Parse(olustur.Single(x => x.Status == 200).Govde).RootElement.GetProperty("id").GetGuid();

        var iptal = await Task.WhenAll((await Task.WhenAll(oturumlar.Select(s => PostAsync(s, $"/finans/dis-hizmet/{id}/iptal", null, null)))).Select(Oku));
        Assert.DoesNotContain(iptal, x => x.Status >= 500);
        Assert.Equal(1, iptal.Count(x => x.Status == 204));
        Assert.Equal(8, (await DefterAsync(o, id)).Count);
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Tedarikci));
        Assert.Empty(await DengesizKumelerAsync(o));
    }

    [Fact]
    public async Task Csrfsiz_ve_yanlis_belirtecli_istek_reddedilir_yazmaz()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        var govde = new { cariId = o.Musteri, kiraId = o.Kira, tutar = 10m, hesap = "Kasa" };
        var sonuc = new[]
        {
            await Oku(await PostAsync(s, "/finans/tahsilat", govde, YeniAnahtar(), xsrf: null)),
            await Oku(await PostAsync(s, "/finans/tahsilat", govde, YeniAnahtar(), xsrf: "yanlis-belirtec")),
            await Oku(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa" }, YeniAnahtar(), xsrf: null)),
            await Oku(await PostAsync(s, $"/finans/dis-hizmet/{Guid.NewGuid()}/iptal", null, null, xsrf: null)),
        };
        Assert.All(sonuc, x => Assert.Equal((400, "xsrf_gecersiz"), (x.Status, x.Kod)));
        Assert.Equal(0, await DefterSayisiAsync(o));
    }

    [Fact]
    public async Task USD_kira_TRY_ya_da_EUR_tahsilat_400_USD_tahsilat_ham_tutar()
    {
        var o = await OrtamKurAsync(sp => sp.GetRequiredService<RentACar.Application.Kur.FixedExchangeRateService>()
            .UpsertAsync(new RentACar.Application.Kur.SabitKurInput { Kod = "USD", Kur = 30m, Aktif = true }));
        var (kiraUsd, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, KiraBas.AddDays(20), gun: 3, doviz: "USD"));
        var s = await GirisAsync(o, Kim.Muhasebe);

        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = kiraUsd, tutar = 100m, hesap = "Kasa" }, YeniAnahtar()))).Status);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = kiraUsd, tutar = 100m, hesap = "Kasa", doviz = "EUR", kur = 35m }, YeniAnahtar()))).Status);
        Assert.Equal(0, await DefterSayisiAsync(o));

        await Id(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = kiraUsd, tutar = 10m, hesap = "Kasa", doviz = "USD", kur = 30m }, YeniAnahtar()));
        Assert.Equal(10m, (await KiraOkuAsync(o, kiraUsd)).Tahsilat);  // USD kira: ham tutar
        Assert.Equal(-300m, await CariBakiyeAsync(o, o.Musteri));      // baz: 10 × 30
    }

    [Fact]
    public async Task Isaret_ve_bakiyeler_elle_oracle()
    {
        var o = await OrtamKurAsync();
        var s = await GirisAsync(o, Kim.Muhasebe);
        // Fatura 300 (+300) → tahsilat 500 banka (−500, fazla) → iade ödemesi 200 kasa (+200) → depozito al 50 kasa
        // (cari bakiyesini ETKİLEMEZ) → dış hizmet tedarikçi 1000 %10 (tedarikçi −900).
        await Id(await PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, null));
        await Id(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 500m, hesap = "Banka" }, YeniAnahtar()));
        await Id(await PostAsync(s, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 200m, hesap = "Kasa" }, YeniAnahtar()));
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 50m, hesap = "Kasa" }, YeniAnahtar()));
        await Id(await PostAsync(s, "/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 1000m, komisyonOran = 10m }, YeniAnahtar()));

        var k = await KiraOkuAsync(o, o.Kira);
        var kasa = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Kasa)
            .Select(e => new { e.Direction, e.Amount.Amount, e.Amount.Rate }).ToListAsync());
        var banka = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Banka)
            .Select(e => new { e.Direction, e.Amount.Amount, e.Amount.Rate }).ToListAsync());
        Assert.Equal(300m, k.Tahsilat);   // 500 − 200
        Assert.Equal(0m, k.Bakiye);
        Assert.Equal(0m, await CariBakiyeAsync(o, o.Musteri));   // +300 −500 +200
        Assert.Equal(-900m, await CariBakiyeAsync(o, o.Tedarikci)); // −1000 +100
        Assert.Equal(-150m, kasa.Sum(x => (x.Direction == LedgerDirection.Debit ? 1 : -1) * x.Amount * x.Rate)); // −200 +50
        Assert.Equal(500m, banka.Sum(x => (x.Direction == LedgerDirection.Debit ? 1 : -1) * x.Amount * x.Rate));
        Assert.Empty(await DengesizKumelerAsync(o));
    }

    [Fact]
    public async Task Iptal_kiraya_fatura_donem_ve_dis_hizmet_400()
    {
        var o = await OrtamKurAsync();
        var (kira, _) = await OkuAsync(o, sp => KiraAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), gun: 90));
        await OkuAsync(o, async sp => { await sp.GetRequiredService<RentalService>().CancelAsync(kira); return 0; });
        var s = await GirisAsync(o, Kim.Muhasebe);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/fatura", new { kiraId = kira }, null))).Status);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/donem-fatura", new { kiraId = kira, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null))).Status);
        Assert.Equal(400, (await Oku(await PostAsync(s, "/finans/dis-hizmet", new { kiraId = kira, cariId = o.Tedarikci, alinanHizmet = "V", hizmetBedeli = 10m }, YeniAnahtar()))).Status);
        Assert.Equal(0, await DefterSayisiAsync(o));
    }

    [Fact]
    public async Task Deterministik_anahtar_uc_kullanici_eszamanli_tek_tahsilat()
    {
        var o = await OrtamKurAsync();
        var oturumlar = await Task.WhenAll(new[] { Kim.Muhasebe, Kim.Admin, Kim.OperatorA, Kim.Muhasebe, Kim.Admin, Kim.OperatorA }.Select(k => GirisAsync(o, k)));
        var k = RentACar.Web.Finance.TahsilatAnahtar.Uret(o.Kira, 300m, 0);
        var sonuc = await Task.WhenAll((await Task.WhenAll(oturumlar.Select(s => PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 300m, hesap = "Kasa", tahsilatAnahtar = k }, YeniAnahtar())))).Select(Oku));
        Assert.DoesNotContain(sonuc, x => x.Status >= 500);
        Assert.Equal(1, sonuc.Count(x => x.Status == 200));
        Assert.All(sonuc.Where(x => x.Status != 200), x => Assert.Equal("mukerrer", x.Kod));
        Assert.Equal(300m, (await KiraOkuAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(-300m, await CariBakiyeAsync(o, o.Musteri));
    }
}
