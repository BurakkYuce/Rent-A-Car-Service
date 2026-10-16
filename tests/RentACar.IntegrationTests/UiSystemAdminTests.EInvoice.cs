using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using static RentACar.IntegrationTests.SystemApiTestKit;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kiracı başına e-belge entegratör ayarları (<c>/api/ui/v1/ayarlar</c>): şifre/API anahtarı YALNIZ YAZMA (yanıtta yok,
/// DB'de şifreli), entegratör yalnız KAYITLI adaptör olabilir (bu sürümde hiç yok → seçenek listesi boş, durum "bağlı
/// değil"), etiket ve senaryo biçimi alana bağlı 400. Beklenen cümleler elle yazıldı.
/// </summary>
public sealed partial class UiSystemAdminTests
{
    private static object EInvoiceBody(string? version, string? secret = null, string? integrator = null, string? senderAlias = null,
        string? scenario = null)
        => new
        {
            firmaUnvan = "Deneme Oto Kiralama A.Ş.", firmaVergiNo = "1234567890", firmaIl = "İstanbul", firmaIlce = "Kadıköy",
            eFaturaKullanici = "entegrator-kullanici", eFaturaSifre = secret, eFaturaEntegrator = integrator,
            eFaturaGondericiEtiket = senderAlias, eFaturaPostaKutusuEtiket = "urn:mail:defaultpk@deneme.example",
            eFaturaCanliOrtam = true, eFaturaSenaryo = scenario, faturaSeriKodu = "RNT", surum = version,
        };

    [Fact]
    public async Task EInvoice_settings_keep_secret_write_only_encrypted_and_report_honest_not_connected_state()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var first = await Json(await admin.C.GetAsync(Settings));
        Assert.False(first.GetProperty("eFaturaBagli").GetBoolean());
        Assert.Equal("e-Fatura gönderilmedi: entegratör bağlı değil (entegratör seçilmedi).", first.GetProperty("eFaturaDurumMesaji").GetString());
        Assert.Equal(0, first.GetProperty("eFaturaEntegratorSecenekleri").GetArrayLength()); // bu sürümde adaptör yok

        var secret = Secret();
        var r = await Send(admin, HttpMethod.Put, Settings, EInvoiceBody(first.GetProperty("surum").GetString(), secret,
            senderAlias: "urn:mail:defaultgb@deneme.example", scenario: "TICARIFATURA"));
        var text = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        var saved = System.Text.Json.JsonDocument.Parse(text).RootElement;
        Assert.True(saved.GetProperty("eFaturaSifreTanimli").GetBoolean());
        Assert.False(saved.TryGetProperty("eFaturaSifre", out _));
        Assert.Equal(("İstanbul", "Kadıköy"), (saved.GetProperty("firmaIl").GetString(), saved.GetProperty("firmaIlce").GetString()));
        Assert.Equal("urn:mail:defaultgb@deneme.example", saved.GetProperty("eFaturaGondericiEtiket").GetString());
        Assert.Equal("TICARIFATURA", saved.GetProperty("eFaturaSenaryo").GetString());
        Assert.True(saved.GetProperty("eFaturaCanliOrtam").GetBoolean());
        Assert.False(saved.GetProperty("eFaturaBagli").GetBoolean()); // kimlik dolu ama entegratör seçilmedi/adaptör yok

        var cipher = await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.EFaturaSifreEnc).FirstAsync());
        Assert.False(string.IsNullOrEmpty(cipher));
        Assert.NotEqual(secret, cipher);
        Assert.DoesNotContain(secret, cipher!, StringComparison.Ordinal);
        Assert.Equal(secret, fx.Web.Services.GetRequiredService<ISecretProtector>().Unprotect(cipher)); // geri çözülebilir şifre

        foreach (var url in new[] { Settings, V1 + "/denetim?tablo=Ayarlar&boyut=200" })
        {
            var body = await (await admin.C.GetAsync(url)).Content.ReadAsStringAsync();
            Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
            Assert.DoesNotContain(cipher!, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EInvoice_settings_reject_unregistered_integrator_and_malformed_alias_or_scenario_on_their_fields()
    {
        var e = await _kit.SetupAsync();
        var admin = await _kit.LoginAsync(e, Who.Admin);
        var version = (await Json(await admin.C.GetAsync(Settings))).GetProperty("surum").GetString();

        await Problem(await Send(admin, HttpMethod.Put, Settings, EInvoiceBody(version, integrator: "uydurma-entegrator")),
            HttpStatusCode.BadRequest, "dogrulama", "eFaturaEntegrator");
        await Problem(await Send(admin, HttpMethod.Put, Settings, EInvoiceBody(version, senderAlias: "defaultgb@deneme.example")),
            HttpStatusCode.BadRequest, "dogrulama", "eFaturaGondericiEtiket");
        await Problem(await Send(admin, HttpMethod.Put, Settings, EInvoiceBody(version, scenario: "EARSIVFATURA")),
            HttpStatusCode.BadRequest, "dogrulama", "eFaturaSenaryo");
        await Problem(await Send(admin, HttpMethod.Put, Settings, EInvoiceBody(version, integrator: new string('a', 33))),
            HttpStatusCode.BadRequest, "dogrulama", "eFaturaEntegrator");
        // Hiçbiri yazılmadı.
        Assert.Null(await _kit.ReadAsync(e.TenantId, db => db.TenantSettings.AsNoTracking().Select(x => x.EFaturaEntegrator).FirstOrDefaultAsync()));
    }
}
