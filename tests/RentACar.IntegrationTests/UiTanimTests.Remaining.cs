using System.Net;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.2c — the definitions that had no <c>/api/ui/v1</c> endpoint (payment types, fuel kinds, transmission types,
/// colours, reservation sources, account codes) on the generic contract. Expected values are hand-built literals.
/// </summary>
public sealed partial class UiTanimTests
{
    private static readonly Dictionary<string, DefinitionCase> RemainingCases = new()
    {
        ["odeme-tipleri"] = new("/odeme-tipleri", CodeName),
        ["yakit-turleri"] = new("/yakit-turleri", CodeName),
        ["vites-turleri"] = new("/vites-turleri", CodeName),
        ["renkler"] = new("/renkler", CodeName),
        ["rezervasyon-kaynaklari"] = new("/rezervasyon-kaynaklari",
            (k, a, ak, s) => new { kod = k, ad = a, tedarikci = "Broker A", kiraOrani = 12.5m, aktif = ak, surum = s }),
        ["hesap-kodlari"] = new("/hesap-kodlari",
            (k, a, ak, s) => new { kod = k, ad = a, aciklama = "Satışlar", aktif = ak, surum = s }, NameMax: 200),
    };

    public static TheoryData<string> RemainingCaseKeys => new(RemainingCases.Keys);

    [Theory]
    [MemberData(nameof(RemainingCaseKeys))]
    public Task Remaining_definition_crud_uniqueness_version_permission_and_isolation(string key)
        => RunDefinitionCaseAsync(RemainingCases[key]);

    [Fact]
    public async Task Reservation_source_fields_round_trip_and_limits_map_to_fields()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        var root = V1 + "/rezervasyon-kaynaklari";

        var created = await Json(await Send(op, HttpMethod.Post, root, new
        {
            kod = "web", ad = "Web Sitesi", tedarikci = "Rentalcars", kiraOrani = 12.5m, hizmetOrani = 3m, dropOrani = 0m,
            kaynakGrubu = "Broker", uzatamaz = true, kmSinirsiz = true, maxGun = 30, komisyonOrani = 10m,
            bebekKoltugu = 150.25m, mailAdres = "rez@ornek.test", gizle = true, sadeceMusteriOdeme = true,
        }), HttpStatusCode.Created);
        Assert.Equal("WEB", created.GetProperty("kod").GetString());
        Assert.Equal("Rentalcars", created.GetProperty("tedarikci").GetString());
        Assert.Equal(12.5m, created.GetProperty("kiraOrani").GetDecimal());
        Assert.Equal(3m, created.GetProperty("hizmetOrani").GetDecimal());
        Assert.Equal(0m, created.GetProperty("dropOrani").GetDecimal());
        Assert.Equal("Broker", created.GetProperty("kaynakGrubu").GetString());
        Assert.True(created.GetProperty("uzatamaz").GetBoolean());
        Assert.False(created.GetProperty("provizyonYok").GetBoolean());
        Assert.True(created.GetProperty("kmSinirsiz").GetBoolean());
        Assert.Equal(30, created.GetProperty("maxGun").GetInt32());
        Assert.Equal(10m, created.GetProperty("komisyonOrani").GetDecimal());
        Assert.Equal(150.25m, created.GetProperty("bebekKoltugu").GetDecimal());
        Assert.True(created.GetProperty("gizle").GetBoolean());
        Assert.True(created.GetProperty("sadeceMusteriOdeme").GetBoolean());

        // Full PUT clears what is not sent (kaynakGrubu null, maxGun null, flags false).
        var id = created.GetProperty("id").GetGuid();
        var put = await Json(await Send(op, HttpMethod.Put, $"{root}/{id}", new
        {
            kod = "WEB", ad = "Web", aktif = true, surum = created.GetProperty("surum").GetString(),
        }));
        Assert.Equal(JsonValueKindNull, put.GetProperty("kaynakGrubu").ValueKind);
        Assert.Equal(JsonValueKindNull, put.GetProperty("maxGun").ValueKind);
        Assert.False(put.GetProperty("uzatamaz").GetBoolean());

        // Business ranges (service) and column limits (edge) → 400 with the field.
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R1", ad = "Oran", kiraOrani = 100.01m }), HttpStatusCode.BadRequest, "dogrulama", "kiraOrani");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R2", ad = "Oran", komisyonOrani = -1m }), HttpStatusCode.BadRequest, "dogrulama", "komisyonOrani");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R3", ad = "Gün", maxGun = 0 }), HttpStatusCode.BadRequest, "dogrulama", "maxGun");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R4", ad = "Grup", kaynakGrubu = "Yok" }), HttpStatusCode.BadRequest, "dogrulama", "kaynakGrubu");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R5", ad = "Tutar", bebekKoltugu = 1_000_000_000_000_000m }), HttpStatusCode.BadRequest, "dogrulama", "bebekKoltugu");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R6", ad = "Metin", tedarikci = new string('t', 129) }), HttpStatusCode.BadRequest, "dogrulama", "tedarikci");
        await ExpectProblem(await Send(op, HttpMethod.Post, root, new { kod = "R7", ad = "Mail", mailAdres = new string('m', 257) }), HttpStatusCode.BadRequest, "dogrulama", "mailAdres");
        Assert.Single((await Json(await Send(op, HttpMethod.Get, root))).EnumerateArray());
    }

    [Fact]
    public async Task Reservation_source_reflect_copies_rates_only_to_other_active_sources()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        var root = V1 + "/rezervasyon-kaynaklari";
        var src = (await Json(await Send(op, HttpMethod.Post, root, new { kod = "S1", ad = "Kaynak", kiraOrani = 7.5m, hizmetOrani = 2m, dropOrani = 1m }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var active = (await Json(await Send(op, HttpMethod.Post, root, new { kod = "S2", ad = "Aktif" }), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var passive = (await Json(await Send(op, HttpMethod.Post, root, new { kod = "S3", ad = "Pasif", aktif = false }), HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var result = await Json(await Send(op, HttpMethod.Post, $"{root}/{src}/yansit"));
        Assert.Equal(1, result.GetProperty("guncellenen").GetInt32());
        var a = await Json(await Send(op, HttpMethod.Get, $"{root}/{active}"));
        Assert.Equal(7.5m, a.GetProperty("kiraOrani").GetDecimal());
        Assert.Equal(2m, a.GetProperty("hizmetOrani").GetDecimal());
        Assert.Equal(1m, a.GetProperty("dropOrani").GetDecimal());
        var p = await Json(await Send(op, HttpMethod.Get, $"{root}/{passive}"));
        Assert.Equal(JsonValueKindNull, p.GetProperty("kiraOrani").ValueKind);
    }

    [Fact]
    public async Task Account_code_duplicate_on_update_is_field_error_and_stale_version_is_conflict()
    {
        var env = await SetUpAsync();
        var op = await LoginAsync(env, Who.OperatorA);
        var root = V1 + "/hesap-kodlari";
        var a = await Json(await Send(op, HttpMethod.Post, root, new { kod = "600", ad = "Yurtiçi Satışlar" }), HttpStatusCode.Created);
        await Json(await Send(op, HttpMethod.Post, root, new { kod = "770", ad = "Genel Yönetim Giderleri" }), HttpStatusCode.Created);
        var id = a.GetProperty("id").GetGuid();
        var v1 = a.GetProperty("surum").GetString();

        // Duplicate code on a versioned update: the DB unique index (no service pre-check) → 400 kod, not 500.
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{root}/{id}", new { kod = "770", ad = "X", aktif = true, surum = v1 }), HttpStatusCode.BadRequest, "dogrulama", "kod");
        var ok = await Json(await Send(op, HttpMethod.Put, $"{root}/{id}", new { kod = "600", ad = "Satışlar", aciklama = "  ", aktif = false, surum = v1 }));
        Assert.Equal("Satışlar", ok.GetProperty("ad").GetString());
        Assert.Equal(JsonValueKindNull, ok.GetProperty("aciklama").ValueKind);
        await ExpectProblem(await Send(op, HttpMethod.Put, $"{root}/{id}", new { kod = "600", ad = "Bayat", aktif = true, surum = v1 }), HttpStatusCode.Conflict, "cakisma");
    }
}
