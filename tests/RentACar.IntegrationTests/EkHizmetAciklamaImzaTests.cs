using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.BelgeSablon;
using RentACar.Application.Common;
using RentACar.Application.EkHizmetler;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-80 — Ek hizmet açıklama/max-gün + belge şablonu imza alanı anahtarı.
///
/// <para>Bağımsız oracle: değerler testte elle verilir ve aynen geri okunur; doğrulama mesajları
/// servis kodundan kopyalanmaz, beklenen metin testte elle yazılır.</para>
///
/// <para><b>Regresyon çiti:</b> her iki alan da MEVCUT davranışı değiştirmemeli —
/// <c>Aciklama</c>/<c>MaxGun</c> boş bırakılabilir (null), <c>ImzaAlaniGoster</c> varsayılanı
/// <c>true</c>'dur (yeni satırda ve şablon HİÇ seçilmediğinde).</para>
/// </summary>
[Collection("postgres")]
public sealed class EkHizmetAciklamaImzaTests(PostgresFixture fx)
{
    // ---------- Grup 1: Ek hizmet açıklama + max gün ----------

    [Fact]
    public async Task Aciklama_ve_MaxGun_create_update_turunda_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        var id = await svc.CreateAsync(new EkHizmetTanimInput
        {
            Kod = "genc", Ad = "Genç Sürücü", BirimUcret = 150m, KdvOrani = 0.20m,
            Aciklama = "  25 yaş altı sürücüler için zorunlu ek ücret.  ", MaxGun = 30
        });

        var t = await svc.GetAsync(id);
        Assert.NotNull(t);
        Assert.Equal("GENC", t!.Kod);                                              // eski normalize davranışı
        Assert.Equal("25 yaş altı sürücüler için zorunlu ek ücret.", t.Aciklama);   // trim
        Assert.Equal(30, t.MaxGun);

        await svc.UpdateAsync(id, new EkHizmetTanimInput
        { Kod = "GENC", Ad = "Genç Sürücü", BirimUcret = 150m, Aciklama = "Güncellendi", MaxGun = 45 });
        var g = await svc.GetAsync(id);
        Assert.Equal("Güncellendi", g!.Aciklama);
        Assert.Equal(45, g.MaxGun);
    }

    [Fact]
    public async Task Bos_Aciklama_ve_MaxGun_null_kalir_mevcut_davranis_DEGISMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        // Yeni alanlar HİÇ verilmeden eski çağrı biçimi çalışmalı.
        var id = await svc.CreateAsync(new EkHizmetTanimInput { Kod = "GPS", Ad = "Navigasyon", BirimUcret = 50m });
        var t = await svc.GetAsync(id);
        Assert.Null(t!.Aciklama);
        Assert.Null(t.MaxGun);
        Assert.Equal(50m, t.BirimUcret);
        Assert.True(t.Aktif);

        // Yalnız boşluktan oluşan açıklama da null'a düşer (boş metin saklanmaz).
        var id2 = await svc.CreateAsync(new EkHizmetTanimInput
        { Kod = "BBK", Ad = "Bebek Koltuğu", BirimUcret = 40m, Aciklama = "   " });
        Assert.Null((await svc.GetAsync(id2))!.Aciklama);
    }

    [Fact]
    public async Task Gecersiz_MaxGun_ve_uzun_aciklama_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<EkHizmetTanimService>();

        // 0 ve negatif anlamsız: "sınırsız" için null kullanılır.
        var ex0 = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new EkHizmetTanimInput { Kod = "S0", Ad = "Sıfır", MaxGun = 0 }));
        Assert.Contains("Max gün pozitif olmalıdır", ex0.Message);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new EkHizmetTanimInput { Kod = "SN", Ad = "Negatif", MaxGun = -5 }));

        var exU = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new EkHizmetTanimInput { Kod = "UZN", Ad = "Uzun", Aciklama = new string('x', 513) }));
        Assert.Contains("512 karakter", exU.Message);

        // Reddedilenler yazılmamış olmalı.
        Assert.Empty(await svc.ListAsync());

        // Tam sınır (512) kabul.
        var id = await svc.CreateAsync(new EkHizmetTanimInput
        { Kod = "SNR", Ad = "Sınır", Aciklama = new string('y', 512), MaxGun = 1 });
        Assert.Equal(512, (await svc.GetAsync(id))!.Aciklama!.Length);
    }

    // ---------- Grup 5: Belge şablonu imza alanı ----------

    [Fact]
    public async Task ImzaAlaniGoster_varsayilani_TRUE_ve_round_trip_calisir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<BelgeSablonService>();

        // Alan HİÇ set edilmeden: varsayılan true (mevcut PDF davranışı korunur).
        var id = await svc.CreateAsync(new BelgeSablonInput
        { BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Varsayılan Şablon" });
        Assert.True((await svc.ListAsync()).Single(x => x.Id == id).ImzaAlaniGoster);

        // Kapatılabilir…
        await svc.UpdateAsync(id, new BelgeSablonInput
        { BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Varsayılan Şablon", ImzaAlaniGoster = false });
        Assert.False((await svc.ListAsync()).Single(x => x.Id == id).ImzaAlaniGoster);

        // …ve geri açılabilir.
        await svc.UpdateAsync(id, new BelgeSablonInput
        { BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "Varsayılan Şablon", ImzaAlaniGoster = true });
        Assert.True((await svc.ListAsync()).Single(x => x.Id == id).ImzaAlaniGoster);
    }

    [Fact]
    public async Task Cozumleyici_sablon_YOKKEN_imza_alanini_ACIK_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var coz = s.ServiceProvider.GetRequiredService<BelgeSablonCozumleyici>();

        // Hiç şablon yok → SablonMetin.Bos → imza alanı AÇIK olmalı (regresyon çiti: şablon
        // tanımlamayan tenant'ların sözleşmesi imzasız basılmaya başlamamalı).
        Assert.True((await coz.KiraAsync(null)).ImzaAlaniGoster);

        // Kapalı varsayılan şablon tanımlanırsa çözümleyici onu taşır.
        await s.ServiceProvider.GetRequiredService<BelgeSablonService>().CreateAsync(new BelgeSablonInput
        {
            BelgeTuru = BelgeTuru.KiraSozlesmesi, Ad = "E-imza", VarsayilanMi = true,
            ImzaAlaniGoster = false
        });
        Assert.False((await coz.KiraAsync(null)).ImzaAlaniGoster);
    }
}
