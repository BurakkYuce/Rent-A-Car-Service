using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace RentACar.IntegrationTests;

/// <summary>
/// #320 L2 — <c>X-Beklenen-Oturum</c> (kiracı|kullanıcı). SPA'nın tekrar öncesi <c>ben</c> doğrulaması ile POST arasında
/// başka sekmede başka kullanıcıyla giriş yapılırsa ortak çerez (ve ona bağlı XSRF) yeni kullanıcınındır; başlık eski
/// kimliği taşıdığı için sunucu YAZMADAN 409 <c>oturum_degisti</c> döner. Tek <see cref="HttpClient"/> = tek tarayıcı
/// (ortak çerez kavanozu). BAĞIMSIZ ORACLE: beklenen tutar ve sayılar elle (300 TL tek düzeltme → bakiye 300, tek kasa
/// dışı defter kümesi).
/// </summary>
public sealed partial class UiFinanceHubApiTests
{
    private const string ExpectedSessionHeader = "X-Beklenen-Oturum";

    /// <summary>Aynı istemcide (aynı çerez kavanozu) giriş: başka sekmedeki girişin karşılığı. Yeni XSRF + ben kimliği döner.</summary>
    private async Task<(Session S, string Identity)> LoginOnAsync(HttpClient c, Env e, Who who)
    {
        var first = CookieValue(await c.GetAsync(Root + "/oturum/xsrf"), "XSRF-TOKEN")!;
        var req = new HttpRequestMessage(HttpMethod.Post, Root + "/oturum/giris")
        {
            Content = JsonContent.Create(new { firma = e.Code, kullanici = e.Users[who], sifre = e.Password }),
        };
        req.Headers.Add("X-XSRF-TOKEN", first);
        var r = await c.SendAsync(req);
        var ben = await Ok(r);
        var identity = $"{ben.GetProperty("kiraci").GetProperty("id").GetGuid()}|{ben.GetProperty("kullanici").GetProperty("id").GetGuid()}";
        return (new Session(c, CookieValue(r, "XSRF-TOKEN")!), identity);
    }

    private static Task<HttpResponseMessage> AdjustAsync(Session s, Guid customer, decimal amount, string key, string? expected)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/bakiye-duzeltme")
        {
            Content = JsonContent.Create(new { cariId = customer, yon = "Borclandir", tutar = amount }),
        };
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        req.Headers.Add("Idempotency-Key", key);
        if (expected is not null) req.Headers.Add(ExpectedSessionHeader, expected);
        return s.C.SendAsync(req);
    }

    [Fact]
    public async Task Expected_session_header_of_previous_user_is_rejected_without_write()
    {
        var e = await SetupAsync();
        var c = fx.Web.Client();
        var (_, accountant) = await LoginOnAsync(c, e, Who.Accountant);
        // Başka sekmede ADMIN girdi: ortak çerez ve XSRF artık admininki.
        var (admin, adminIdentity) = await LoginOnAsync(c, e, Who.Admin);
        Assert.NotEqual(accountant, adminIdentity);

        // Muhasebecinin donmuş denemesi (aynı anahtar + gövde) admin oturumuyla gider → yazılmaz.
        var key = NewKey();
        await Problem(await AdjustAsync(admin, e.CustomerA, 300m, key, accountant), HttpStatusCode.Conflict, "oturum_degisti");
        Assert.Equal(0m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(0, await DbAsync(e, db => db.AccountLedgerEntries.CountAsync()));

        // Aynı anahtar, doğru kimlik: işlem yazılır (anahtar 409'da tüketilmedi).
        await IdOf(await AdjustAsync(admin, e.CustomerA, 300m, key, adminIdentity));
        Assert.Equal(300m, await BalanceAsync(e, e.CustomerA));
    }

    [Fact]
    public async Task Expected_session_header_absent_or_matching_behaves_as_before()
    {
        var e = await SetupAsync();
        var c = fx.Web.Client();
        var (s, identity) = await LoginOnAsync(c, e, Who.Accountant);

        // Başlıksız (harici istemci, eski sekme): davranış değişmez.
        await IdOf(await AdjustAsync(s, e.CustomerA, 100m, NewKey(), null));
        // Büyük harfli Guid aynı kimliktir.
        await IdOf(await AdjustAsync(s, e.CustomerA, 200m, NewKey(), identity.ToUpperInvariant()));
        Assert.Equal(300m, await BalanceAsync(e, e.CustomerA));
    }

    [Fact]
    public async Task Expected_session_header_other_tenant_or_malformed_is_rejected()
    {
        var e = await SetupAsync();
        var c = fx.Web.Client();
        var (s, identity) = await LoginOnAsync(c, e, Who.Accountant);
        var user = identity.Split('|')[1];

        foreach (var expected in new[]
                 {
                     $"{Guid.NewGuid()}|{user}",   // aynı kullanıcı kimliği, başka kiracı
                     $"{e.TenantId}|{Guid.NewGuid()}", // aynı kiracı, başka kullanıcı
                     identity + "|*",               // şubeli tam bağlam biçimi kabul edilmez
                     "bozuk",
                 })
            await Problem(await AdjustAsync(s, e.CustomerA, 100m, NewKey(), expected), HttpStatusCode.Conflict, "oturum_degisti");

        // Çoklu değer (virgülle birleşir) de reddedilir.
        var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/bakiye-duzeltme")
        {
            Content = JsonContent.Create(new { cariId = e.CustomerA, yon = "Borclandir", tutar = 100m }),
        };
        req.Headers.Add("X-XSRF-TOKEN", s.Xsrf);
        req.Headers.Add("Idempotency-Key", NewKey());
        req.Headers.Add(ExpectedSessionHeader, new[] { identity, identity });
        await Problem(await s.C.SendAsync(req), HttpStatusCode.Conflict, "oturum_degisti");

        Assert.Equal(0m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(0, await DbAsync(e, db => db.AccountLedgerEntries.CountAsync()));
    }

    /// <summary>
    /// #417 L1 (adversarial P4'ün kalıcı karşılığı): rastgele anahtarlı form artık anahtarı İLK gönderimdeki kimliğe bağlar
    /// ve tekrarda da başlığı gönderir. İlk gönderim yazıldı (yanıt kayboldu), başka sekmede admin girdi, aynı anahtarla
    /// tekrar → 409, ikinci yazım yok. Oracle: tek işlem 500 → bakiye 500 (başlıksız tekrar 1000 yapıyordu).
    /// </summary>
    [Fact]
    public async Task Expected_session_header_on_retry_after_user_switch_prevents_second_write()
    {
        var e = await SetupAsync();
        var c = fx.Web.Client();
        var (accountantSession, accountant) = await LoginOnAsync(c, e, Who.Accountant);
        var key = NewKey();
        await IdOf(await AdjustAsync(accountantSession, e.CustomerA, 500m, key, accountant));

        var (admin, _) = await LoginOnAsync(c, e, Who.Admin);
        await Problem(await AdjustAsync(admin, e.CustomerA, 500m, key, accountant), HttpStatusCode.Conflict, "oturum_degisti");

        Assert.Equal(500m, await BalanceAsync(e, e.CustomerA));
    }

    /// <summary>
    /// #417 L2: deterministik kira tahsilatı (gövdede <c>tahsilatAnahtar</c>; başlıkta aynı değer + denemenin kimliği). İlk
    /// deneme sunucuya ULAŞMADI, başka sekmede admin girdi: muhasebecinin tekrarı admin adına YAZILMAZ (409, kasa 0); admin
    /// kendi kimliğiyle gönderince tek tahsilat yazılır (kasa 1).
    /// </summary>
    [Fact]
    public async Task Expected_session_header_on_deterministic_collection_is_not_written_for_other_user()
    {
        var e = await SetupAsync();
        var c = fx.Web.Client();
        var (_, accountant) = await LoginOnAsync(c, e, Who.Accountant);
        var detail = await Ok(await c.GetAsync(Root + $"/kiralar/{e.Rental}"));
        var key = detail.GetProperty("tahsilat").GetProperty("anahtar").GetGuid().ToString();
        var (admin, adminIdentity) = await LoginOnAsync(c, e, Who.Admin);

        Task<HttpResponseMessage> CollectAsync(string expected)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, V1 + "/tahsilat")
            {
                Content = JsonContent.Create(new { cariId = e.CustomerA, kiraId = e.Rental, tutar = 100m, hesap = "Kasa", tahsilatAnahtar = key }),
            };
            req.Headers.Add("X-XSRF-TOKEN", admin.Xsrf);
            req.Headers.Add("Idempotency-Key", key);
            req.Headers.Add(ExpectedSessionHeader, expected);
            return admin.C.SendAsync(req);
        }

        await Problem(await CollectAsync(accountant), HttpStatusCode.Conflict, "oturum_degisti");
        Assert.Equal(0, await CashCountAsync(e));

        await IdOf(await CollectAsync(adminIdentity));
        Assert.Equal(1, await CashCountAsync(e));
    }

    /// <summary>
    /// adv-419 L3: ANAHTARSIZ yazma (kasa işlemi ters kaydı). SPA bayat sekmenin bildiği kimliği (<c>ben</c>) başlığa koyar.
    /// Muhasebeci 200 borç + 80 tahsilat yazdı; başka sekmede admin girdi (ortak çerez admininki); bayat sekmedeki "ters
    /// kayıt" tıkı muhasebecinin kimliğiyle gider → 409, ters kayıt yazılmaz. Admin kendi kimliğiyle gönderince yazılır.
    /// Oracle elle: 200 − 80 = 120; ters kayıttan sonra 200; kasa satırı 1 → 2.
    /// </summary>
    [Fact]
    public async Task Expected_session_header_on_keyless_reverse_is_not_written_for_other_user()
    {
        var e = await SetupAsync();
        var c = fx.Web.Client();
        var (accountantSession, accountant) = await LoginOnAsync(c, e, Who.Accountant);
        await IdOf(await AdjustAsync(accountantSession, e.CustomerA, 200m, NewKey(), accountant));
        var collection = await IdOf(await PostAsync(accountantSession, "/api/ui/v1/finans/tahsilat",
            new { cariId = e.CustomerA, tutar = 80m, hesap = "Kasa" }, NewKey()));
        Assert.Equal(120m, await BalanceAsync(e, e.CustomerA));

        var (admin, adminIdentity) = await LoginOnAsync(c, e, Who.Admin);

        Task<HttpResponseMessage> ReverseAsync(string expected)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, V1 + $"/kasa/islemler/{collection}/ters");
            req.Headers.Add("X-XSRF-TOKEN", admin.Xsrf);
            req.Headers.Add(ExpectedSessionHeader, expected); // Idempotency-Key YOK
            return admin.C.SendAsync(req);
        }

        await Problem(await ReverseAsync(accountant), HttpStatusCode.Conflict, "oturum_degisti");
        Assert.Equal(120m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(1, await CashCountAsync(e));

        await IdOf(await ReverseAsync(adminIdentity));
        Assert.Equal(200m, await BalanceAsync(e, e.CustomerA));
        Assert.Equal(2, await CashCountAsync(e));
        await AllLedgerBalancedAsync(e);
    }
}
