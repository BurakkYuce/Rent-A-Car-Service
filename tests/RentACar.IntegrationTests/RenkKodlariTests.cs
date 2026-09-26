using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.TenantSettings;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-81 — Görünüm renk kodları (tenant başına 8 hex).
///
/// <para><b>Regresyon çiti:</b> renk seçmeyen tenant'ta hiçbir şey değişmemeli. Bu, iki ayrı
/// mekanizmayla sağlanıyor ve ikisi de burada test ediliyor: (1) ayar NULL kalabiliyor,
/// (2) CSS tarafı <c>var(--tr-renk-x, eski-sabit)</c> fallback zinciriyle okuyor — yani
/// değişken hiç basılmazsa eski renk devrede kalır.</para>
///
/// <para><b>Güvenlik:</b> bu değerler doğrudan bir <c>&lt;style&gt;</c> bloğuna yazılıyor.
/// Doğrulama olmasaydı serbest metin CSS'e sızardı; servis <c>#rrggbb</c> dışındaki her şeyi
/// reddediyor ve layout ikinci bir savunma olarak biçimi yeniden sınıyor.</para>
///
/// <para>Bağımsız oracle: hex değerler testte elle yazılır ve aynen geri okunur.</para>
/// </summary>
[Collection("postgres")]
public sealed class RenkKodlariTests(PostgresFixture fx)
{
    private static async Task<TenantSettingsModel> SettingAsync(IServiceProvider sp)
        => await sp.GetRequiredService<TenantSettingsService>().GetAsync();

    [Fact]
    public async Task Sekiz_renk_round_trip_ve_kucuk_harfe_normalize()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<TenantSettingsService>();

        var m = await SettingAsync(sp);
        m.RenkGecikenler = "#DC2626";           // büyük harf → küçüğe normalize
        m.RenkBugunDonecekler = "#0ea5e9";
        m.RenkBugunCikacaklar = "#16a34a";
        m.RenkOpsiyonlu = " #f59e0b ";          // boşluklu → trim
        m.RenkLimitBakiye = "#b91c1c";
        m.RenkAlacakli = "#7c3aed";
        m.RenkRezAtananPlaka = "#0d9488";
        m.RenkKiralanmayan = "#64748b";
        await svc.SaveAsync(m);

        var g = await SettingAsync(sp);
        Assert.Equal("#dc2626", g.RenkGecikenler);
        Assert.Equal("#0ea5e9", g.RenkBugunDonecekler);
        Assert.Equal("#16a34a", g.RenkBugunCikacaklar);
        Assert.Equal("#f59e0b", g.RenkOpsiyonlu);
        Assert.Equal("#b91c1c", g.RenkLimitBakiye);
        Assert.Equal("#7c3aed", g.RenkAlacakli);
        Assert.Equal("#0d9488", g.RenkRezAtananPlaka);
        Assert.Equal("#64748b", g.RenkKiralanmayan);
    }

    [Fact]
    public async Task Renk_secilmeyen_tenantta_hepsi_NULL_kalir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        var g = await SettingAsync(sp);
        Assert.Null(g.RenkGecikenler);
        Assert.Null(g.RenkBugunDonecekler);
        Assert.Null(g.RenkBugunCikacaklar);
        Assert.Null(g.RenkOpsiyonlu);
        Assert.Null(g.RenkLimitBakiye);
        Assert.Null(g.RenkAlacakli);
        Assert.Null(g.RenkRezAtananPlaka);
        Assert.Null(g.RenkKiralanmayan);

        // Kaydedip geri temizlemek de mümkün olmalı ("varsayılana dön" ulaşılabilir kalsın).
        var svc = sp.GetRequiredService<TenantSettingsService>();
        var m = await SettingAsync(sp);
        m.RenkGecikenler = "#123456";
        await svc.SaveAsync(m);
        Assert.Equal("#123456", (await SettingAsync(sp)).RenkGecikenler);

        m = await SettingAsync(sp);
        m.RenkGecikenler = null;
        await svc.SaveAsync(m);
        Assert.Null((await SettingAsync(sp)).RenkGecikenler);
    }

    [Fact]
    public async Task Gecersiz_renk_REDDEDILIR_ve_kaydedilmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<TenantSettingsService>();

        foreach (var bad in new[] { "kirmizi", "#12", "#12345", "#1234567", "dc2626", "#gggggg",
                                     "red; background:url(x)", "#dc2626;}" })
        {
            var m = await SettingAsync(sp);
            m.RenkGecikenler = bad;
            var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.SaveAsync(m));
            Assert.Contains("renk kodu", ex.Message);
        }

        // Hiçbiri yazılmamış olmalı.
        Assert.Null((await SettingAsync(sp)).RenkGecikenler);
    }

    [Fact]
    public async Task Renk_kodlari_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
        {
            var svc = s1.ServiceProvider.GetRequiredService<TenantSettingsService>();
            var m = await SettingAsync(s1.ServiceProvider);
            m.RenkGecikenler = "#abcdef";
            await svc.SaveAsync(m);
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        Assert.Null((await SettingAsync(s2.ServiceProvider)).RenkGecikenler);

        using var s1b = host.ScopeFor(t1);
        Assert.Equal("#abcdef", (await SettingAsync(s1b.ServiceProvider)).RenkGecikenler);
    }

    [Fact]
    public void CSS_fallback_zinciri_KIRILMAMIS()
    {
        // Kaynak çiti: birisi var(--tr-renk-x) yazıp fallback'i düşürürse renk seçmemiş
        // tenant'ta rozet renksiz kalır. Fallback'in KORUNDUĞU dosyadan doğrulanır.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        var css = File.ReadAllText(Path.Combine(d!.FullName, "src/RentACar.Web/wwwroot/app.css"));

        // --tr-renk-* her kullanımda İKİ argümanlı var(...) içinde olmalı (fallback'li).
        foreach (Match m in Regex.Matches(css, @"var\(\s*--tr-renk-[a-z-]+\s*(,)?"))
            Assert.True(m.Groups[1].Success,
                $"CSS'te fallback'siz tenant renk değişkeni var: {m.Value}");

        Assert.Contains("--tr-renk-gecikenler", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_renk_bildirimini_DOGRULAYARAK_basar()
    {
        // Değerler <style> içine yazıldığı için layout ikinci savunmayı taşımalı: biçim sınaması
        // kaldırılırsa servis doğrulamasını atlayan herhangi bir yol (import/seed) CSS'e serbest
        // metin sızdırabilirdi.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "RentACar.slnx"))) d = d.Parent;
        Assert.NotNull(d);
        var layout = File.ReadAllText(Path.Combine(d!.FullName,
            "src/RentACar.Web/Components/Layout/MainLayout.razor"));

        Assert.Contains("^#[0-9a-fA-F]{6}$", layout, StringComparison.Ordinal);
        Assert.Contains("--tr-renk-", layout, StringComparison.Ordinal);
    }
}
