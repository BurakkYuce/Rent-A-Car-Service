using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.FiloKiralamalar;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-21 — Filo kiralama künye alanları + liste araması.
///
/// <para><b>Fazın en önemli kısıtı:</b> künye güncelleme yolu taksit planını DEĞİŞTİREMEZ.
/// <see cref="FiloKiralamaMetaInput"/> para/süre alanlarını TİP DÜZEYİNDE taşımaz
/// (<c>RentalUpdateInput</c> deseni) — yani "bir alanı maplemeyi unutmak" gibi bir risk yok, yol
/// yapısal olarak kapalı. Aşağıda bir yansıma testi bu sözleşmeyi kilitliyor.</para>
///
/// <para>Bağımsız oracle: değerler testte elle verilip aynen geri okunur; filtre beklentileri
/// senaryodan sayılır, servisin dönüşünden türetilmez.</para>
/// </summary>
[Collection("postgres")]
public sealed class FiloKiralamaDerinlikTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset T0 = TestZaman.Simdi().AddDays(-100);

    private static async Task<(Guid musteri, Guid arac)> TemelAsync(
        IServiceProvider sp, string ad, string plaka)
    {
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Kurumsal, Unvan = ad });
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        return (m, v);
    }

    [Fact]
    public async Task Kunye_alanlari_create_turunda_KORUNUR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (m, v) = await TemelAsync(sp, "Alfa A.Ş.", "34 FK 01");
        var svc = sp.GetRequiredService<FiloKiralamaService>();

        var id = await svc.CreateAsync(new FiloKiralamaInput
        {
            MusteriId = m, VehicleId = v, BasTar = T0, SureAy = 12, AylikUcret = 10000m,
            SatisTemsilcisi = "  Ayşe Yılmaz  ", FaturaTuru = "Dönem",
            SozlesmeTarihi = T0.AddDays(-5), ImzaTarih = T0.AddDays(-4),
            MakbuzNo = "MKB-1", DosyaNo = "DSY-1", SozlesmeNo = "FRM-2026-007",
            VadeGun = 15, FiyatTuru = "30 Gün Aylık", Kaynak = "Doğrudan",
            CikisKm = 12000, ToplamKm = 45000, ToplamKmLimiti = 60000
        });

        var k = await svc.GetAsync(id);
        Assert.NotNull(k);
        Assert.Equal("Ayşe Yılmaz", k!.SatisTemsilcisi);   // trim
        Assert.Equal("Dönem", k.FaturaTuru);
        Assert.Equal(T0.AddDays(-5), k.SozlesmeTarihi);
        Assert.Equal(T0.AddDays(-4), k.ImzaTarih);
        Assert.Equal("MKB-1", k.MakbuzNo);
        Assert.Equal("DSY-1", k.DosyaNo);
        Assert.Equal("FRM-2026-007", k.SozlesmeNo);
        Assert.Equal(15, k.VadeGun);
        Assert.Equal("30 Gün Aylık", k.FiyatTuru);
        Assert.Equal("Doğrudan", k.Kaynak);
        Assert.Equal(12000, k.CikisKm);
        Assert.Equal(45000, k.ToplamKm);
        // Sistem numarası ile firma numarası AYRI iki alandır, birbirine karışmamalı.
        Assert.NotEqual(k.SozlesmeNo, k.No);
        Assert.False(string.IsNullOrWhiteSpace(k.No));
        // KM limiti ile fiili KM ayrı alanlar.
        Assert.Equal(60000, k.ToplamKmLimiti);
    }

    [Fact]
    public async Task Kunye_guncelleme_TAKSIT_PLANINI_DEGISTIRMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (m, v) = await TemelAsync(sp, "Beta Ltd.", "34 FK 02");
        var svc = sp.GetRequiredService<FiloKiralamaService>();

        var id = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = m, VehicleId = v, BasTar = T0, SureAy = 6, AylikUcret = 5000m, KdvOrani = 0.20m, DamgaVergisi = 100m });

        var once = FiloKiralamaService.TaksitPlani((await svc.GetAsync(id))!);
        // ELLE: 6 ay × 5000 = 30.000 net; KDV %20 = 6.000; damga 100 → genel toplam 36.100.
        Assert.Equal(30000m, once.ToplamNet);
        Assert.Equal(6000m, once.ToplamKdv);
        Assert.Equal(36100m, once.GenelToplam);
        Assert.Equal(6, once.Taksitler.Count);

        await svc.UpdateMetaAsync(id, new FiloKiralamaMetaInput
        { SatisTemsilcisi = "Yeni Temsilci", VadeGun = 30, Kaynak = "Acente", ToplamKm = 1000, CikisKm = 500 });

        var sonra = await svc.GetAsync(id);
        Assert.Equal("Yeni Temsilci", sonra!.SatisTemsilcisi);
        Assert.Equal(30, sonra.VadeGun);
        // Para/süre alanları AYNEN duruyor → plan da aynı.
        Assert.Equal(6, sonra.SureAy);
        Assert.Equal(5000m, sonra.AylikUcret);
        Assert.Equal(0.20m, sonra.KdvOrani);
        Assert.Equal(100m, sonra.DamgaVergisi);
        Assert.Equal(T0, sonra.BasTar);
        var yeniPlan = FiloKiralamaService.TaksitPlani(sonra);
        Assert.Equal(once.GenelToplam, yeniPlan.GenelToplam);
        Assert.Equal(once.Taksitler.Count, yeniPlan.Taksitler.Count);
    }

    [Fact]
    public void Kunye_giris_tipi_PARA_VE_SURE_alani_TASIMAZ()
    {
        // Sözleşme kilidi: biri ileride "kolaylık olsun" diye AylikUcret/SureAy/KdvOrani/Kur/BasTar
        // eklerse taksit planı sessizce değiştirilebilir hâle gelir. Test o anda kırmızıya döner.
        var yasak = new[] { "AylikUcret", "SureAy", "KdvOrani", "Kur", "Doviz", "BasTar", "DamgaVergisi",
                            "MusteriId", "VehicleId", "Durum" };
        var alanlar = typeof(FiloKiralamaMetaInput)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        foreach (var y in yasak)
            Assert.DoesNotContain(y, alanlar);
    }

    [Fact]
    public async Task Iptal_edilmis_sozlesme_duzenlenemez_ve_gecersiz_km_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (m, v) = await TemelAsync(sp, "Gama", "34 FK 03");
        var svc = sp.GetRequiredService<FiloKiralamaService>();
        var id = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = m, VehicleId = v, SureAy = 3, AylikUcret = 1000m });

        // Toplam KM çıkış KM'sinden küçük olamaz.
        var ex = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateMetaAsync(id,
            new FiloKiralamaMetaInput { CikisKm = 5000, ToplamKm = 4000 }));
        Assert.Contains("Toplam KM", ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateMetaAsync(id,
            new FiloKiralamaMetaInput { VadeGun = -1 }));

        await svc.IptalAsync(id);
        var ex2 = await Assert.ThrowsAsync<ValidationException>(() => svc.UpdateMetaAsync(id,
            new FiloKiralamaMetaInput { SatisTemsilcisi = "X" }));
        Assert.Contains("İptal edilmiş", ex2.Message);
    }

    [Fact]
    public async Task Filtreler_ELLE_BEKLENEN_alt_kumeyi_dondurur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<FiloKiralamaService>();

        var (m1, v1) = await TemelAsync(sp, "Alfa Lojistik", "34 AL 01");
        var (m2, v2) = await TemelAsync(sp, "Beta Turizm", "06 BT 02");

        var a = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = m1, VehicleId = v1, BasTar = T0, SureAy = 12, AylikUcret = 9000m, SozlesmeNo = "FRM-A" });
        var b = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = m2, VehicleId = v2, BasTar = T0.AddDays(50), SureAy = 6, AylikUcret = 4000m, MakbuzNo = "MKB-B" });
        var c = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = m1, VehicleId = v1, BasTar = T0.AddDays(80), SureAy = 3, AylikUcret = 3000m, DosyaNo = "DSY-C" });

        // Filtresiz: eski davranış (3 kayıt).
        Assert.Equal(3, (await svc.ListAsync()).Count);
        Assert.Equal(3, (await svc.ListAsync(new FiloKiralamaFilter())).Count);

        // Müşteri
        Assert.Equal(2, (await svc.ListAsync(new FiloKiralamaFilter { MusteriId = m1 })).Count);
        Assert.Equal(b, Assert.Single(await svc.ListAsync(new FiloKiralamaFilter { MusteriId = m2 })).Id);

        // Plaka — BOŞLUKLU ve küçük harfli giriş de bulmalı (DB'de "34AL01" saklanıyor).
        Assert.Equal(2, (await svc.ListAsync(new FiloKiralamaFilter { Plaka = "34 AL" })).Count);
        Assert.Equal(b, Assert.Single(await svc.ListAsync(new FiloKiralamaFilter { Plaka = "bt" })).Id);

        // Metin araması: sözleşme/makbuz/dosya no
        Assert.Equal(a, Assert.Single(await svc.ListAsync(new FiloKiralamaFilter { Ara = "FRM-A" })).Id);
        Assert.Equal(b, Assert.Single(await svc.ListAsync(new FiloKiralamaFilter { Ara = "mkb-b" })).Id);
        Assert.Equal(c, Assert.Single(await svc.ListAsync(new FiloKiralamaFilter { Ara = "DSY" })).Id);
        Assert.Empty(await svc.ListAsync(new FiloKiralamaFilter { Ara = "yok-boyle" }));

        // Tarih: T0+40 sonrası → b ve c
        Assert.Equal(2, (await svc.ListAsync(new FiloKiralamaFilter { Bas = T0.AddDays(40) })).Count);
        // Kapalı aralık → yalnız b
        Assert.Equal(b, Assert.Single(await svc.ListAsync(
            new FiloKiralamaFilter { Bas = T0.AddDays(40), Bit = T0.AddDays(60) })).Id);

        // Durum: c iptal edilince Aktif filtresi 2 döner
        await svc.IptalAsync(c);
        Assert.Equal(2, (await svc.ListAsync(new FiloKiralamaFilter { Durum = FiloKiraDurum.Aktif })).Count);
        Assert.Equal(c, Assert.Single(await svc.ListAsync(new FiloKiralamaFilter { Durum = FiloKiraDurum.Iptal })).Id);

        // Birleşik: m1 + Aktif → yalnız a
        Assert.Equal(a, Assert.Single(await svc.ListAsync(
            new FiloKiralamaFilter { MusteriId = m1, Durum = FiloKiraDurum.Aktif })).Id);
    }

    [Fact]
    public async Task Filo_kiralama_DEFTERE_YAZMAZ_kunye_guncellemesi_de_yazmaz()
    {
        // Regresyon çiti: bu ekran bilinçli olarak deftersizdir (gelir aylık faturalama ile tanınır).
        // Künye alanları eklendi diye bir defter satırı doğmamalı.
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var (m, v) = await TemelAsync(sp, "Delta", "34 FK 04");
        var svc = sp.GetRequiredService<FiloKiralamaService>();

        var id = await svc.CreateAsync(new FiloKiralamaInput
        { MusteriId = m, VehicleId = v, SureAy = 12, AylikUcret = 25000m, SozlesmeNo = "FRM-D" });
        await svc.UpdateMetaAsync(id, new FiloKiralamaMetaInput { SatisTemsilcisi = "Temsilci" });

        // Cari bakiye SIFIR kalmalı — sözleşme deftere hiçbir şey postlamadı.
        Assert.Equal(0m, await sp.GetRequiredService<RentACar.Application.Finance.CashService>()
            .GetCariBalanceAsync(m));
    }

    [Fact]
    public async Task Filo_kiralama_tenant_izolasyonlu_filtreyle_de()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
        {
            var sp = s1.ServiceProvider;
            var (m, v) = await TemelAsync(sp, "Gizli A.Ş.", "34 GZ 01");
            await sp.GetRequiredService<FiloKiralamaService>().CreateAsync(new FiloKiralamaInput
            { MusteriId = m, VehicleId = v, SureAy = 12, AylikUcret = 1000m, SozlesmeNo = "GIZLI" });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<FiloKiralamaService>();
        Assert.Empty(await svc.ListAsync());
        Assert.Empty(await svc.ListAsync(new FiloKiralamaFilter { Ara = "GIZLI" }));
        Assert.Empty(await svc.ListAsync(new FiloKiralamaFilter { Plaka = "34 GZ" }));
    }
}
