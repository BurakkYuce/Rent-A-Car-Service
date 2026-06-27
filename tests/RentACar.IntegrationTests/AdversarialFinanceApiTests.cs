using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// ADVERSARIAL probes against the NEW Finance REST API (src/RentACar.Api/Endpoints/FinanceApi.cs).
/// Goal: REFUTE its safety — cross-tenant money access, auth/role bypass, request→service mapping
/// bugs, idempotency-over-HTTP, error leakage. Money LOGIC (CashService/InvoiceService) is trusted;
/// these probes attack the HTTP/DTO/authz layer. Runs against the real RLS-enforcing Postgres
/// (racar_app role = NOBYPASSRLS), so isolation results are provable, not mocked.
/// </summary>
[Collection("postgres")]
public sealed class AdversarialFinanceApiTests(PostgresFixture fx)
{
    private static string Uniq(string p) => $"{p}{Guid.NewGuid():N}";

    private async Task<HttpClient> SeedAndLoginAsync(ApiFactory api, string code, UserRole role, string user = "u", string pass = "p")
    {
        await ApiSeed.TenantUserAsync(fx.OwnerConnectionString, code, user, pass, role);
        return await api.LoginClientAsync(code, user, pass);
    }

    private static async Task<Guid> CreateIdAsync(HttpClient c, string url, object body)
    {
        var resp = await c.PostAsJsonAsync(url, body);
        var raw = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.Created, $"POST {url} expected 201 but got {(int)resp.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<decimal> BalanceAsync(HttpClient c, Guid cariId)
    {
        var resp = await c.GetAsync($"/api/v1/finance/customers/{cariId}/balance");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("bakiye").GetDecimal();
    }

    /// <summary>Asserts the {error,message} envelope is present (no stack-trace leak).</summary>
    private static async Task AssertErrorEnvelopeAsync(HttpResponseMessage resp)
    {
        var raw = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.TryGetProperty("error", out var err), $"missing 'error' in: {raw}");
        Assert.True(doc.RootElement.TryGetProperty("message", out _), $"missing 'message' in: {raw}");
        Assert.False(string.IsNullOrWhiteSpace(err.GetString()), $"empty 'error' in: {raw}");
        Assert.DoesNotContain("StackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", raw); // .NET stack-trace frame marker
    }

    /// <summary>Builds an invoiceable rental (GenelToplam > 0) in the caller's tenant.</summary>
    private static async Task<Guid> CreateRentalAsync(HttpClient c, decimal gunluk = 100m)
    {
        var plaka = "34" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var vehicleId = await CreateIdAsync(c, "/api/v1/vehicles",
            new { plaka, grup = "B", durum = "Musait", km = 0, yakit = "Benzin" });
        var custId = await CreateIdAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Rent", soyad = "Er" });
        var bas = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var bit = bas.AddDays(3); // 3 × gunluk = GenelToplam
        var resvId = await CreateIdAsync(c, "/api/v1/reservations",
            new { musteriId = custId, vehicleId, basTar = bas, bitTar = bit, gunlukUcret = gunluk });
        (await c.PostAsync($"/api/v1/reservations/{resvId}/confirm", null)).EnsureSuccessStatusCode();
        var conv = await c.PostAsync($"/api/v1/reservations/{resvId}/convert", null);
        conv.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await conv.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("rentalId").GetGuid();
    }

    // ───────────────────────────── 2) AUTH / ROLE BYPASS ─────────────────────────────

    [Fact]
    public async Task Operator_is_forbidden_on_EVERY_finance_route()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advop"), UserRole.Operator);
        var anyId = Guid.NewGuid();
        var cashBody = new { cariId = Guid.NewGuid(), tutar = 100m, doviz = "TRY", kur = 1m, hesap = "Kasa" };
        var transferBody = new { kaynak = "Kasa", hedef = "Banka", tutar = 50m, doviz = "TRY", kur = 1m };

        foreach (var url in new[]
                 {
                     "/api/v1/finance/cash",
                     $"/api/v1/finance/cash/{anyId}",
                     $"/api/v1/finance/customers/{anyId}/balance",
                     $"/api/v1/finance/customers/{anyId}/statement",
                     "/api/v1/finance/invoices",
                     $"/api/v1/finance/invoices/{anyId}",
                 })
        {
            var resp = await c.GetAsync(url);
            Assert.True(HttpStatusCode.Forbidden == resp.StatusCode, $"GET {url} expected 403 got {(int)resp.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/v1/finance/cash/collect", cashBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/v1/finance/cash/pay", cashBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/v1/finance/cash/transfer", transferBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/v1/finance/cash/{anyId}/reverse", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync($"/api/v1/finance/invoices/from-rental/{anyId}", null)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_is_401_on_finance_routes()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = api.CreateClient(); // no bearer token
        var anyId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/finance/cash")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync($"/api/v1/finance/customers/{anyId}/balance")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync($"/api/v1/finance/customers/{anyId}/statement")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/v1/finance/invoices")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await c.PostAsJsonAsync("/api/v1/finance/cash/collect", new { cariId = anyId, tutar = 1m, hesap = "Kasa" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync($"/api/v1/finance/cash/{anyId}/reverse", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync($"/api/v1/finance/invoices/from-rental/{anyId}", null)).StatusCode);
    }

    [Fact]
    public async Task Muhasebe_role_is_allowed_through_the_finance_gate()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advmuh"), UserRole.Muhasebe);

        // Muhasebe has FinanceWrite (but NOT OperationsWrite) → finance reads/writes pass the gate.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/finance/cash")).StatusCode);

        // cariId existence is not validated by the service, so no OperationsWrite-gated setup needed.
        var cari = Guid.NewGuid();
        var cashId = await CreateIdAsync(c, "/api/v1/finance/cash/collect",
            new { cariId = cari, tutar = 100m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.NotEqual(Guid.Empty, cashId);
        Assert.Equal(-100m, await BalanceAsync(c, cari));

        // Cross-check: the same role is correctly BLOCKED from an OperationsWrite endpoint.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync("/api/v1/customers", new { tip = "Bireysel", ad = "M" })).StatusCode);
    }

    // ───────────────────────── 1) CROSS-TENANT MONEY ACCESS ─────────────────────────

    [Fact]
    public async Task CrossTenant_reads_are_isolated_balance_statement_and_byId()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var ca = await SeedAndLoginAsync(api, Uniq("advxa"), UserRole.Admin);
        var aCari = await CreateIdAsync(ca, "/api/v1/customers", new { tip = "Bireysel", ad = "A" });
        var aCash = await CreateIdAsync(ca, "/api/v1/finance/cash/collect",
            new { cariId = aCari, tutar = 1000m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(-1000m, await BalanceAsync(ca, aCari));

        var cb = await SeedAndLoginAsync(api, Uniq("advxb"), UserRole.Admin);

        // B passes A's cariId → RLS hides A's ledger → balance 0, statement empty (NOT a leak).
        Assert.Equal(0m, await BalanceAsync(cb, aCari));
        var stmt = await cb.GetFromJsonAsync<List<JsonElement>>($"/api/v1/finance/customers/{aCari}/statement");
        Assert.Empty(stmt!);

        // B passes A's cashId → 404 (not visible), and B's own list excludes it.
        var single = await cb.GetAsync($"/api/v1/finance/cash/{aCash}");
        Assert.Equal(HttpStatusCode.NotFound, single.StatusCode);
        var bList = await cb.GetFromJsonAsync<List<JsonElement>>("/api/v1/finance/cash");
        Assert.DoesNotContain(bList!, e => e.GetProperty("id").GetGuid() == aCash);

        // A still sees its own money intact (control).
        Assert.Equal(-1000m, await BalanceAsync(ca, aCari));
    }

    [Fact]
    public async Task CrossTenant_reverse_of_other_tenants_cash_fails_and_leaves_it_intact()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var cb = await SeedAndLoginAsync(api, Uniq("advrb"), UserRole.Admin);
        var bCari = await CreateIdAsync(cb, "/api/v1/customers", new { tip = "Bireysel", ad = "B" });
        var bCash = await CreateIdAsync(cb, "/api/v1/finance/cash/collect",
            new { cariId = bCari, tutar = 777m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(-777m, await BalanceAsync(cb, bCari));

        // A attempts to reverse B's cash transaction by id.
        var ca = await SeedAndLoginAsync(api, Uniq("advra"), UserRole.Admin);
        var attack = await ca.PostAsync($"/api/v1/finance/cash/{bCash}/reverse", null);
        Assert.Equal(HttpStatusCode.BadRequest, attack.StatusCode); // resolves as "not found" under A's RLS
        await AssertErrorEnvelopeAsync(attack);

        // B's money is untouched; B can still legitimately reverse it (proves it was never reversed).
        Assert.Equal(-777m, await BalanceAsync(cb, bCari));
        var legit = await cb.PostAsync($"/api/v1/finance/cash/{bCash}/reverse", null);
        Assert.Equal(HttpStatusCode.Created, legit.StatusCode);
        Assert.Equal(0m, await BalanceAsync(cb, bCari));
    }

    [Fact]
    public async Task CrossTenant_invoice_from_other_tenants_rental_fails()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var cb = await SeedAndLoginAsync(api, Uniq("advib"), UserRole.Admin);
        var bRental = await CreateRentalAsync(cb);
        var bInvBefore = await cb.GetFromJsonAsync<List<JsonElement>>("/api/v1/finance/invoices");

        var ca = await SeedAndLoginAsync(api, Uniq("advia"), UserRole.Admin);
        var attack = await ca.PostAsync($"/api/v1/finance/invoices/from-rental/{bRental}", null);
        Assert.Equal(HttpStatusCode.BadRequest, attack.StatusCode); // "Kira sözleşmesi bulunamadı" under A's RLS
        await AssertErrorEnvelopeAsync(attack);

        // No invoice leaked into A, and none injected into B.
        var aInv = await ca.GetFromJsonAsync<List<JsonElement>>("/api/v1/finance/invoices");
        Assert.Empty(aInv!);
        var bInvAfter = await cb.GetFromJsonAsync<List<JsonElement>>("/api/v1/finance/invoices");
        Assert.Equal(bInvBefore!.Count, bInvAfter!.Count);
    }

    [Fact]
    public async Task CrossTenant_collect_with_foreign_cariId_stays_in_caller_tenant()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var cb = await SeedAndLoginAsync(api, Uniq("advcb"), UserRole.Admin);
        var bCari = await CreateIdAsync(cb, "/api/v1/customers", new { tip = "Bireysel", ad = "B" });
        Assert.Equal(0m, await BalanceAsync(cb, bCari));

        // A posts a collection referencing B's cariId in the body.
        var ca = await SeedAndLoginAsync(api, Uniq("advca"), UserRole.Admin);
        var resp = await ca.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = bCari, tutar = 1234m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode); // accepted, but RLS pins it to A

        // CRITICAL: B's ledger for that cariId is UNCHANGED → no cross-tenant write/corruption.
        Assert.Equal(0m, await BalanceAsync(cb, bCari));
        // A merely sees its OWN row keyed by that GUID (its own tenant's data, not B's).
        Assert.Equal(-1234m, await BalanceAsync(ca, bCari));
    }

    // ─────────────────── 3) REQUEST→SERVICE MAPPING / BAD INPUT ───────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Collect_rejects_nonpositive_amount(decimal tutar)
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advamt"), UserRole.Admin);
        var resp = await c.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = Guid.NewGuid(), tutar, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertErrorEnvelopeAsync(resp);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Collect_rejects_nonpositive_kur(decimal kur)
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advkur"), UserRole.Admin);
        var resp = await c.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = Guid.NewGuid(), tutar = 100m, doviz = "USD", kur, hesap = "Kasa" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertErrorEnvelopeAsync(resp);
    }

    [Theory]
    [InlineData("Cari")]
    [InlineData("Gelir")]
    [InlineData("Kdv")]
    [InlineData("Gider")]
    public async Task Collect_rejects_non_Kasa_Banka_hesap(string hesap)
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advhes"), UserRole.Admin);
        var resp = await c.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = Guid.NewGuid(), tutar = 100m, doviz = "TRY", kur = 1m, hesap });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertErrorEnvelopeAsync(resp);
    }

    [Fact]
    public async Task Collect_rejects_empty_cari()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advemp"), UserRole.Admin);
        var resp = await c.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = Guid.Empty, tutar = 100m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertErrorEnvelopeAsync(resp);
    }

    [Theory]
    [InlineData("Kasa", "Kasa", 50)]   // same account
    [InlineData("Kasa", "Banka", 0)]   // nonpositive amount
    [InlineData("Gelir", "Banka", 50)] // non Kasa/Banka source
    [InlineData("Kasa", "Cari", 50)]   // non Kasa/Banka target
    public async Task Transfer_rejects_bad_inputs(string kaynak, string hedef, decimal tutar)
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advtr"), UserRole.Admin);
        var resp = await c.PostAsJsonAsync("/api/v1/finance/cash/transfer",
            new { kaynak, hedef, tutar, doviz = "TRY", kur = 1m });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertErrorEnvelopeAsync(resp);
    }

    [Fact]
    public async Task Collect_maps_multicurrency_money_dto_faithfully()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advfx"), UserRole.Admin);
        var cari = await CreateIdAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Fx" });

        // 100 USD @ kur 30 → base 3000.
        var resp = await c.PostAsJsonAsync("/api/v1/finance/cash/collect",
            new { cariId = cari, tutar = 100m, doviz = "usd", kur = 30m, hesap = "Banka" });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var t = doc.RootElement.GetProperty("tutar");
        Assert.Equal(100m, t.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", t.GetProperty("currency").GetString()); // normalized upper
        Assert.Equal(30m, t.GetProperty("rate").GetDecimal());
        Assert.Equal(3000m, t.GetProperty("base").GetDecimal());
        Assert.Equal("Banka", doc.RootElement.GetProperty("karsiHesap").GetString());

        // Balance is in base currency → −3000.
        Assert.Equal(-3000m, await BalanceAsync(c, cari));
    }

    // ───────────────────────── 4) IDEMPOTENCY OVER HTTP ─────────────────────────

    [Fact]
    public async Task Reverse_is_idempotent_double_reverse_and_reverse_of_reversal_both_400()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advidem"), UserRole.Admin);
        var cari = await CreateIdAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Id" });
        var cashId = await CreateIdAsync(c, "/api/v1/finance/cash/collect",
            new { cariId = cari, tutar = 500m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(-500m, await BalanceAsync(c, cari));

        // First reverse succeeds.
        var first = await c.PostAsync($"/api/v1/finance/cash/{cashId}/reverse", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var reversalId = firstDoc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(0m, await BalanceAsync(c, cari));

        // Second reverse of the SAME original must fail (no double-reverse).
        var second = await c.PostAsync($"/api/v1/finance/cash/{cashId}/reverse", null);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        await AssertErrorEnvelopeAsync(second);

        // Reversing the reversal itself must also fail.
        var revOfRev = await c.PostAsync($"/api/v1/finance/cash/{reversalId}/reverse", null);
        Assert.Equal(HttpStatusCode.BadRequest, revOfRev.StatusCode);
        await AssertErrorEnvelopeAsync(revOfRev);

        // Balance stayed at 0 (not +500 from a double reverse).
        Assert.Equal(0m, await BalanceAsync(c, cari));
    }

    [Fact]
    public async Task Concurrent_reverse_only_one_succeeds()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advconc"), UserRole.Admin);
        var cari = await CreateIdAsync(c, "/api/v1/customers", new { tip = "Bireysel", ad = "Cc" });
        var cashId = await CreateIdAsync(c, "/api/v1/finance/cash/collect",
            new { cariId = cari, tutar = 1000m, doviz = "TRY", kur = 1m, hesap = "Kasa" });
        Assert.Equal(-1000m, await BalanceAsync(c, cari));

        // Fire several reverse requests concurrently against the same original.
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => c.PostAsync($"/api/v1/finance/cash/{cashId}/reverse", null))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        var created = results.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = results.Count(r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(1, created);                 // exactly one reversal posted
        Assert.Equal(results.Length - 1, rejected); // all others rejected (idempotent guard)

        // Money correctness oracle: balance restored exactly once → 0 (not +N×1000).
        Assert.Equal(0m, await BalanceAsync(c, cari));
    }

    // ───────────── 5) ERROR LEAKAGE / STATUS-CODE CORRECTNESS ─────────────

    [Fact]
    public async Task Missing_cash_and_invoice_return_404_envelope()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("adv404"), UserRole.Admin);

        var cash = await c.GetAsync($"/api/v1/finance/cash/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, cash.StatusCode);
        await AssertErrorEnvelopeAsync(cash);

        var inv = await c.GetAsync($"/api/v1/finance/invoices/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, inv.StatusCode);
        await AssertErrorEnvelopeAsync(inv);
    }

    /// <summary>
    /// REGRESYON (adversarial bulgu düzeltildi): /invoices/from-rental kdvRate query param'ı
    /// istemci-kontrollü. Endpoint artık kdvRate'i 0..1 aralığında doğrular → geçersiz (negatif/
    /// >1) CLIENT girişi 400 (validation), 500 değil. Sınır-içi (örn. 0.18) çalışır.
    /// </summary>
    [Fact]
    public async Task Invoice_from_rental_invalid_kdvRate_is_400_not_500()
    {
        using var api = new ApiFactory(fx.AppConnectionString);
        var c = await SeedAndLoginAsync(api, Uniq("advkdv"), UserRole.Admin);
        var rental = await CreateRentalAsync(c); // GenelToplam = 300 > 0 (invoiceable)

        // Negatif → 400 (validation), 500 değil.
        var neg = await c.PostAsync($"/api/v1/finance/invoices/from-rental/{rental}?kdvRate=-1", null);
        Assert.Equal(HttpStatusCode.BadRequest, neg.StatusCode);
        await AssertErrorEnvelopeAsync(neg);
        using (var doc = JsonDocument.Parse(await neg.Content.ReadAsStringAsync()))
            Assert.Equal("validation", doc.RootElement.GetProperty("error").GetString());

        // >1 (örn. 100) → 400 (sınır üstü).
        var tooBig = await c.PostAsync($"/api/v1/finance/invoices/from-rental/{rental}?kdvRate=100", null);
        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);

        // Geçerli oran (0.18) → 201; aynı rental hâlâ faturalanabilir (negatif kalıcı iz bırakmadı).
        var ok = await c.PostAsync($"/api/v1/finance/invoices/from-rental/{rental}?kdvRate=0.18", null);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }
}
