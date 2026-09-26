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
    private static readonly DateTimeOffset RentalStart = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

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

    private static string RandomText(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];
    private static string NewKey() => Guid.NewGuid().ToString("N");

    private async Task<Ortam> SetUpEnvironmentAsync(Func<IServiceProvider, Task>? extra = null)
    {
        var tenantId = Guid.NewGuid();
        var code = RandomText("adv");
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
                    Kim.Muhasebe => (UserRole.Muhasebe, null),
                    Kim.OperatorA => (UserRole.Operator, "SubeA"),
                    _ => (UserRole.Operator, "SubeB"),
                };
                var u = new User { TenantId = tenantId, UserName = name, DisplayName = name, Rol = rol, AtanmisSube = branch, IsActive = true };
                u.PasswordHash = hasher.HashPassword(u, password);
                db.Users.Add(u);
                if (kim is Kim.OperatorA or Kim.OperatorB or Kim.OperatorBTers)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceWrite", Ver = true });
                if (kim is Kim.OperatorBTers)
                    db.KullaniciIzinIstisnalari.Add(new KullaniciIzinIstisna { TenantId = tenantId, UserId = u.Id, Izin = "FinanceReverse", Ver = true });
            }
            await db.SaveChangesAsync();
        }
        await fx.MakePilotAsync(tenantId, true);

        using var host = new TestHost(fx.Pg.AppConnectionString);
        using var s = host.ScopeFor(tenantId, role: UserRole.Admin);
        var sp = s.ServiceProvider;
        if (extra is not null) await extra(sp);
        var customers = sp.GetRequiredService<CustomerService>();
        var customer = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Adv", Soyad = "Musteri" });
        var supplier = await customers.CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Adv", Soyad = "Tedarikci" });
        var (rental, vehicle) = await RentalAsync(sp, customer, RentalStart, day: 3);
        return new Ortam
        {
            TenantId = tenantId, Kod = code, Sifre = password, Kullanicilar = users,
            Musteri = customer, Tedarikci = supplier, Kira = rental, KiraArac = vehicle,
        };
    }

    private static async Task<(Guid Kira, Guid Arac)> RentalAsync(IServiceProvider sp, Guid customer, DateTimeOffset start, int day,
        string office = "SubeA", string? currency = null)
    {
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 AD " + Random.Shared.Next(1000, 9999) });
        var rental = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = customer, VehicleId = vehicle, BasTar = start, BitTar = start.AddDays(day),
            GunlukUcret = 100m, CikisOfisi = office, Doviz = currency,
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

    private Task<int> LedgerCountAsync(Ortam o)
        => DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().CountAsync());

    private Task<RentalContract> ReadRentalAsync(Ortam o, Guid rental)
        => ReadAsync(o, async sp => (await sp.GetRequiredService<RentalService>().GetAsync(rental))!);

    private Task<decimal> AccountBalanceAsync(Ortam o, Guid account)
        => ReadAsync(o, sp => sp.GetRequiredService<CashService>().GetAccountBalanceAsync(account));

    private Task<decimal> DepositBalanceAsync(Ortam o, Guid account)
        => ReadAsync(o, sp => sp.GetRequiredService<DepositService>().GetBalanceAsync(account));

    private async Task<List<string>> UnbalancedSetsAsync(Ortam o)
    {
        var rows = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().ToListAsync());
        var errors = new List<string>();
        foreach (var set in rows.GroupBy(e => (e.SourceType, e.SourceId)))
        {
            var debit = set.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            var credit = set.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount.Amount * e.Amount.Rate);
            if (debit != credit) errors.Add($"Dengesiz küme {set.Key}: {debit} ≠ {credit}");
        }
        return errors;
    }

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

    private static Task<HttpResponseMessage> PostAsync(Oturum s, string path, object? body, string? key, string? xsrf = "__oturum")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + path);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (xsrf is not null) req.Headers.Add("X-XSRF-TOKEN", xsrf == "__oturum" ? s.Xsrf : xsrf);
        if (key is not null) req.Headers.Add("Idempotency-Key", key);
        return s.C.SendAsync(req);
    }

    private static async Task<(int Status, string? Kod, string Govde)> Read(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        string? code = null;
        try
        {
            var j = JsonDocument.Parse(text).RootElement;
            if (j.ValueKind == JsonValueKind.Object && j.TryGetProperty("kod", out var k)) code = k.GetString();
        }
        catch (JsonException) { }
        return ((int)r.StatusCode, code, text);
    }

    private static async Task<Guid> Id(HttpResponseMessage r)
    {
        var (st, _, g) = await Read(r);
        Assert.True(st == 200, $"{st}: {g}");
        return JsonDocument.Parse(g).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(int Status, string? Kod, string Govde)> Problem400(HttpResponseMessage r, string alan)
    {
        var result = await Read(r);
        Assert.True(result.Status == 400 && result.Kod == "dogrulama", $"beklenen 400 dogrulama, gelen {result.Status}: {result.Govde}");
        var j = JsonDocument.Parse(result.Govde).RootElement;
        Assert.True(j.TryGetProperty("errors", out var e) && e.TryGetProperty(alan, out _), $"errors[{alan}] yok: {result.Govde}");
        return result;
    }

    // =================================================================== HIGH-1 TRY + açık kur ≠ 1

    [Fact]
    public async Task HIGH1_TRY_islemde_acik_kur_1_degilse_400_kur_hicbir_sey_yazilmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var account2 = await ReadAsync(o, sp => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Irat", Soyad = "Kur" }));
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = account2, tutar = 100m, hesap = "Kasa" }, NewKey()));
        var once = await LedgerCountAsync(o);

        // Kira 300 TRY; müşteri 100 TRY ödüyor, istemci önceki USD seçiminden kalma kur=5 gönderiyor.
        await Problem400(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 100m, hesap = "Kasa", doviz = "TRY", kur = 5m }, NewKey()), "kur");
        await Problem400(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 100m, hesap = "Kasa", kur = 5m }, NewKey()), "kur");
        await Problem400(await PostAsync(s, "/finans/dis-hizmet",
            new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m, kur = 5m }, NewKey()), "kur");
        await Problem400(await PostAsync(s, "/finans/odeme", new { cariId = o.Tedarikci, tutar = 10m, hesap = "Kasa", kur = 5m }, NewKey()), "kur");
        await Problem400(await PostAsync(s, "/finans/depozito/irat", new { cariId = account2, tutar = 20m, kur = 5m }, NewKey()), "kur");
        await Problem400(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, tutar = 1m, hesap = "Kasa", doviz = "TL", kur = 0.5m }, NewKey()), "kur");

        Assert.Equal(once, await LedgerCountAsync(o));
        var rental = await ReadRentalAsync(o, o.Kira);
        Assert.Equal(0m, rental.Tahsilat);
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Tedarikci));
        Assert.Equal(0m, await DepositBalanceAsync(o, o.Musteri));
        Assert.Equal(100m, await DepositBalanceAsync(o, account2));

        // Açık kur = 1 (TRY) meşrudur: 100 TRY → kira Tahsilat 100, cari −100.
        var id = await Id(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 100m, hesap = "Kasa", doviz = "TRY", kur = 1m }, NewKey()));
        Assert.All(await LedgerAsync(o, id), e => Assert.Equal(1m, e.Amount.Rate));
        Assert.Equal(100m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(-100m, await AccountBalanceAsync(o, o.Musteri));
        Assert.Empty(await UnbalancedSetsAsync(o));
    }

    [Fact]
    public async Task HIGH1_servis_duzeyinde_de_kapali_Blazor_yolu()
    {
        var o = await SetUpEnvironmentAsync();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => ReadAsync(o, sp => sp.GetRequiredService<CashService>()
            .CollectAsync(new CashInput { CariId = o.Musteri, Tutar = 100m, Doviz = "TRY", Kur = 5m, Hesap = LedgerAccountType.Kasa })));
        Assert.Equal("kur", ex.Alan);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => ReadAsync(o, sp => sp.GetRequiredService<DepositService>()
            .GetAsync(o.Musteri, 100m, LedgerAccountType.Kasa, currency: null, exchangeRate: 5m)));
        Assert.Equal("kur", ex2.Alan);
        Assert.Equal(0, await LedgerCountAsync(o));
    }

    // =================================================================== MEDIUM-1 büyüklük/uzunluk → 400 (500 değil)

    [Fact]
    public async Task MEDIUM1_asiri_buyuk_ya_da_uzun_girdi_400_alanli_500_yok()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var longText = new string('x', 600);

        var attempts = new (string Yol, object Govde, bool Anahtar, string Alan)[]
        {
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 10000000000000000m, hesap = "Kasa" }, true, "tutar"),
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", doviz = "USD", kur = 100000000000000m }, true, "kur"),
            ("/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 1000000000000m, hesap = "Kasa", doviz = "USD", kur = 1000000m }, true, "tutar"),
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 1m, hesap = "Kasa", aciklama = longText }, true, "aciklama"),
            ("/finans/odeme", new { cariId = o.Musteri, tutar = 10000000000000000m, hesap = "Kasa" }, true, "tutar"),
            ("/finans/depozito/al", new { cariId = o.Musteri, tutar = 10000000000000000m, hesap = "Kasa" }, true, "tutar"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10000000000000000m }, true, "hizmetBedeli"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10m, komisyonFaturaNo = new string('9', 100) }, true, "komisyonFaturaNo"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = new string('v', 300), hizmetBedeli = 10m }, true, "alinanHizmet"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10m, hizmetAlinanFirma = new string('f', 300) }, true, "hizmetAlinanFirma"),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 10m, aciklama = longText }, true, "aciklama"),
            ("/finans/fatura", new { kiraId = o.Kira, otv = 10000000000000000m }, false, "otv"),
            ("/finans/fatura", new { kiraId = o.Kira, tevkifatTutar = 10000000000000000m }, false, "tevkifatTutar"),
            ("/finans/fatura", new { kiraId = o.Kira, damgaVergisi = 10000000000000000m }, false, "damgaVergisi"),
            ("/finans/depozito/irat", new { cariId = o.Musteri, tutar = 5m, aciklama = longText }, true, "aciklama"),
        };
        foreach (var (path, body, key, alan) in attempts)
            await Problem400(await PostAsync(s, path, body, key ? NewKey() : null), alan);

        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.DisHizmetAlimlari.AsNoTracking().CountAsync()));
        Assert.Equal(0, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(0, await LedgerCountAsync(o));
    }

    [Fact]
    public async Task MEDIUM1_uc_sinirini_gecip_kolonu_tasiran_turetilmis_deger_400_veri_tasmasi()
    {
        // Otomatik kurla (uç çarpımı bilemez) 9e14 USD × 40 = 3,6e16 baz → kira Tahsilat numeric(19,4) taşar (22003).
        var o = await SetUpEnvironmentAsync(sp => sp.GetRequiredService<RentACar.Application.Kur.FixedExchangeRateService>()
            .UpsertAsync(new RentACar.Application.Kur.SabitKurInput { Kod = "USD", Kur = 40m, Aktif = true }));
        var s = await LoginAsync(o, Kim.Muhasebe);

        var (st, code, g) = await Read(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 900000000000000m, hesap = "Kasa", doviz = "USD" }, NewKey()));
        Assert.True(st == 400 && code == "dogrulama", $"{st}: {g}");
        // F8.1a adversarial M1: baz sınırı artık ÇÖZÜLEN kura da uçta uygulanır — 22003 ağına ulaşmadan alanlı 400
        // (errors.tutar). Sözleşme aynı: 400 dogrulama, iç ayrıntı sızmaz, hiçbir şey yazılmaz.
        var detail = JsonDocument.Parse(g).RootElement.GetProperty("detail").GetString()!;
        Assert.Equal("Tutar × kur çok büyük.", detail);
        Assert.DoesNotContain("Rentals", g, StringComparison.Ordinal); // iç ayrıntı sızmaz
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync())); // işlem geri alındı
        Assert.Equal(0m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
    }

    [Theory]
    [InlineData("22001", true)]
    [InlineData("22003", true)]
    [InlineData("23505", false)] // benzersizlik ihlali ağın kapsamında DEĞİL (hata gizlenmez)
    [InlineData("40001", false)]
    public void MEDIUM1_veri_tasmasi_agi_yalniz_22001_ve_22003(string sqlState, bool expected)
    {
        var pg = new Npgsql.PostgresException("iç ayrıntı", "ERROR", "ERROR", sqlState);
        var wrapped = new DbUpdateException("kayıt", pg);
        Assert.Equal(expected, RentACar.Web.Api.UiError.DataOverflow(wrapped));
        var mapping = RentACar.Web.Api.UiError.Map(wrapped);
        Assert.Equal(expected, mapping is not null);
        if (mapping is { } e)
        {
            Assert.Equal(400, e.Status);
            Assert.Equal("dogrulama", e.Kod);
        }
    }

    // =================================================================== L2 4 ondalıkta sıfır

    [Fact]
    public async Task L2_dort_ondalikta_sifir_kalan_tutar_400_belge_no_tuketmez()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        await Problem400(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 0.00004m, hesap = "Kasa" }, NewKey()), "tutar");
        await Problem400(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 0.00004m, hesap = "Kasa" }, NewKey()), "tutar");
        await Problem400(await PostAsync(s, "/finans/dis-hizmet",
            new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "V", hizmetBedeli = 0.00004m }, NewKey()), "hizmetBedeli");
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));
        Assert.Equal(0, await LedgerCountAsync(o));
        // Sınırın hemen üstü (0,0001) yazılır.
        await Id(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 0.0001m, hesap = "Kasa" }, NewKey()));
    }

    // =================================================================== MEDIUM-2 hayalet / yabancı cari

    [Fact]
    public async Task MEDIUM2_var_olmayan_ya_da_baska_kiracinin_carisine_para_yazilmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var o2 = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var ghost = Guid.NewGuid();

        foreach (var account in new[] { ghost, o2.Musteri })
        foreach (var (path, body) in new (string, object)[]
                 {
                     ("/finans/tahsilat", new { cariId = account, tutar = 10m, hesap = "Kasa" }),
                     ("/finans/odeme", new { cariId = account, tutar = 10m, hesap = "Kasa" }),
                     ("/finans/depozito/al", new { cariId = account, tutar = 10m, hesap = "Kasa" }),
                     ("/finans/depozito/irat", new { cariId = account, tutar = 10m }),
                 })
            await Problem400(await PostAsync(s, path, body, NewKey()), "cariId");

        Assert.Equal(0m, await AccountBalanceAsync(o2, o2.Musteri));
        Assert.Equal(0, await LedgerCountAsync(o));
        Assert.Equal(0, await LedgerCountAsync(o2));
    }

    // =================================================================== MEDIUM-3 tahsilatAnahtar kiraya bağlı

    [Fact]
    public async Task MEDIUM3_baska_subenin_operatoru_donem_tahsilatini_tahsilatAnahtar_ile_bastiramaz()
    {
        var o = await SetUpEnvironmentAsync();
        var (rentalA, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), day: 90));
        var m2 = await ReadAsync(o, sp => sp.GetRequiredService<CustomerService>().CreateAsync(new CustomerInput { Tip = CustomerType.Bireysel, Ad = "SubeB", Soyad = "Musteri" }));
        var (rentalB, _) = await ReadAsync(o, sp => RentalAsync(sp, m2, RentalStart, day: 2, office: "SubeB"));

        var b = await LoginAsync(o, Kim.OperatorB);
        // B kiraA'yı göremez; kendi kirasına kiraA'nın dönem-1 RowKey'ini "tahsilat anahtarı" diye verir → 409.
        var (st, code, g) = await Read(await PostAsync(b, "/finans/tahsilat",
            new { cariId = m2, kiraId = rentalB, tutar = 1m, hesap = "Kasa", tahsilatAnahtar = CashService.RowKey(rentalA, 1) }, null));
        Assert.True(st == 409 && code == "mukerrer", $"{st}: {g}");
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));

        var m = await LoginAsync(o, Kim.Muhasebe);
        var y = await Read(await PostAsync(m, "/finans/donem-fatura", new { kiraId = rentalA, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null));
        Assert.Equal(200, y.Status);
        Assert.True(JsonDocument.Parse(y.Govde).RootElement.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(3100m, (await ReadRentalAsync(o, rentalA)).Tahsilat); // 31/90 × 9000
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));
    }

    [Fact]
    public async Task MEDIUM3_tahsilatAnahtar_sunucuda_yeniden_hesaplanir_baska_kiranin_ve_uydurma_anahtar_409()
    {
        var o = await SetUpEnvironmentAsync();
        var (rental2, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, RentalStart.AddDays(10), day: 2));
        var s = await LoginAsync(o, Kim.Muhasebe);
        var k1 = RentACar.Web.Finance.CollectionKey.Generate(o.Kira, 300m, 0); // kira1 panelinin anahtarı

        var (st, code, _) = await Read(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = rental2, tutar = 200m, hesap = "Kasa", tahsilatAnahtar = k1 }, null));
        Assert.True(st == 409 && code == "mukerrer", $"başka kiranın anahtarı: {st}");
        var fabricated = RentACar.Web.Finance.CollectionKey.Generate(rental2, 12345m, 99);
        var (st3, code3, _) = await Read(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = rental2, tutar = 1m, hesap = "Kasa", tahsilatAnahtar = fabricated }, null));
        Assert.True(st3 == 409 && code3 == "mukerrer", $"uydurma anahtar: {st3}");
        Assert.Equal(0, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync()));

        // Meşru: kira1 kendi anahtarıyla yazılır (DB ölçeği "300.0000" ile de, sade "300" ile de aynı kira durumu).
        await Id(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 300m, hesap = "Kasa", tahsilatAnahtar = k1 }, null));
        // Sonraki meşru tahsilat YENİ durumun anahtarıyla (bakiye 0, işlem sayısı 1) — ikinci tahsilat engellenmez.
        var k2 = RentACar.Web.Finance.CollectionKey.Generate(o.Kira, 0m, 1);
        await Id(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 50m, hesap = "Kasa", tahsilatAnahtar = k2 }, null));
        // Bayat k1 tekrar gelirse → 409.
        var (st4, code4, _) = await Read(await PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 300m, hesap = "Kasa", tahsilatAnahtar = k1 }, null));
        Assert.True(st4 == 409 && code4 == "mukerrer", $"bayat anahtar: {st4}");
        Assert.Equal(350m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
    }

    [Fact]
    public async Task MEDIUM3_donem_tahsilat_anahtari_baska_kayitta_kullanildiysa_sessiz_bastirma_yok_gurultulu_hata()
    {
        // Blazor'un ham islemAnahtari yolu hâlâ istemci Guid'i alır: RowKey(kiraA, 1) başka bir tahsilatta
        // önceden kullanılmışsa dönem ucu bunu "daha önce alınmış" SAYMAMALI.
        var o = await SetUpEnvironmentAsync();
        var (rentalA, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), day: 90));
        await ReadAsync(o, sp => sp.GetRequiredService<CashService>().CollectAsync(new CashInput
        {
            CariId = o.Musteri, RentalId = o.Kira, Tutar = 1m, Hesap = LedgerAccountType.Kasa, IslemAnahtari = CashService.RowKey(rentalA, 1),
        }));

        var s = await LoginAsync(o, Kim.Muhasebe);
        var (st, code, g) = await Read(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rentalA, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null));
        Assert.True(st == 400 && code == "dogrulama", $"{st}: {g}");
        Assert.Contains("tahsilat yazılamadı", g);
        Assert.Equal(0m, (await ReadRentalAsync(o, rentalA)).Tahsilat);

        // Meşru tekrar (aynı kiranın kendi dönem tahsilatı) hâlâ sessiz no-op.
        var (rentalC, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 6, 1, 10, 0, 0, TimeSpan.Zero), day: 60));
        var first = await Read(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rentalC, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null));
        var repeat = await Read(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rentalC, donemSira = 1, tahsilat = true, hesap = "Banka" }, null));
        Assert.Equal(200, first.Status);
        Assert.Equal(200, repeat.Status);
        Assert.False(JsonDocument.Parse(repeat.Govde).RootElement.GetProperty("tahsilatYazildi").GetBoolean());
        Assert.Equal(1, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == rentalC)));
    }

    // =================================================================== L1 depozitoda hesap uyumu

    [Fact]
    public async Task L1_pasif_hesap_ve_hesap_dovizi_depozitoda_da_denetlenir()
    {
        Guid inactive = Guid.Empty, tryBank = Guid.Empty;
        var o = await SetUpEnvironmentAsync(async sp =>
        {
            var h = sp.GetRequiredService<FinancialAccountService>();
            inactive = await h.CreateAsync(new FinancialAccountInput { Kod = "ESK", Ad = "Eski Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = false });
            tryBank = await h.CreateAsync(new FinancialAccountInput { Kod = "TRB", Ad = "TL Banka", Tur = "Banka", Doviz = "TRY", Aktif = true });
        });
        var s = await LoginAsync(o, Kim.Muhasebe);

        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = inactive }, NewKey()))).Status);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = inactive }, NewKey()))).Status);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Banka", hesapId = tryBank, doviz = "USD", kur = 30m }, NewKey()))).Status);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Banka", hesapId = tryBank, doviz = "USD", kur = 30m }, NewKey()))).Status);
        Assert.Equal(0, await LedgerCountAsync(o));
        // TRY depozito TRY banka hesabına yazılır.
        var id = await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Banka", hesapId = tryBank }, NewKey()));
        Assert.Contains(await LedgerAsync(o, id), e => e.AccountType == LedgerAccountType.Banka && e.AccountRef == tryBank);
    }

    // =================================================================== L5 iptalde kapsam önce

    [Fact]
    public async Task L5_FinanceReverse_sahibi_baska_sube_operatoru_iptal_edemez_iptal_edilmis_durum_sizmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var m = await LoginAsync(o, Kim.Muhasebe);
        var id = await Id(await PostAsync(m, "/finans/dis-hizmet",
            new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m }, NewKey()));
        var b = await LoginAsync(o, Kim.OperatorBTers);
        var r = await Read(await PostAsync(b, $"/finans/dis-hizmet/{id}/iptal", null, null));
        Assert.Equal((403, "yetki_yok"), (r.Status, r.Kod));
        var cancel = await PostAsync(m, $"/finans/dis-hizmet/{id}/iptal", null, null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        var r2 = await Read(await PostAsync(b, $"/finans/dis-hizmet/{id}/iptal", null, null));
        Assert.True(r2.Status == 403 && r2.Kod == "yetki_yok", $"iptal edilmiş başka-şube kaydında durum sızdı: {r2.Status} {r2.Govde}");
    }

    // =================================================================== gerileme kilitleri (yeşil probe'lar)

    [Fact]
    public async Task Capraz_kiraci_kira_hesap_ve_dis_hizmet_kimlikleri_kabul_edilmez_500_yok()
    {
        Guid account2 = Guid.Empty;
        var o = await SetUpEnvironmentAsync();
        var o2 = await SetUpEnvironmentAsync(async sp => account2 = await sp.GetRequiredService<FinancialAccountService>()
            .CreateAsync(new FinancialAccountInput { Kod = "K2", Ad = "Yabancı Kasa", Tur = "Kasa", Doviz = "TRY", Aktif = true }));
        var s = await LoginAsync(o, Kim.Muhasebe);
        var s2 = await LoginAsync(o2, Kim.Muhasebe);
        var outsourced2 = await Id(await PostAsync(s2, "/finans/dis-hizmet",
            new { kiraId = o2.Kira, cariId = o2.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 100m }, NewKey()));

        var attempts = new (string Yol, object? Govde, bool Anahtar)[]
        {
            ("/finans/tahsilat", new { cariId = o.Musteri, kiraId = o2.Kira, tutar = 10m, hesap = "Kasa" }, true),
            ("/finans/tahsilat", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = account2 }, true),
            ("/finans/fatura", new { kiraId = o2.Kira }, false),
            ("/finans/donem-fatura", new { kiraId = o2.Kira, donemSira = 1 }, false),
            ("/finans/dis-hizmet", new { kiraId = o2.Kira, cariId = o.Tedarikci, alinanHizmet = "V", hizmetBedeli = 10m }, true),
            ("/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o2.Tedarikci, alinanHizmet = "V", hizmetBedeli = 10m }, true),
            ($"/finans/dis-hizmet/{outsourced2}/iptal", null, false),
            ("/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa", hesapId = account2 }, true),
            ("/finans/depozito/irat", new { cariId = o.Musteri, tutar = 1m, kiraId = o2.Kira }, true),
        };
        foreach (var (path, body, key) in attempts)
        {
            var (st, _, g) = await Read(await PostAsync(s, path, body, key ? NewKey() : null));
            Assert.True(st is >= 400 and < 500, $"{path} → {st}: {g}");
        }
        Assert.Equal(DisHizmetDurum.Kayitli, await DbAsync(o2, db => db.DisHizmetAlimlari.AsNoTracking().Where(d => d.Id == outsourced2).Select(d => d.Durum).SingleAsync()));
        Assert.Equal(0, await DbAsync(o2, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(0, await LedgerCountAsync(o));
    }

    [Fact]
    public async Task Eszamanli_fatura_tek_fatura_500_yok()
    {
        var o = await SetUpEnvironmentAsync();
        var sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => LoginAsync(o, Kim.Muhasebe)));
        var result = await Task.WhenAll((await Task.WhenAll(sessions.Select(s => PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, null)))).Select(Read));
        Assert.DoesNotContain(result, x => x.Status >= 500);
        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync()));
        Assert.Equal(300m, await AccountBalanceAsync(o, o.Musteri));
        Assert.Empty(await UnbalancedSetsAsync(o));
    }

    [Fact]
    public async Task Eszamanli_donem_kes_ve_tahsil_tek_fatura_tek_tahsilat()
    {
        var o = await SetUpEnvironmentAsync();
        var (rental, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), day: 90));
        var sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => LoginAsync(o, Kim.Muhasebe)));
        var body = new { kiraId = rental, donemSira = 1, tahsilat = true, hesap = "Kasa" };
        var result = await Task.WhenAll((await Task.WhenAll(sessions.Select(s => PostAsync(s, "/finans/donem-fatura", body, null)))).Select(Read));
        Assert.DoesNotContain(result, x => x.Status >= 500);
        Assert.Equal(1, await DbAsync(o, db => db.Invoices.AsNoTracking().CountAsync(i => i.RentalId == rental || i.KaynakKiraId == rental)));
        Assert.Equal(1, await DbAsync(o, db => db.CashTransactions.AsNoTracking().CountAsync(t => t.RentalId == rental)));
        Assert.Equal(3100m, (await ReadRentalAsync(o, rental)).Tahsilat);
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));
        Assert.Empty(await UnbalancedSetsAsync(o));
    }

    [Fact]
    public async Task Eszamanli_depozito_al_ayni_anahtar_ayni_icerik_tek_kume_hepsi_ayni_id()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var key = NewKey();
        var body = new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" };
        var result = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(s, "/finans/depozito/al", body, key)))).Select(Read));
        Assert.All(result, x => Assert.Equal(200, x.Status));
        Assert.Single(result.Select(x => x.Govde).Distinct());
        Assert.Equal(500m, await DepositBalanceAsync(o, o.Musteri));
    }

    [Fact]
    public async Task Eszamanli_irat_tutulani_asamaz_ve_ayni_anahtar_tek_kayit()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 500m, hesap = "Kasa" }, NewKey()));

        var key = NewKey();
        var same = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 100m, kiraId = o.Kira }, key)))).Select(Read));
        Assert.DoesNotContain(same, x => x.Status >= 500);
        Assert.Equal(400m, await DepositBalanceAsync(o, o.Musteri));

        var different = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(s, "/finans/depozito/irat", new { cariId = o.Musteri, tutar = 150m }, NewKey())))).Select(Read));
        Assert.DoesNotContain(different, x => x.Status >= 500);
        Assert.Equal(2, different.Count(x => x.Status == 200)); // 400 tutuluyor → 2 × 150
        Assert.Equal(100m, await DepositBalanceAsync(o, o.Musteri));
        Assert.Empty(await UnbalancedSetsAsync(o));
    }

    [Fact]
    public async Task Eszamanli_dis_hizmet_ayni_anahtar_tek_kayit_iptal_tek_ters_kayit()
    {
        var o = await SetUpEnvironmentAsync();
        var sessions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => LoginAsync(o, Kim.Muhasebe)));
        var key = NewKey();
        var body = new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Çekici", hizmetBedeli = 1000m, komisyonOran = 10m };
        var create = await Task.WhenAll((await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            PostAsync(sessions[0], "/finans/dis-hizmet", body, key)))).Select(Read));
        Assert.DoesNotContain(create, x => x.Status >= 500);
        Assert.Equal(1, create.Count(x => x.Status == 200));
        var id = JsonDocument.Parse(create.Single(x => x.Status == 200).Govde).RootElement.GetProperty("id").GetGuid();

        var cancel = await Task.WhenAll((await Task.WhenAll(sessions.Select(s => PostAsync(s, $"/finans/dis-hizmet/{id}/iptal", null, null)))).Select(Read));
        Assert.DoesNotContain(cancel, x => x.Status >= 500);
        Assert.Equal(1, cancel.Count(x => x.Status == 204));
        Assert.Equal(8, (await LedgerAsync(o, id)).Count);
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Tedarikci));
        Assert.Empty(await UnbalancedSetsAsync(o));
    }

    [Fact]
    public async Task Csrfsiz_ve_yanlis_belirtecli_istek_reddedilir_yazmaz()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        var body = new { cariId = o.Musteri, kiraId = o.Kira, tutar = 10m, hesap = "Kasa" };
        var result = new[]
        {
            await Read(await PostAsync(s, "/finans/tahsilat", body, NewKey(), xsrf: null)),
            await Read(await PostAsync(s, "/finans/tahsilat", body, NewKey(), xsrf: "yanlis-belirtec")),
            await Read(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 10m, hesap = "Kasa" }, NewKey(), xsrf: null)),
            await Read(await PostAsync(s, $"/finans/dis-hizmet/{Guid.NewGuid()}/iptal", null, null, xsrf: null)),
        };
        Assert.All(result, x => Assert.Equal((400, "xsrf_gecersiz"), (x.Status, x.Kod)));
        Assert.Equal(0, await LedgerCountAsync(o));
    }

    [Fact]
    public async Task USD_kira_TRY_ya_da_EUR_tahsilat_400_USD_tahsilat_ham_tutar()
    {
        var o = await SetUpEnvironmentAsync(sp => sp.GetRequiredService<RentACar.Application.Kur.FixedExchangeRateService>()
            .UpsertAsync(new RentACar.Application.Kur.SabitKurInput { Kod = "USD", Kur = 30m, Aktif = true }));
        var (rentalUsd, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, RentalStart.AddDays(20), day: 3, currency: "USD"));
        var s = await LoginAsync(o, Kim.Muhasebe);

        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = rentalUsd, tutar = 100m, hesap = "Kasa" }, NewKey()))).Status);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = rentalUsd, tutar = 100m, hesap = "Kasa", doviz = "EUR", kur = 35m }, NewKey()))).Status);
        Assert.Equal(0, await LedgerCountAsync(o));

        await Id(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = rentalUsd, tutar = 10m, hesap = "Kasa", doviz = "USD", kur = 30m }, NewKey()));
        Assert.Equal(10m, (await ReadRentalAsync(o, rentalUsd)).Tahsilat);  // USD kira: ham tutar
        Assert.Equal(-300m, await AccountBalanceAsync(o, o.Musteri));      // baz: 10 × 30
    }

    [Fact]
    public async Task Isaret_ve_bakiyeler_elle_oracle()
    {
        var o = await SetUpEnvironmentAsync();
        var s = await LoginAsync(o, Kim.Muhasebe);
        // Fatura 300 (+300) → tahsilat 500 banka (−500, fazla) → iade ödemesi 200 kasa (+200) → depozito al 50 kasa
        // (cari bakiyesini ETKİLEMEZ) → dış hizmet tedarikçi 1000 %10 (tedarikçi −900).
        await Id(await PostAsync(s, "/finans/fatura", new { kiraId = o.Kira }, null));
        await Id(await PostAsync(s, "/finans/tahsilat", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 500m, hesap = "Banka" }, NewKey()));
        await Id(await PostAsync(s, "/finans/odeme", new { cariId = o.Musteri, kiraId = o.Kira, tutar = 200m, hesap = "Kasa" }, NewKey()));
        await Id(await PostAsync(s, "/finans/depozito/al", new { cariId = o.Musteri, tutar = 50m, hesap = "Kasa" }, NewKey()));
        await Id(await PostAsync(s, "/finans/dis-hizmet", new { kiraId = o.Kira, cariId = o.Tedarikci, alinanHizmet = "Vale", hizmetBedeli = 1000m, komisyonOran = 10m }, NewKey()));

        var k = await ReadRentalAsync(o, o.Kira);
        var cash = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Kasa)
            .Select(e => new { e.Direction, e.Amount.Amount, e.Amount.Rate }).ToListAsync());
        var bank = await DbAsync(o, db => db.AccountLedgerEntries.AsNoTracking().Where(e => e.AccountType == LedgerAccountType.Banka)
            .Select(e => new { e.Direction, e.Amount.Amount, e.Amount.Rate }).ToListAsync());
        Assert.Equal(300m, k.Tahsilat);   // 500 − 200
        Assert.Equal(0m, k.Bakiye);
        Assert.Equal(0m, await AccountBalanceAsync(o, o.Musteri));   // +300 −500 +200
        Assert.Equal(-900m, await AccountBalanceAsync(o, o.Tedarikci)); // −1000 +100
        Assert.Equal(-150m, cash.Sum(x => (x.Direction == LedgerDirection.Debit ? 1 : -1) * x.Amount * x.Rate)); // −200 +50
        Assert.Equal(500m, bank.Sum(x => (x.Direction == LedgerDirection.Debit ? 1 : -1) * x.Amount * x.Rate));
        Assert.Empty(await UnbalancedSetsAsync(o));
    }

    [Fact]
    public async Task Iptal_kiraya_fatura_donem_ve_dis_hizmet_400()
    {
        var o = await SetUpEnvironmentAsync();
        var (rental, _) = await ReadAsync(o, sp => RentalAsync(sp, o.Musteri, new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.Zero), day: 90));
        await ReadAsync(o, async sp => { await sp.GetRequiredService<RentalService>().CancelAsync(rental); return 0; });
        var s = await LoginAsync(o, Kim.Muhasebe);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/fatura", new { kiraId = rental }, null))).Status);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/donem-fatura", new { kiraId = rental, donemSira = 1, tahsilat = true, hesap = "Kasa" }, null))).Status);
        Assert.Equal(400, (await Read(await PostAsync(s, "/finans/dis-hizmet", new { kiraId = rental, cariId = o.Tedarikci, alinanHizmet = "V", hizmetBedeli = 10m }, NewKey()))).Status);
        Assert.Equal(0, await LedgerCountAsync(o));
    }

    [Fact]
    public async Task Deterministik_anahtar_uc_kullanici_eszamanli_tek_tahsilat()
    {
        var o = await SetUpEnvironmentAsync();
        var sessions = await Task.WhenAll(new[] { Kim.Muhasebe, Kim.Admin, Kim.OperatorA, Kim.Muhasebe, Kim.Admin, Kim.OperatorA }.Select(k => LoginAsync(o, k)));
        var k = RentACar.Web.Finance.CollectionKey.Generate(o.Kira, 300m, 0);
        var result = await Task.WhenAll((await Task.WhenAll(sessions.Select(s => PostAsync(s, "/finans/tahsilat",
            new { cariId = o.Musteri, kiraId = o.Kira, tutar = 300m, hesap = "Kasa", tahsilatAnahtar = k }, NewKey())))).Select(Read));
        Assert.DoesNotContain(result, x => x.Status >= 500);
        Assert.Equal(1, result.Count(x => x.Status == 200));
        Assert.All(result.Where(x => x.Status != 200), x => Assert.Equal("mukerrer", x.Kod));
        Assert.Equal(300m, (await ReadRentalAsync(o, o.Kira)).Tahsilat);
        Assert.Equal(-300m, await AccountBalanceAsync(o, o.Musteri));
    }
}
