using System.Net;
using Microsoft.EntityFrameworkCore;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR #295 bağımsız KVKK incelemesinin probe'larından kalıcı çit. Beklenen değerler elle kurulmuş senaryodan:
/// H1 tür çevirmesi gizli vergi no'yu açmaz; L1/L2 assistans gizli ad/telefon sözleşmesi; M1 operatör detayı.
/// </summary>
public sealed partial class UiCustomerApiTests
{
    private const string AssistanceUrl = V1 + "/assistans-talepleri";

    private static async Task<(Guid Id, string? Version)> PostAssistanceAsync(Session s, object body)
    {
        var (c, _) = await Json(await Send(s, HttpMethod.Post, AssistanceUrl, body), HttpStatusCode.Created);
        return (c.GetProperty("talep").GetProperty("id").GetGuid(), c.GetProperty("surum").GetString());
    }

    private Task<(string? AdSoyad, string? CepTel)> StoredContactAsync(Guid tenantId, Guid id)
        => ReadAsync(tenantId, async db =>
        {
            var a = await db.AssistansTalepleri.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.AdSoyad, x.CepTel }).SingleAsync();
            return (a.AdSoyad, a.CepTel);
        });

    [Fact]
    public async Task R295_P1_assistance_search_does_not_match_hidden_caller_or_customer()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var secret = Marker();
        const string customerPhone = "05327770011";
        var hidden = await CreateViaApiAsync(admin, new()
        {
            ["tip"] = "Bireysel", ["ad"] = "Gizli", ["soyad"] = secret, ["cepTel"] = customerPhone,
            ["anonimAd"] = true, ["anonimTelefon"] = true,
        });
        var rental = await RentalAsync(e, hidden, "SubeA");
        const string callerPhone = "05438880022";
        await PostAssistanceAsync(admin, new { rentalId = rental.RentalId, mesaj = "Akü bitti" });
        var (_, rawCaller) = await Json(await Send(admin, HttpMethod.Post, AssistanceUrl,
            new { rentalId = rental.RentalId, adSoyad = "Arayan " + secret, cepTel = callerPhone, mesaj = "Lastik" }),
            HttpStatusCode.Created);
        Assert.DoesNotContain(callerPhone, rawCaller);

        foreach (var probe in new[] { callerPhone[..8], customerPhone[..8], secret[..6] })
        {
            var (page, raw) = await Json(await Send(admin, HttpMethod.Get, $"{AssistanceUrl}?ara={probe}"));
            Assert.Empty(Records(page));
            Assert.DoesNotContain(customerPhone, raw);
        }
        // Görünür alanla arama çalışmaya devam eder (çit yalnız gizli alanlara).
        var (byMessage, _) = await Json(await Send(admin, HttpMethod.Get, $"{AssistanceUrl}?ara=Lastik"));
        Assert.Single(Records(byMessage));
    }

    [Fact]
    public async Task R295_P2a_assistance_clear_with_empty_string_is_not_refilled_from_anonymized_customer()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var hidden = await CreateViaApiAsync(admin, new()
        {
            ["tip"] = "Bireysel", ["ad"] = "Gizli", ["soyad"] = Marker(), ["cepTel"] = "05550000009",
            ["anonimAd"] = true, ["anonimTelefon"] = true,
        });
        var rental = await RentalAsync(e, hidden, "SubeA");
        var (id, version) = await PostAssistanceAsync(admin,
            new { rentalId = rental.RentalId, adSoyad = "Arayan Yakini", cepTel = "05441112299", mesaj = "Lastik" });

        await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{id}", new Dictionary<string, object?>
        { ["rentalId"] = rental.RentalId, ["adSoyad"] = "", ["cepTel"] = "", ["mesaj"] = "Lastik", ["surum"] = version }));

        Assert.Equal<(string?, string?)>((null, null), await StoredContactAsync(e.TenantId, id)); // temizlendi; müşteri kartından doldurulmadı
    }

    [Fact]
    public async Task R295_P2b_assistance_rental_change_keeps_hidden_caller_when_request_sends_null()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var hidden = await CreateViaApiAsync(admin, new()
        {
            ["tip"] = "Bireysel", ["ad"] = "Gizli", ["soyad"] = Marker(), ["cepTel"] = "05550000009",
            ["anonimAd"] = true, ["anonimTelefon"] = true,
        });
        var visible = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = Marker(), ["cepTel"] = "05550000008" });
        var rA = await RentalAsync(e, hidden, "SubeA");
        var rB = await RentalAsync(e, visible, "SubeA");
        var (id, version) = await PostAssistanceAsync(admin,
            new { rentalId = rA.RentalId, adSoyad = "Arayan Yakini", cepTel = "05441112299", mesaj = "Lastik" });

        var (_, raw) = await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{id}", new Dictionary<string, object?>
        { ["rentalId"] = rB.RentalId, ["adSoyad"] = null, ["cepTel"] = null, ["mesaj"] = "Lastik", ["surum"] = version }));

        Assert.DoesNotContain("05550000009", raw);
        Assert.Equal<(string?, string?)>(("Arayan Yakini", "05441112299"), await StoredContactAsync(e.TenantId, id));
    }

    [Fact]
    public async Task R295_P2c_assistance_does_not_copy_anonymized_customer_into_snapshot()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var hidden = await CreateViaApiAsync(admin, new()
        {
            ["tip"] = "Bireysel", ["ad"] = "Gizli", ["soyad"] = Marker(), ["cepTel"] = "05550000007",
            ["anonimAd"] = true, ["anonimTelefon"] = true,
        });
        var rental = await RentalAsync(e, hidden, "SubeA");
        var (id, _) = await PostAssistanceAsync(admin, new { rentalId = rental.RentalId, mesaj = "Akü" });
        Assert.Equal<(string?, string?)>((null, null), await StoredContactAsync(e.TenantId, id));
    }
}
