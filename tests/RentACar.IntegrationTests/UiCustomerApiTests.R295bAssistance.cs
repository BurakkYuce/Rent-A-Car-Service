using System.Net;
using System.Text.Json;

namespace RentACar.IntegrationTests;

/// <summary>
/// #295b assistans gizli snapshot kuralı: değer yalnız BAĞLI kiranın müşterisi anonim olduğu için gizli. Bağ değişince
/// ya da kaldırılınca saklı değer SİLİNİR (düz görünmez, ManageUsers kapısı atlanmaz); aynı kirada <c>null</c> korur,
/// <c>""</c> temizler ve temizlenen alan sözleşmeden yeniden dolmaz (L-C).
/// </summary>
public sealed partial class UiCustomerApiTests
{
    private sealed record AnonymisedCase(Guid TicketId, string? Version, string Secret, string Phone, Guid OtherRental, string OtherName);

    /// <summary>Müşteri AÇIKken talep açılır (snapshot gerçek ad/telefonu kopyalar), sonra müşteri anonimleştirilir.</summary>
    private async Task<AnonymisedCase> AnonymisedTicketAsync(Env e, Session admin, Session op)
    {
        var secret = Marker();
        var phone = "0532" + Random.Shared.Next(1_000_000, 9_999_999);
        var (cust, _) = await Json(await Send(admin, HttpMethod.Post, Customers, new Dictionary<string, object?>
            { ["tip"] = "Bireysel", ["ad"] = "Gercek", ["soyad"] = secret, ["cepTel"] = phone }), HttpStatusCode.Created);
        var custId = cust.GetProperty("id").GetGuid();
        var otherName = "Baska" + Marker();
        var other = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = otherName, ["cepTel"] = "05550000031" });
        var rA = await RentalAsync(e, custId, "SubeA");
        var rB = await RentalAsync(e, other, "SubeA");
        var (t, _) = await Json(await Send(op, HttpMethod.Post, AssistanceUrl, new { rentalId = rA.RentalId, mesaj = "Akü" }), HttpStatusCode.Created);
        var tid = t.GetProperty("talep").GetProperty("id").GetGuid();
        Assert.Equal<(string?, string?)>(($"Gercek {secret}", phone), await StoredContactAsync(e.TenantId, tid));
        await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{custId}", new Dictionary<string, object?>
        {
            ["tip"] = "Bireysel", ["ad"] = "Gercek", ["soyad"] = secret, ["cepTel"] = phone,
            ["anonimAd"] = true, ["anonimTelefon"] = true, ["surum"] = cust.GetProperty("surum").GetString(),
        }));
        var (hidden, rawH) = await Json(await Send(op, HttpMethod.Get, $"{AssistanceUrl}/{tid}"));
        Assert.DoesNotContain(secret, rawH);
        Assert.DoesNotContain(phone, rawH);
        return new(tid, hidden.GetProperty("surum").GetString(), secret, phone, rB.RentalId, otherName);
    }

    private static Dictionary<string, object?> NullContactPut(Guid? rentalId, string? version) => new()
    { ["rentalId"] = rentalId, ["adSoyad"] = null, ["cepTel"] = null, ["mesaj"] = "Akü", ["surum"] = version };

    [Fact]
    public async Task R295b_H_assistance_relink_drops_anonymised_snapshot()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        var c = await AnonymisedTicketAsync(e, admin, opA);

        var (relinked, raw) = await Json(await Send(opA, HttpMethod.Put, $"{AssistanceUrl}/{c.TicketId}",
            NullContactPut(c.OtherRental, c.Version)));
        Assert.DoesNotContain(c.Secret, raw);
        Assert.DoesNotContain(c.Phone, raw);
        // Eski gizli değer taşınmadı; yeni kiranın GÖRÜNÜR müşterisinden doldu (kabul).
        Assert.Equal(c.OtherName, relinked.GetProperty("talep").GetProperty("adSoyad").GetString());
        Assert.Equal<(string?, string?)>((c.OtherName, "05550000031"), await StoredContactAsync(e.TenantId, c.TicketId));
    }

    [Fact]
    public async Task R295b_H_assistance_unlink_drops_anonymised_snapshot()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        var c = await AnonymisedTicketAsync(e, admin, opA);

        // Bağı kaldırmak (şubesiz kayıt) yalnız kapsamsız kullanıcıya açık (#295 L3); snapshot kuralı aynı.
        var (unlinked, raw) = await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{c.TicketId}",
            NullContactPut(null, c.Version)));
        Assert.DoesNotContain(c.Secret, raw);
        Assert.DoesNotContain(c.Phone, raw);
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("talep").GetProperty("adSoyad").ValueKind);
        Assert.Equal<(string?, string?)>((null, null), await StoredContactAsync(e.TenantId, c.TicketId)); // kayıttan silindi
    }

    [Fact]
    public async Task R295b_H_assistance_unlink_writes_new_user_value()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        var c = await AnonymisedTicketAsync(e, admin, opA);
        var put = NullContactPut(null, c.Version);
        put["adSoyad"] = "Yeni Arayan";
        // Şube kapsamlı operatör bağı kaldıramaz (#295 L3) → kapsamsız kullanıcı.
        await Problem(await Send(opA, HttpMethod.Put, $"{AssistanceUrl}/{c.TicketId}", put), HttpStatusCode.Forbidden, "yetki_yok");
        await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{c.TicketId}", put));
        Assert.Equal<(string?, string?)>(("Yeni Arayan", null), await StoredContactAsync(e.TenantId, c.TicketId));
    }

    [Fact]
    public async Task R295b_LC_assistance_clear_stays_clear_and_anon_not_copied()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var visible = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Acik" + Marker(), ["cepTel"] = "05550000011" });
        var anon = await CreateViaApiAsync(admin, new()
            { ["tip"] = "Bireysel", ["ad"] = "Gizli", ["cepTel"] = "05550000012", ["anonimAd"] = true, ["anonimTelefon"] = true });
        var rV = await RentalAsync(e, visible, "SubeA");
        var rN = await RentalAsync(e, anon, "SubeA");

        var (nId, _) = await PostAssistanceAsync(admin, new { rentalId = rN.RentalId, mesaj = "x" });
        Assert.Equal<(string?, string?)>((null, null), await StoredContactAsync(e.TenantId, nId)); // anonim kopyalanmaz

        var (vId, v0) = await PostAssistanceAsync(admin, new { rentalId = rV.RentalId, mesaj = "x" });
        Assert.Equal("05550000011", (await StoredContactAsync(e.TenantId, vId)).CepTel); // oluşturmada doldurulur
        var (c1, _) = await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{vId}", new Dictionary<string, object?>
            { ["rentalId"] = rV.RentalId, ["adSoyad"] = "", ["cepTel"] = "", ["mesaj"] = "x", ["surum"] = v0 }));
        Assert.Equal<(string?, string?)>((null, null), await StoredContactAsync(e.TenantId, vId));
        // Aynı kirada null = dokunma: temizlenen alan sözleşmeden yeniden DOLMAZ.
        await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{vId}", new Dictionary<string, object?>
            { ["rentalId"] = rV.RentalId, ["adSoyad"] = null, ["cepTel"] = null, ["mesaj"] = "y", ["surum"] = c1.GetProperty("surum").GetString() }));
        Assert.Equal<(string?, string?)>((null, null), await StoredContactAsync(e.TenantId, vId));
    }

    [Fact]
    public async Task R295b_LC_assistance_same_rental_null_keeps_visible_value()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var visible = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Acik", ["cepTel"] = "05550000021" });
        var rV = await RentalAsync(e, visible, "SubeA");
        var (id, v0) = await PostAssistanceAsync(admin,
            new { rentalId = rV.RentalId, adSoyad = "Arayan", cepTel = "05441110000", mesaj = "x" });
        await Json(await Send(admin, HttpMethod.Put, $"{AssistanceUrl}/{id}", new Dictionary<string, object?>
            { ["rentalId"] = rV.RentalId, ["adSoyad"] = null, ["cepTel"] = null, ["mesaj"] = "y", ["surum"] = v0 }));
        Assert.Equal<(string?, string?)>(("Arayan", "05441110000"), await StoredContactAsync(e.TenantId, id));
    }
}
