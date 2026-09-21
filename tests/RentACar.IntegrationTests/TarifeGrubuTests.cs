using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Pricing;
using RentACar.Application.TarifeGruplari;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-72 — Tarife (fiyat) grubu master + tarife teminat/görünürlük alanları.
///
/// <para><b>Kimlik alanları:</b> şifre DÜZ SAKLANMAZ. Test bunu DB'ye doğrudan bakarak kanıtlıyor —
/// servis dönüşüne değil, kolonun kendisine. Ayrıca boş şifreyle güncelleme mevcut özeti KORUMALI:
/// aksi hâlde her düzenleme kimliği sessizce siler.</para>
///
/// <para><b>Silme davranışı:</b> tarife grubu silinince bağlı `RateCard` satırları SİLİNMEZ, yalnız
/// `TarifeGrubuId` NULL'a düşer (ON DELETE SET NULL). Fiyat satırının bir gruplama kaydı yüzünden
/// yok olması veri kaybı olurdu.</para>
/// </summary>
[Collection("postgres")]
public sealed class TarifeGrubuTests(PostgresFixture fx)
{
    private const string Sifre = "brokerSifre1";

    [Fact]
    public async Task CRUD_ve_kod_benzersizligi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<TarifeGrubuService>();

        var id = await svc.CreateAsync(new TarifeGrubuInput
        { Kod = "brk-x", Ad = " Broker X ", Oran = 0.15m, KullaniciAdi = " brokerx ", Sifre = Sifre });

        var g = await svc.GetAsync(id);
        Assert.Equal("BRK-X", g!.Kod);          // kod büyük harfe normalize
        Assert.Equal("Broker X", g.Ad);         // trim
        Assert.Equal(0.15m, g.Oran);
        Assert.Equal("brokerx", g.KullaniciAdi);
        Assert.True(g.Aktif);

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new TarifeGrubuInput { Kod = "BRK-X", Ad = "Kopya" }));

        // Aktiflik/pasiflik
        await svc.UpdateAsync(id, new TarifeGrubuInput { Kod = "BRK-X", Ad = "Broker X", Aktif = false });
        Assert.Empty(await svc.ListActiveAsync());
        Assert.Single(await svc.ListAsync());

        Assert.True(await svc.DeleteAsync(id));
        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Sifre_DUZ_METIN_olarak_SAKLANMAZ_ve_bos_birakilinca_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<TarifeGrubuService>();

        var id = await svc.CreateAsync(new TarifeGrubuInput
        { Kod = "BRK1", Ad = "Broker 1", KullaniciAdi = "u1", Sifre = Sifre });

        // DB'ye DOĞRUDAN bak: ham şifre hiçbir kolonda geçmemeli.
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.TarifeGruplari.AsNoTracking().SingleAsync(x => x.Id == id);
            Assert.False(string.IsNullOrWhiteSpace(row.SifreHash));
            Assert.DoesNotContain(Sifre, row.SifreHash!, StringComparison.Ordinal);
            Assert.NotEqual(Sifre, row.SifreHash);
        }

        var ilkHash = (await svc.GetAsync(id))!.SifreHash;

        // Şifre BOŞ bırakılarak güncelleme → mevcut özet korunmalı.
        await svc.UpdateAsync(id, new TarifeGrubuInput
        { Kod = "BRK1", Ad = "Broker 1 (yeni ad)", KullaniciAdi = "u1", Sifre = null });
        var sonra = await svc.GetAsync(id);
        Assert.Equal("Broker 1 (yeni ad)", sonra!.Ad);
        Assert.Equal(ilkHash, sonra.SifreHash);          // kimlik kaybolmadı

        // Yeni şifre verilince özet DEĞİŞMELİ.
        await svc.UpdateAsync(id, new TarifeGrubuInput
        { Kod = "BRK1", Ad = "Broker 1", Sifre = "baskaSifre2" });
        Assert.NotEqual(ilkHash, (await svc.GetAsync(id))!.SifreHash);
    }

    [Fact]
    public async Task Gecersiz_giris_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<TarifeGrubuService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new TarifeGrubuInput { Kod = "", Ad = "X" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new TarifeGrubuInput { Kod = "K", Ad = "" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new TarifeGrubuInput { Kod = "K", Ad = "X", Oran = -1m }));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(
            new TarifeGrubuInput { Kod = "K", Ad = "X", Sifre = "kisa" }));
        Assert.Contains("en az 6 karakter", ex.Message);

        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task Yetkisiz_rol_yazamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = s.ServiceProvider.GetRequiredService<TarifeGrubuService>();
        await Assert.ThrowsAsync<YetkiYokException>(() => svc.CreateAsync(
            new TarifeGrubuInput { Kod = "K", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Tarife_grubu_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
            id = await s1.ServiceProvider.GetRequiredService<TarifeGrubuService>()
                .CreateAsync(new TarifeGrubuInput { Kod = "GIZLI", Ad = "Gizli Grup" });

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc2 = s2.ServiceProvider.GetRequiredService<TarifeGrubuService>();
        Assert.Empty(await svc2.ListAsync());
        Assert.Null(await svc2.GetAsync(id));                 // id bilinse bile görünmez
        Assert.False(await svc2.DeleteAsync(id));             // silinemez
        // Aynı kod başka tenant'ta serbest olmalı.
        Assert.NotEqual(Guid.Empty, await svc2.CreateAsync(new TarifeGrubuInput { Kod = "GIZLI", Ad = "Başka" }));
    }

    // ---------- RateCard tarafı ----------

    [Fact]
    public async Task RateCard_teminat_bayraklari_ve_grup_referansi_round_trip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var grup = await sp.GetRequiredService<TarifeGrubuService>()
            .CreateAsync(new TarifeGrubuInput { Kod = "BRK", Ad = "Broker" });
        var rates = sp.GetRequiredService<RateCardService>();

        var id = await rates.CreateAsync(new RateCardInput
        {
            Kod = "T1", Ad = "Tarife 1", Grup = "EKO", MinGun = 1, MaxGun = 30, GunlukUcret = 1000m,
            ScdwDahil = true, MiniHasarDahil = true, HirsizlikDahil = false, ScdwZorunlu = true,
            Gosterme = true, TarifeGrubuId = grup
        });

        var r = await rates.GetAsync(id);
        Assert.True(r!.ScdwDahil);
        Assert.True(r.MiniHasarDahil);
        Assert.False(r.HirsizlikDahil);
        Assert.True(r.ScdwZorunlu);
        Assert.True(r.Gosterme);
        Assert.Equal(grup, r.TarifeGrubuId);

        // Bayraklar geri kapatılabilmeli (bool'un false'a dönüşü Normalize'da kaybolmamalı).
        await rates.UpdateAsync(id, new RateCardInput
        { Kod = "T1", Ad = "Tarife 1", Grup = "EKO", MinGun = 1, MaxGun = 30, GunlukUcret = 1000m, Aktif = true });
        var g2 = await rates.GetAsync(id);
        Assert.False(g2!.ScdwDahil);
        Assert.False(g2.Gosterme);
        Assert.Null(g2.TarifeGrubuId);
    }

    [Fact]
    public async Task Var_olmayan_tarife_grubu_REDDEDILIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var rates = s.ServiceProvider.GetRequiredService<RateCardService>();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => rates.CreateAsync(new RateCardInput
        { Kod = "T9", Ad = "Hayalet", Grup = "EKO", GunlukUcret = 100m, TarifeGrubuId = Guid.NewGuid() }));
        Assert.Contains("tarife grubu bulunamadı", ex.Message);
        Assert.Empty(await rates.ListAsync());
    }

    [Fact]
    public async Task Baska_tenantin_grubu_TARIFEYE_baglanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        Guid yabanciGrup;
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
            yabanciGrup = await s1.ServiceProvider.GetRequiredService<TarifeGrubuService>()
                .CreateAsync(new TarifeGrubuInput { Kod = "Y", Ad = "Yabancı" });

        using var s2 = host.ScopeFor(Guid.NewGuid());
        await Assert.ThrowsAsync<ValidationException>(() =>
            s2.ServiceProvider.GetRequiredService<RateCardService>().CreateAsync(new RateCardInput
            { Kod = "T1", Ad = "Sızıntı", Grup = "EKO", GunlukUcret = 100m, TarifeGrubuId = yabanciGrup }));
    }

    [Fact]
    public async Task Grup_silinince_TARIFE_SILINMEZ_bag_NULLa_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var gruplar = sp.GetRequiredService<TarifeGrubuService>();
        var rates = sp.GetRequiredService<RateCardService>();

        var grup = await gruplar.CreateAsync(new TarifeGrubuInput { Kod = "BRK", Ad = "Broker" });
        var tarife = await rates.CreateAsync(new RateCardInput
        { Kod = "T1", Ad = "Tarife", Grup = "EKO", GunlukUcret = 500m, TarifeGrubuId = grup });

        await gruplar.DeleteAsync(grup);

        var r = await rates.GetAsync(tarife);
        Assert.NotNull(r);                 // tarife satırı DURUYOR
        Assert.Equal(500m, r!.GunlukUcret);
        Assert.Null(r.TarifeGrubuId);      // yalnız bağ koptu
    }
}
