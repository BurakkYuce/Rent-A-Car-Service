using System.Net;
using System.Text.Json;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

public sealed partial class UiCustomerApiTests
{
    private async Task<Guid> CustomerAsync(Env e, string surname, string? tc = null, bool anonymous = false)
    {
        var c = new Customer
        {
            Tip = CariType.Bireysel, Ad = "Can", Soyad = surname, CepTel = "05551234567", Email = "can@example.com",
            AnonimAd = anonymous, AnonimTelefon = anonymous, AnonimMail = anonymous,
        };
        await WriteAsync(e.TenantId, db => db.Customers.Add(c));
        if (tc is not null)
        {
            // TC API üzerinden yazılır (şifre + blind-index servis yolundan geçsin).
            var admin = await LoginAsync(e, Who.Admin);
            var (card, _) = await Json(await Send(admin, HttpMethod.Get, $"{Customers}/{c.Id}"));
            var body = new Dictionary<string, object?>
            {
                ["tip"] = "Bireysel", ["ad"] = anonymous ? null : "Can", ["soyad"] = anonymous ? null : surname,
                ["tcKimlik"] = tc, ["anonimAd"] = anonymous, ["anonimTelefon"] = anonymous, ["anonimMail"] = anonymous,
                ["surum"] = card.GetProperty("surum").GetString(),
            };
            await Json(await Send(admin, HttpMethod.Put, $"{Customers}/{c.Id}", body));
        }
        return c.Id;
    }

    [Fact]
    public async Task Complaint_scope_version_and_anonymized_customer()
    {
        var e = await SetupAsync();
        var hidden = await CustomerAsync(e, "Saklioglu", anonymous: true);
        var rentalB = await RentalAsync(e, hidden, "SubeB");
        var opA = await LoginAsync(e, Who.OperatorA);
        var opB = await LoginAsync(e, Who.OperatorB);
        const string url = V1 + "/sikayetler";

        // Operatör A başka şubenin kirasına şikayet bağlayamaz (403); B bağlar.
        var body = new Dictionary<string, object?>
        {
            ["cariId"] = hidden, ["konu"] = "Araç kirli", ["rentalId"] = rentalB.RentalId, ["puan"] = 2, ["sikayetKanali"] = "Telefon",
        };
        await Problem(await Send(opA, HttpMethod.Post, url, body), HttpStatusCode.Forbidden, "yetki_yok");
        var (created, raw) = await Json(await Send(opB, HttpMethod.Post, url, body), HttpStatusCode.Created);
        var s = created.GetProperty("sikayet");
        var id = s.GetProperty("id").GetGuid();
        Assert.Equal("Anonim müşteri", s.GetProperty("musteriAd").GetString());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("musteriTel").ValueKind);
        Assert.Equal(rentalB.Plate, s.GetProperty("plaka").GetString());
        Assert.Equal(rentalB.ContractNo, s.GetProperty("sozlesmeNo").GetString());
        Assert.DoesNotContain("Saklioglu", raw);

        // A: tekil 403 (içerik yok), listede görünmez; B listede görür.
        await Problem(await Send(opA, HttpMethod.Get, $"{url}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Delete, $"{url}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        var (listA, _) = await Json(await Send(opA, HttpMethod.Get, url));
        Assert.Empty(Records(listA));
        var (listB, listRawB) = await Json(await Send(opB, HttpMethod.Get, url));
        Assert.Single(Records(listB));
        Assert.DoesNotContain("Saklioglu", listRawB);

        // PUT: surum zorunlu, doğru surum 200, eski surum 409; puan sınırı 400.
        body["durum"] = "Cozuldu";
        await Problem(await Send(opB, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "surum");
        body["surum"] = created.GetProperty("surum").GetString();
        var (upd, _) = await Json(await Send(opB, HttpMethod.Put, $"{url}/{id}", body));
        Assert.Equal("Cozuldu", upd.GetProperty("sikayet").GetProperty("durum").GetString());
        await Problem(await Send(opB, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.Conflict, "cakisma");
        body["surum"] = upd.GetProperty("surum").GetString();
        body["puan"] = 9;
        await Problem(await Send(opB, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "puan");
        body["puan"] = 2;
        body["cariId"] = Guid.NewGuid();
        await Problem(await Send(opB, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "cariId");

        // Başka kiracı: 404.
        var other = await SetupAsync();
        var otherAdmin = await LoginAsync(other, Who.Admin);
        await Problem(await Send(otherAdmin, HttpMethod.Get, $"{url}/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherAdmin, HttpMethod.Delete, $"{url}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(opB, HttpMethod.Delete, $"{url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Survey_answers_version_and_scope()
    {
        var e = await SetupAsync();
        var cust = await CustomerAsync(e, "Anketli");
        var rentalA = await RentalAsync(e, cust, "SubeA");
        var opA = await LoginAsync(e, Who.OperatorA);
        var opB = await LoginAsync(e, Who.OperatorB);
        const string url = V1 + "/anketler";

        var (questions, _) = await Json(await Send(opA, HttpMethod.Get, $"{url}/varsayilan-sorular"));
        Assert.Equal(8, questions.GetArrayLength());

        var body = new Dictionary<string, object?>
        {
            ["cariId"] = cust, ["rentalId"] = rentalA.RentalId, ["puan"] = 8, ["anketTuru"] = "Donus",
            ["cevaplar"] = new[] { new { soruNo = 1, soru = "Temiz miydi?", cevap = "Evet", aciklama = (string?)null },
                                   new { soruNo = 2, soru = "Tekrar?", cevap = "Hayır", aciklama = (string?)"fiyat" } },
        };
        var (created, _) = await Json(await Send(opA, HttpMethod.Post, url, body), HttpStatusCode.Created);
        var id = created.GetProperty("anket").GetProperty("id").GetGuid();
        Assert.Equal("SubeA", created.GetProperty("anket").GetProperty("cikisOfisi").GetString()); // kiradan kopyalandı
        Assert.Equal(2, created.GetProperty("cevaplar").GetArrayLength());
        Assert.Equal("Can Anketli", created.GetProperty("anket").GetProperty("musteriAd").GetString());

        await Problem(await Send(opB, HttpMethod.Get, $"{url}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        var (listB, _) = await Json(await Send(opB, HttpMethod.Get, url));
        Assert.Empty(Records(listB));

        // Cevaplar TAMAMEN değişir; eski sürüm 409; puan 11 → 400.
        body["cevaplar"] = new[] { new { soruNo = 3, soru = "Personel?", cevap = "İyi", aciklama = (string?)null } };
        body["surum"] = created.GetProperty("surum").GetString();
        var (upd, _) = await Json(await Send(opA, HttpMethod.Put, $"{url}/{id}", body));
        var answer = Assert.Single(upd.GetProperty("cevaplar").EnumerateArray());
        Assert.Equal(3, answer.GetProperty("soruNo").GetInt32());
        await Problem(await Send(opA, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.Conflict, "cakisma");
        body["surum"] = upd.GetProperty("surum").GetString();
        body["puan"] = 11;
        await Problem(await Send(opA, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "puan");
        body["puan"] = 5;
        body["tarih"] = TestZaman.GunSonra(10);
        await Problem(await Send(opA, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.BadRequest, "dogrulama", "tarih");
    }

    [Fact]
    public async Task Assistance_fills_from_rental_and_hides_anonymized_snapshot()
    {
        var e = await SetupAsync();
        var hidden = await CustomerAsync(e, "Yoldaoglu", anonymous: true);
        var rentalA = await RentalAsync(e, hidden, "SubeA");
        var opA = await LoginAsync(e, Who.OperatorA);
        var opB = await LoginAsync(e, Who.OperatorB);
        const string url = V1 + "/assistans-talepleri";

        await Problem(await Send(opB, HttpMethod.Post, url, new { rentalId = rentalA.RentalId, mesaj = "Lastik patladı" }),
            HttpStatusCode.Forbidden, "yetki_yok");
        await Problem(await Send(opA, HttpMethod.Post, url, new { mesaj = "" }), HttpStatusCode.BadRequest, "dogrulama", "mesaj");
        var (created, raw) = await Json(await Send(opA, HttpMethod.Post, url,
            new { rentalId = rentalA.RentalId, mesaj = "Lastik patladı", yedekLastikMi = true }), HttpStatusCode.Created);
        var t = created.GetProperty("talep");
        Assert.Equal(rentalA.Plate, t.GetProperty("plaka").GetString()); // boş plaka kiradan
        Assert.Equal(JsonValueKind.Null, t.GetProperty("adSoyad").ValueKind);   // anonim müşterinin snapshot'ı gizli
        Assert.Equal(JsonValueKind.Null, t.GetProperty("cepTel").ValueKind);
        Assert.DoesNotContain("Yoldaoglu", raw);
        Assert.DoesNotContain("05551234567", raw);

        var id = t.GetProperty("id").GetGuid();
        await Problem(await Send(opB, HttpMethod.Get, $"{url}/{id}"), HttpStatusCode.Forbidden, "yetki_yok");
        var (list, listRaw) = await Json(await Send(opA, HttpMethod.Get, $"{url}?yedekLastik=true"));
        Assert.Single(Records(list));
        Assert.DoesNotContain("Yoldaoglu", listRaw);

        var upd = new Dictionary<string, object?>
        {
            ["rentalId"] = rentalA.RentalId, ["mesaj"] = "Çekici geldi", ["kapandi"] = true, ["surum"] = created.GetProperty("surum").GetString(),
        };
        var (u, _) = await Json(await Send(opA, HttpMethod.Put, $"{url}/{id}", upd));
        Assert.True(u.GetProperty("talep").GetProperty("kapandi").GetBoolean());
        await Problem(await Send(opA, HttpMethod.Put, $"{url}/{id}", upd), HttpStatusCode.Conflict, "cakisma");
    }

    [Fact]
    public async Task Legal_file_crud_version_and_isolation()
    {
        var e = await SetupAsync();
        var cust = await CustomerAsync(e, "Borclu");
        var opA = await LoginAsync(e, Who.OperatorA);
        var acc = await LoginAsync(e, Who.Accounting);
        const string url = V1 + "/hukuk-dosyalari";
        var no = "hk-" + Guid.NewGuid().ToString("N")[..6];

        await Problem(await Send(acc, HttpMethod.Get, url), HttpStatusCode.Forbidden, "yetki_yok"); // Blazor: OperationsWrite
        var body = new Dictionary<string, object?>
        {
            ["dosyaNo"] = no, ["cariId"] = cust, ["tur"] = "Icra", ["tutar"] = 1000m, ["tahsilat"] = 250m, ["avukat"] = "Av. Deniz",
        };
        var (created, _) = await Json(await Send(opA, HttpMethod.Post, url, body), HttpStatusCode.Created);
        var d = created.GetProperty("dosya");
        Assert.Equal(no.ToUpperInvariant(), d.GetProperty("dosyaNo").GetString());
        Assert.Equal(750m, d.GetProperty("kalan").GetDecimal()); // 1000 − 250
        Assert.Equal("Can Borclu", d.GetProperty("musteriAd").GetString());
        var id = d.GetProperty("id").GetGuid();

        await Problem(await Send(opA, HttpMethod.Post, url, body), HttpStatusCode.BadRequest, "dogrulama", "dosyaNo");
        body["tutar"] = -1m;
        body["dosyaNo"] = no + "x";
        await Problem(await Send(opA, HttpMethod.Post, url, body), HttpStatusCode.BadRequest, "dogrulama", "tutar");

        body["dosyaNo"] = no;
        body["tutar"] = 1200m;
        body["surum"] = created.GetProperty("surum").GetString();
        var (upd, _) = await Json(await Send(opA, HttpMethod.Put, $"{url}/{id}", body));
        Assert.Equal(950m, upd.GetProperty("dosya").GetProperty("kalan").GetDecimal()); // 1200 − 250
        await Problem(await Send(opA, HttpMethod.Put, $"{url}/{id}", body), HttpStatusCode.Conflict, "cakisma");

        var other = await SetupAsync();
        var otherOp = await LoginAsync(other, Who.OperatorA);
        await Problem(await Send(otherOp, HttpMethod.Get, $"{url}/{id}"), HttpStatusCode.NotFound, null);
        await Problem(await Send(otherOp, HttpMethod.Delete, $"{url}/{id}"), HttpStatusCode.NotFound, null);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(opA, HttpMethod.Delete, $"{url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Crm_analysis_and_rental_pick_respect_privacy_and_scope()
    {
        var e = await SetupAsync();
        var hidden = await CustomerAsync(e, "Raporoglu", anonymous: true);
        var visible = await CustomerAsync(e, "Acikoglu");
        var ra = await RentalAsync(e, hidden, "SubeA");
        var rb = await RentalAsync(e, visible, "SubeB");

        var admin = await LoginAsync(e, Who.Admin);
        var (analysis, raw) = await Json(await Send(admin, HttpMethod.Get, $"{V1}/crm/analiz?sirala=ad"));
        Assert.Equal(2, analysis.GetProperty("musteriSayisi").GetInt32());
        Assert.Equal(3000m, analysis.GetProperty("toplamCiro").GetDecimal()); // 2 kira × 1500
        Assert.DoesNotContain("Raporoglu", raw);
        Assert.Contains(Records(analysis.GetProperty("segment")), r => r.GetProperty("ad").GetString() == "Anonim müşteri");
        await Json(await Send(await LoginAsync(e, Who.Accounting), HttpMethod.Get, $"{V1}/crm/analiz/secenekler"));
        var opA = await LoginAsync(e, Who.OperatorA);
        await Problem(await Send(opA, HttpMethod.Get, $"{V1}/crm/analiz"), HttpStatusCode.Forbidden, "yetki_yok");

        // Kira seçimi: A yalnız kendi şubesinin kirasını görür; ad KVKK kuralıyla.
        var (pick, pickRaw) = await Json(await Send(opA, HttpMethod.Get, $"{V1}/crm/secim/kira"));
        var only = Assert.Single(pick.EnumerateArray());
        Assert.Equal(ra.RentalId, only.GetProperty("id").GetGuid());
        Assert.Equal("Anonim müşteri", only.GetProperty("musteriAd").GetString());
        Assert.DoesNotContain(rb.ContractNo, pickRaw);
        var (byPlate, _) = await Json(await Send(admin, HttpMethod.Get, $"{V1}/crm/secim/kira?q={rb.Plate[..5].ToLowerInvariant()}"));
        Assert.Contains(byPlate.EnumerateArray(), x => x.GetProperty("id").GetGuid() == rb.RentalId);
    }

    /// <summary>KVKK çiti: TC'si kayıtlı carinin görünebildiği HER F7 okuma yüzeyinde ham yanıt TC içermez.</summary>
    [Fact]
    public async Task Tc_is_absent_from_every_f7_response()
    {
        var e = await SetupAsync();
        var tc = RandomTc();
        var cust = await CustomerAsync(e, "Taramaoglu", tc);
        var rental = await RentalAsync(e, cust, "SubeA");
        var admin = await LoginAsync(e, Who.Admin);
        await Json(await Send(admin, HttpMethod.Post, V1 + "/sikayetler", new { cariId = cust, konu = "Test", rentalId = rental.RentalId }), HttpStatusCode.Created);
        await Json(await Send(admin, HttpMethod.Post, V1 + "/anketler", new { cariId = cust, puan = 7, rentalId = rental.RentalId }), HttpStatusCode.Created);
        await Json(await Send(admin, HttpMethod.Post, V1 + "/assistans-talepleri", new { rentalId = rental.RentalId, mesaj = "Akü" }), HttpStatusCode.Created);
        await Json(await Send(admin, HttpMethod.Post, V1 + "/hukuk-dosyalari", new { dosyaNo = "TR-" + Guid.NewGuid().ToString("N")[..6], cariId = cust, tutar = 10m }), HttpStatusCode.Created);

        string[] urls =
        [
            Customers, $"{Customers}?q={tc}", $"{Customers}/{cust}", $"{Customers}/{cust}/detay", $"{Customers}/secim/il",
            V1 + "/sikayetler", V1 + "/anketler", V1 + "/assistans-talepleri", V1 + "/hukuk-dosyalari", V1 + "/crm/analiz",
            V1 + "/crm/secim/kira", V1 + "/secim/musteri?q=Taramaoglu", $"{V1}/secim/musteri/{cust}",
        ];
        foreach (var u in urls)
        {
            var (_, body) = await Json(await Send(admin, HttpMethod.Get, u));
            Assert.False(body.Contains(tc, StringComparison.Ordinal), $"TC sızdı: {u}");
        }
    }
}
