using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// F11.2d — personel yazma-yalnız PII kuralı (TC: null = koru, "" = sil; maaş: <c>maasTemizle</c>) ve Veri İçe Aktar
/// uçları. Beklenenler elle kurulmuş dosya/senaryodan.
/// </summary>
public sealed partial class UiSystemDefinitionsTests
{
    [Fact]
    public async Task Personnel_tc_is_write_only_blank_keeps_empty_string_clears_and_salary_clear_flag()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        const string path = V1 + "/personel";
        const string tc = "10000000146";

        var created = await Json(await Send(admin, HttpMethod.Post, path,
            new { kod = "T1", ad = "Ayşe", soyad = "Demir", tcKimlik = tc, maas = 30000m }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        // null TC + null maaş → ikisi de korunur.
        var kept = await Json(await Send(admin, HttpMethod.Put, $"{path}/{id}",
            new { kod = "T1", ad = "Ayşe", soyad = "Demir", surum = Surum(created) }));
        Assert.True(kept.GetProperty("tcKimlikTanimli").GetBoolean());
        Assert.Equal(30000m, kept.GetProperty("maas").GetDecimal());

        // "" TC → silinir; maasTemizle → maaş silinir; yanıtta TC hiçbir biçimde yok.
        var clearResp = await Send(admin, HttpMethod.Put, $"{path}/{id}",
            new { kod = "T1", ad = "Ayşe", soyad = "Demir", tcKimlik = "", maasTemizle = true, surum = Surum(kept) });
        var clearText = await clearResp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, clearResp.StatusCode);
        Assert.DoesNotContain(tc, clearText);
        var cleared = JsonDocument.Parse(clearText).RootElement;
        Assert.False(cleared.GetProperty("tcKimlikTanimli").GetBoolean());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("maas").ValueKind);
        var row = await _kit.ReadAsync(e.TenantId, db => db.Personeller.Where(p => p.Id == id)
            .Select(p => new { p.TcKimlikEnc, p.MaasEnc }).SingleAsync());
        Assert.Null(row.TcKimlikEnc);
        Assert.Null(row.MaasEnc);

        // Dolu değer silme bayrağına üstün gelir (yazılan değer kaybolmaz).
        var again = await Json(await Send(admin, HttpMethod.Put, $"{path}/{id}",
            new { kod = "T1", ad = "Ayşe", soyad = "Demir", tcKimlik = tc, maas = 32500.5m, maasTemizle = true, surum = Surum(cleared) }));
        Assert.True(again.GetProperty("tcKimlikTanimli").GetBoolean());
        Assert.Equal(32500.5m, again.GetProperty("maas").GetDecimal());
    }

    private static MultipartFormDataContent CsvFile(string csv, string name = "liste.csv")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "dosya", name);
        return form;
    }

    [Fact]
    public async Task Import_vehicles_and_customers_counts_skip_duplicates_and_error_summary_has_no_names()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var plate = "34TST" + System.Random.Shared.Next(100, 999);

        // Araç: 2 satır aynı plaka → 1 eklenen, 1 atlanan.
        var vehicles = await Json(await Send(admin, HttpMethod.Post, V1 + "/ice-aktar/arac",
            CsvFile($"Plaka;Marka;Renk\n{plate};Fiat;Beyaz\n{plate};Fiat;Beyaz\n")));
        Assert.Equal(1, vehicles.GetProperty("eklenen").GetInt32());
        Assert.Equal(1, vehicles.GetProperty("atlanan").GetInt32());
        Assert.Equal(0, vehicles.GetProperty("hatali").GetInt32());
        Assert.Equal(1, await _kit.ReadAsync(e.TenantId, db => db.Vehicles.CountAsync(v => v.Plaka == plate)));

        // Cari: 1 geçerli (TC şifreli girer), aynı TC tekrar → atlanan, 2 bozuk e-posta → özet (ad YOK).
        const string tc = "10000000146";
        var csv = "Ad;Soyad;TC Kimlik;E-posta\n"
                  + $"Deniz;Yıldız;{tc};deniz@ornek.test\n"
                  + $"Deniz;Yıldız;{tc};deniz@ornek.test\n"
                  + "Gizlikisi;Birinci;;bozuk-eposta\n"
                  + "Gizlikisi;Ikinci;;bozuk-eposta\n";
        var resp = await Send(admin, HttpMethod.Post, V1 + "/ice-aktar/cari", CsvFile(csv));
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.DoesNotContain("Gizlikisi", text);
        Assert.DoesNotContain(tc, text);
        var customers = JsonDocument.Parse(text).RootElement;
        Assert.Equal(1, customers.GetProperty("eklenen").GetInt32());
        Assert.Equal(1, customers.GetProperty("atlanan").GetInt32());
        Assert.Equal(2, customers.GetProperty("hatali").GetInt32());
        var summary = customers.GetProperty("hataOzeti").EnumerateArray().Single();
        Assert.Equal("E-posta adresi geçersiz.", summary.GetProperty("mesaj").GetString());
        Assert.Equal(2, summary.GetProperty("adet").GetInt32());
        var stored = await _kit.ReadAsync(e.TenantId, db => db.Customers.Where(c => c.Ad == "Deniz")
            .Select(c => new { c.TcKimlik, c.TcKimlikEnc }).ToListAsync());
        var only = Assert.Single(stored);
        Assert.Null(only.TcKimlik); // düz metin kolon yazılmaz
        Assert.NotNull(only.TcKimlikEnc);
        Assert.DoesNotContain(tc, only.TcKimlikEnc!);

        // Dosya denetimleri → 400 errors[dosya].
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/ice-aktar/arac", CsvFile("")),
            HttpStatusCode.BadRequest, "dogrulama", "dosya");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(admin, HttpMethod.Post, V1 + "/ice-aktar/arac", new MultipartFormDataContent())).StatusCode);
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/ice-aktar/cari", CsvFile("Ad\nX\n", "liste.pdf")),
            HttpStatusCode.BadRequest, "dogrulama", "dosya");
        await Problem(await Send(admin, HttpMethod.Post, V1 + "/ice-aktar/arac", CsvFile("bozuk", "liste.xlsx")),
            HttpStatusCode.BadRequest, "dogrulama", "dosya");

        // ManageUsers olmayan roller 403 ve hiçbir şey yazılmaz.
        foreach (var who in new[] { Who.Manager, Who.OperatorA, Who.Accounting })
        {
            var s = await _kit.LoginAsync(e, who);
            await Problem(await Send(s, HttpMethod.Post, V1 + "/ice-aktar/arac", CsvFile("Plaka\n06YTK001\n")),
                HttpStatusCode.Forbidden, "yetki_yok");
        }
        Assert.Equal(0, await _kit.ReadAsync(e.TenantId, db => db.Vehicles.CountAsync(v => v.Plaka == "06YTK001")));

        // CSRF başlığı olmadan istek reddedilir (grup filtresi; form bağlamanın kendi denetimi kapalı).
        var noXsrf = new HttpRequestMessage(HttpMethod.Post, V1 + "/ice-aktar/arac") { Content = CsvFile("Plaka\n06XSR001\n") };
        var refused = await admin.C.SendAsync(noXsrf);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(0, await _kit.ReadAsync(e.TenantId, db => db.Vehicles.CountAsync(v => v.Plaka == "06XSR001")));

        // Kiracı yalıtımı: içe aktarılan araç başka kiracıda görünmez (racar_app + RLS).
        var other = await _kit.SetupAsync();
        Assert.Equal(0, await _kit.ReadAsync(other.TenantId, db => db.Vehicles.CountAsync(v => v.Plaka == plate)));
    }
}
