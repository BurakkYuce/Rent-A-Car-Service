using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace RentACar.IntegrationTests;

/// <summary>#283 bağımsız KVKK incelemesi M1–M4 kalıcı çitleri (beklenenler elle kurulmuş senaryodan).</summary>
public sealed partial class UiCustomerApiTests
{
    private static string Marker() => "Q" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    private async Task<Guid> CreateViaApiAsync(Session s, Dictionary<string, object?> body)
        => (await Json(await Send(s, HttpMethod.Post, Customers, body), HttpStatusCode.Created)).Json.GetProperty("id").GetGuid();

    [Fact]
    public async Task M1_anonymized_name_cannot_be_probed_by_search_or_sort()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var secret = Marker();
        var hiddenA = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Aaron", ["soyad"] = secret, ["anonimAd"] = true, ["anonimTelefon"] = true });
        var hiddenZ = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Zeki", ["soyad"] = secret + "Z", ["anonimAd"] = true });
        var visible = await CreateViaApiAsync(admin, new() { ["tip"] = "Bireysel", ["ad"] = "Bora", ["soyad"] = "Kaya" });

        // Arama: gerçek soyadın öneki 0 kayıt; yalnız görünen etiket bulur.
        var (prefix, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={secret[..6]}"));
        Assert.Empty(Records(prefix));
        var (byLabel, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q=Anonim"));
        Assert.Equal(new[] { hiddenA, hiddenZ }.OrderBy(x => x), Records(byLabel).Select(r => r.GetProperty("id").GetGuid()).OrderBy(x => x));

        // Sıralama: gerçek ada göre (Aaron < Bora < Zeki) DEĞİL, etikete göre — iki anonim yan yana, Bora sonda.
        var (byName, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?sirala=ad"));
        var ids = Records(byName).Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(visible, ids[2]);
        Assert.Equal(new[] { hiddenA, hiddenZ }.OrderBy(x => x), ids.Take(2).OrderBy(x => x));
        var (bySurname, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?sirala=soyad"));
        Assert.Equal(visible, Records(bySurname).First().GetProperty("id").GetGuid()); // anonim soyad boş sayılır (sonda)
        var (byDefault, _) = await Json(await Send(admin, HttpMethod.Get, Customers));
        Assert.Equal(visible, Records(byDefault).Last().GetProperty("id").GetGuid());

        // Assistans araması: anonim müşterinin kirasından gelen ad/telefon snapshot'ı eşleşmez.
        var rental = await RentalAsync(e, hiddenA, "SubeA");
        var body = new { rentalId = rental.RentalId, adSoyad = "Aaron " + secret, cepTel = "05559990011", mesaj = "Akü bitti" };
        await Json(await Send(admin, HttpMethod.Post, V1 + "/assistans-talepleri", body), HttpStatusCode.Created);
        foreach (var probe in new[] { secret, "0555999" })
        {
            var (hit, _) = await Json(await Send(admin, HttpMethod.Get, $"{V1}/assistans-talepleri?ara={probe}"));
            Assert.Empty(Records(hit));
        }
        var (msg, _) = await Json(await Send(admin, HttpMethod.Get, $"{V1}/assistans-talepleri?ara=Akü"));
        Assert.Single(Records(msg));
    }

    [Fact]
    public async Task M2_secondary_fields_of_anonymized_groups_hidden_and_preserved()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var secrets = new Dictionary<string, string>
        {
            ["gsm2"] = "05321000001", ["tel2"] = "02161000002", ["isTelefonu"] = "02121000003",
            ["faturaUnvan"] = Marker(), ["faturaKiralayanIsim"] = Marker(), ["babaAdi"] = Marker(), ["anaAdi"] = Marker(),
            ["ekAdres"] = Marker(), ["faturaAdresi"] = Marker(), ["isAdresi"] = Marker(), ["kayitliIl"] = Marker(),
            ["kayitliIlce"] = Marker(), ["mahalleKoy"] = Marker(), ["seriNo"] = "S" + Marker()[..8], ["ciltNo"] = "C" + Marker()[..8],
            ["aileSira"] = "A" + Marker()[..8], ["siraNo"] = "N" + Marker()[..8], ["dogumYeri"] = Marker(),
        };
        var body = new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Nur", ["soyad"] = Marker() };
        foreach (var (k, v) in secrets) body[k] = v;
        foreach (var flag in new[] { "anonimAd", "anonimTc", "anonimTelefon", "anonimMail", "anonimAdres", "anonimBelge" }) body[flag] = true;
        var id = await CreateViaApiAsync(admin, body);

        var (card, raw) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{id}"));
        foreach (var (k, v) in secrets)
        {
            Assert.Equal(JsonValueKind.Null, card.GetProperty(k).ValueKind);
            Assert.DoesNotContain(v, raw);
        }

        // Kart olduğu gibi geri gönderilir (gizli alanlar null) → DB'de değerler aynen kalır.
        var put = new HttpRequestMessage(HttpMethod.Put, $"{Customers}/{id}") { Content = JsonContent.Create(card) };
        put.Headers.Add("X-XSRF-TOKEN", admin.Xsrf);
        await Json(await admin.C.SendAsync(put));
        var c = await ReadAsync(e.TenantId, db => db.Customers.AsNoTracking().SingleAsync(x => x.Id == id));
        var stored = new Dictionary<string, string?>
        {
            ["gsm2"] = c.Gsm2, ["tel2"] = c.Tel2, ["isTelefonu"] = c.IsTelefonu, ["faturaUnvan"] = c.FaturaUnvan,
            ["faturaKiralayanIsim"] = c.FaturaKiralayanIsim, ["babaAdi"] = c.BabaAdi, ["anaAdi"] = c.AnaAdi, ["ekAdres"] = c.EkAdres,
            ["faturaAdresi"] = c.FaturaAdresi, ["isAdresi"] = c.IsAdresi, ["kayitliIl"] = c.KayitliIl, ["kayitliIlce"] = c.KayitliIlce,
            ["mahalleKoy"] = c.MahalleKoy, ["seriNo"] = c.SeriNo, ["ciltNo"] = c.CiltNo, ["aileSira"] = c.AileSira,
            ["siraNo"] = c.SiraNo, ["dogumYeri"] = c.DogumYeri,
        };
        foreach (var (k, v) in secrets) Assert.Equal(v, stored[k]);
    }

    [Fact]
    public async Task M3_individual_tax_number_is_validated_masked_and_not_searchable()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var nationalId = RandomNationalId();
        await Problem(await Send(admin, HttpMethod.Post, Customers, new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Vedat", ["vergiNo"] = nationalId }),
            HttpStatusCode.BadRequest, "dogrulama", "vergiNo");
        await Problem(await Send(admin, HttpMethod.Post, Customers, new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Vedat", ["vergiNo"] = "12345" }),
            HttpStatusCode.BadRequest, "dogrulama", "vergiNo");

        var tax = "9" + Random.Shared.Next(100_000_000, 999_999_999);
        var (card, raw) = await Json(await Send(admin, HttpMethod.Post, Customers,
            new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Vedat", ["vergiNo"] = tax }), HttpStatusCode.Created);
        var id = card.GetProperty("id").GetGuid();
        Assert.DoesNotContain(tax, raw);
        Assert.Equal(JsonValueKind.Null, card.GetProperty("vergiNo").ValueKind);
        Assert.Equal("******" + tax[6..], card.GetProperty("vergiNoMaske").GetString());
        var (search, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={tax[..8]}"));
        Assert.Empty(Records(search));

        // Kart geri gönderilir (vergiNo null) → korunur; kurumsal caride vergi no araması çalışmaya devam eder.
        await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{id}",
            new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Vedat", ["surum"] = card.GetProperty("surum").GetString() }));
        Assert.Equal(tax, await ReadAsync(e.TenantId, db => db.Customers.Where(c => c.Id == id).Select(c => c.VergiNo).SingleAsync()));
        var corpTax = "8" + Random.Shared.Next(100_000_000, 999_999_999);
        var corp = await CreateViaApiAsync(admin, new() { ["tip"] = "Kurumsal", ["unvan"] = "Firma " + Marker(), ["vergiNo"] = corpTax });
        var (corpSearch, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}?q={corpTax[..8]}"));
        Assert.Equal(corp, Assert.Single(Records(corpSearch)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task M4_lifting_anonymization_requires_manage_users_and_is_audited()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var opA = await LoginAsync(e, Who.OperatorA);
        var secret = Marker();
        var (card, _) = await Json(await Send(admin, HttpMethod.Post, Customers,
            new Dictionary<string, object?> { ["tip"] = "Bireysel", ["ad"] = "Selin", ["soyad"] = secret, ["anonimAd"] = true }), HttpStatusCode.Created);
        var id = card.GetProperty("id").GetGuid();
        var version = card.GetProperty("surum").GetString();

        // Operatör kaldıramaz: 403, alan hatası yok, kayıt ve sürüm değişmez.
        var lift = new Dictionary<string, object?> { ["tip"] = "Bireysel", ["anonimAd"] = false, ["surum"] = version };
        var text = await Problem(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", lift), HttpStatusCode.Forbidden, "yetki_yok");
        Assert.False(JsonDocument.Parse(text).RootElement.TryGetProperty("errors", out _));
        Assert.True(await ReadAsync(e.TenantId, db => db.Customers.Where(c => c.Id == id).Select(c => c.AnonimAd).SingleAsync()));
        var (same, _) = await Json(await Send(opA, HttpMethod.Get, $"{Customers}/{id}"));
        Assert.Equal(version, same.GetProperty("surum").GetString());

        // Operatör anonimleştirme EKLEYEBİLİR (false → true).
        var add = new Dictionary<string, object?> { ["tip"] = "Bireysel", ["anonimAd"] = true, ["anonimTelefon"] = true, ["surum"] = version };
        var (added, _) = await Json(await Send(opA, HttpMethod.Put, $"{Customers}/{id}", add));
        Assert.True(added.GetProperty("anonimTelefon").GetBoolean());

        // Admin (ManageUsers) kaldırır → gerçek ad görünür; değişiklik denetim kaydında.
        var liftAdmin = new Dictionary<string, object?>
        { ["tip"] = "Bireysel", ["anonimAd"] = false, ["anonimTelefon"] = true, ["surum"] = added.GetProperty("surum").GetString() };
        var (lifted, _) = await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{id}", liftAdmin));
        Assert.Equal(secret, lifted.GetProperty("soyad").GetString());
        var key = id.ToString();
        var rows = await ReadAsync(e.TenantId, db => db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == "Customers" && a.EntityId == key)
            .Select(a => new { a.OldValues, a.NewValues }).ToListAsync());
        static bool? Flag(string? json)
            => json is not null && JsonDocument.Parse(json).RootElement.TryGetProperty("AnonimAd", out var v)
               && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        Assert.Contains(rows, r => Flag(r.OldValues) == true && Flag(r.NewValues) == false);
    }

    [Fact]
    public async Task M5_assistance_full_put_keeps_hidden_caller_contact()
    {
        var e = await SetupAsync();
        var admin = await LoginAsync(e, Who.Admin);
        var hidden = await CreateViaApiAsync(admin, new()
        {
            ["tip"] = "Bireysel", ["ad"] = "Gizli", ["soyad"] = Marker(), ["cepTel"] = "05550000001",
            ["anonimAd"] = true, ["anonimTelefon"] = true,
        });
        var rental = await RentalAsync(e, hidden, "SubeA");
        const string url = V1 + "/assistans-talepleri";
        const string caller = "Arayan Kisi";
        const string callerPhone = "05441112233";

        var (created, _) = await Json(await Send(admin, HttpMethod.Post, url,
            new { rentalId = rental.RentalId, adSoyad = caller, cepTel = callerPhone, mesaj = "Akü bitti" }), HttpStatusCode.Created);
        var t = created.GetProperty("talep");
        Assert.Equal(JsonValueKind.Null, t.GetProperty("adSoyad").ValueKind);
        var id = t.GetProperty("id").GetGuid();

        // İstemci kartı olduğu gibi geri gönderir: gizli alanlar null.
        var put = new Dictionary<string, object?>
        {
            ["rentalId"] = rental.RentalId, ["adSoyad"] = null, ["cepTel"] = null, ["mesaj"] = "Çekici geldi",
            ["kapandi"] = true, ["surum"] = created.GetProperty("surum").GetString(),
        };
        var (updated, raw) = await Json(await Send(admin, HttpMethod.Put, $"{url}/{id}", put));
        Assert.True(updated.GetProperty("talep").GetProperty("kapandi").GetBoolean());
        Assert.DoesNotContain(callerPhone, raw);

        var stored = await ReadAsync(e.TenantId, db => db.AssistansTalepleri.AsNoTracking()
            .Where(a => a.Id == id).Select(a => new { a.AdSoyad, a.CepTel, a.Mesaj }).SingleAsync());
        Assert.Equal(caller, stored.AdSoyad);          // silinmedi, müşteri kartından da yeniden doldurulmadı
        Assert.Equal(callerPhone, stored.CepTel);
        Assert.Equal("Çekici geldi", stored.Mesaj);
    }
}
