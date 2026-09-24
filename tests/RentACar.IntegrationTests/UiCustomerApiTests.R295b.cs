using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR #295 yeniden doğrulama (r295b) probe'larından kalıcı çit — beklenenler elle kurulmuş senaryodan:
/// H1 Servis'e çevirme + temizle-sonra-çevir; L-A biçimli eski TC gizli; M2 boşluklu TC tam eşleşir;
/// H (yeni) assistans bağ değişimi/kaldırılması gizli snapshot'ı açmaz; L-C temizlenen alan temiz kalır.
/// </summary>
public sealed partial class UiCustomerApiTests
{
    [Fact]
    public async Task R295b_H1_flip_to_servis_without_tax_is_400_and_clear_then_flip_ok()
    {
        var e = await SetupAsync();
        var opA = await LoginAsync(e, Who.OperatorA);
        const string tax = "9876543210";
        var (card, _) = await Json(await Send(opA, HttpMethod.Post, Customers,
            new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "V", ["vergiNo"] = tax }), HttpStatusCode.Created);
        var id = card.GetProperty("id").GetGuid();
        var raw400 = await Problem(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", new Dictionary<string, object?>
            { ["tip"] = "Servis", ["unvan"] = "S", ["surum"] = card.GetProperty("surum").GetString() }),
            HttpStatusCode.BadRequest, "dogrulama", "vergiNo");
        Assert.DoesNotContain(tax, raw400);

        var (c1, _) = await Json(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", new Dictionary<string, object?>
            { ["tip"] = "Bireysel", ["ad"] = "V", ["vergiNo"] = "", ["surum"] = card.GetProperty("surum").GetString() }));
        var (c2, raw) = await Json(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", new Dictionary<string, object?>
            { ["tip"] = "Kurumsal", ["unvan"] = "K", ["surum"] = c1.GetProperty("surum").GetString() }));
        Assert.DoesNotContain(tax, raw);
        Assert.Equal("Kurumsal", c2.GetProperty("tip").GetString());
        Assert.Null(await ReadAsync(e.TenantId, db => db.Customers.Where(c => c.Id == id).Select(c => c.VergiNo).SingleAsync()));
    }

    [Theory]
    [InlineData("SPACE")]
    [InlineData("DASH")]
    [InlineData("TRAIL")]
    public async Task R295b_LA_legacy_formatted_tc_on_corporate_is_masked_and_not_probeable(string variant)
    {
        var e = await SetupAsync();
        var opA = await LoginAsync(e, Who.OperatorA);
        var tc = RandomTc();
        var stored = variant switch
        {
            "SPACE" => $"{tc[..3]} {tc[3..6]} {tc[6..9]} {tc[9..]}",
            "DASH" => $"{tc[..3]}-{tc[3..]}",
            _ => tc + " ",
        };
        var legacy = new Customer { Tip = CariType.Kurumsal, Unvan = "Eski " + Marker(), VergiNo = stored };
        await WriteAsync(e.TenantId, db => db.Customers.Add(legacy));

        var (card, rawCard) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}/{legacy.Id}"));
        Assert.DoesNotContain(stored.Trim(), rawCard);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("vergiNo").ValueKind);
        var (list, rawList) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}?q={Uri.EscapeDataString(legacy.Unvan!)}"));
        var row = Assert.Single(Records(list));
        Assert.Equal(JsonValueKind.Null, row.GetProperty("vergiNo").ValueKind);
        Assert.DoesNotContain(stored.Trim(), rawList);
        var (search, _) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}?q={Uri.EscapeDataString(stored[..5])}"));
        Assert.Empty(Records(search));
    }

    [Fact]
    public async Task R295b_M2_spaced_tc_search_matches_exactly_on_server()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var tc = RandomTc();
        var id = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Aranan", ["tcKimlik"] = tc });
        var spaced = $"{tc[..3]} {tc[3..6]} {tc[6..9]} {tc[9..]}";
        var (r, raw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={Uri.EscapeDataString(spaced)}"));
        Assert.Contains(Records(r), x => x.GetProperty("id").GetGuid() == id); // blind-index tam eşleşme, rakamlarla
        Assert.DoesNotContain(tc, raw);
        var (partial, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={Uri.EscapeDataString(spaced[..7])}"));
        Assert.DoesNotContain(Records(partial), x => x.GetProperty("id").GetGuid() == id); // kısmi TC araması yok
    }
}
