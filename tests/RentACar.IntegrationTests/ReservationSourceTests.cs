using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.ReservationSources;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// Rezervasyon kaynağı master — bağımsız oracle. CRUD + kod normalize/benzersizlik + aktif filtre +
/// yetki + tenant izolasyon.
/// </summary>
[Collection("postgres")]
public sealed class ReservationSourceTests(PostgresFixture fx)
{
    [Fact]
    public async Task Create_normalizes_kod_and_roundtrips()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        var id = await svc.CreateAsync(new ReservationSourceInput { Kod = "web", Ad = "Web Sitesi" });
        var got = await svc.GetAsync(id);
        Assert.Equal("WEB", got!.Kod);
        Assert.Equal("Web Sitesi", got.Ad);
        Assert.True(got.Aktif);
    }

    [Fact]
    public async Task Duplicate_kod_rejected_case_insensitive()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        await svc.CreateAsync(new ReservationSourceInput { Kod = "TELEFON", Ad = "Telefon" });
        await Assert.ThrowsAsync<ValidationException>(
            () => svc.CreateAsync(new ReservationSourceInput { Kod = "telefon", Ad = "Başka" }));
    }

    [Fact]
    public async Task ListActive_excludes_passive_but_list_keeps_all()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        var a = await svc.CreateAsync(new ReservationSourceInput { Kod = "A", Ad = "A Kaynak" });
        await svc.CreateAsync(new ReservationSourceInput { Kod = "B", Ad = "B Kaynak" });
        await svc.UpdateAsync(a, new ReservationSourceInput { Kod = "A", Ad = "A Kaynak", Aktif = false });

        var active = await svc.ListActiveAsync();
        Assert.Single(active);
        Assert.Equal("B", active[0].Kod);
        Assert.Equal(2, (await svc.ListAsync()).Count);
    }

    [Fact]
    public async Task Validation_requires_kod_and_ad()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput { Kod = "", Ad = "Ad" }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput { Kod = "X", Ad = "  " }));
    }

    [Fact]
    public async Task NonOperations_user_cannot_manage()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid(), Guid.NewGuid(), "muh", UserRole.Muhasebe);
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();
        await Assert.ThrowsAsync<YetkiYokException>(
            () => svc.CreateAsync(new ReservationSourceInput { Kod = "X", Ad = "Yetkisiz" }));
    }

    [Fact]
    public async Task Sources_are_tenant_isolated()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();

        using (var s1 = host.ScopeFor(t1))
            await s1.ServiceProvider.GetRequiredService<ReservationSourceService>()
                .CreateAsync(new ReservationSourceInput { Kod = "T1", Ad = "Tenant1" });

        using var s2 = host.ScopeFor(t2);
        var svc2 = s2.ServiceProvider.GetRequiredService<ReservationSourceService>();
        Assert.Empty(await svc2.ListAsync());
        await svc2.CreateAsync(new ReservationSourceInput { Kod = "T1", Ad = "Tenant2" });
        Assert.Single(await svc2.ListAsync());
    }

    // ---------------------------------------------------------------------------------------
    // FAZ-24 — tedarikçi + oran alanları ve "Aşağıya Yansıt"
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task FAZ24_tedarikci_ve_oranlar_her_iki_yolda_da_roundtrip()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        var id = await svc.CreateAsync(new ReservationSourceInput
        {
            Kod = "rc", Ad = "Rentalcars", Tedarikci = "  Rentalcars B.V.  ",
            KiraOrani = 12.5m, HizmetOrani = 8m, DropOrani = 0m
        });

        var got = await svc.GetAsync(id);
        Assert.Equal("Rentalcars B.V.", got!.Tedarikci);      // trim
        Assert.Equal(12.5m, got.KiraOrani);
        Assert.Equal(8m, got.HizmetOrani);
        Assert.Equal(0m, got.DropOrani);                      // 0 MEŞRU — null'a çevrilmez

        // GÜNCELLEME YOLU da yazmalı: yalnız create'e yazmak alanı sessizce düşürürdü
        // (kopya-kurucu tuzağı — bkz. MasterTanimService.ekAlanlar).
        await svc.UpdateAsync(id, new ReservationSourceInput
        {
            Kod = "rc", Ad = "Rentalcars", Aktif = true,
            Tedarikci = "Booking", KiraOrani = 20m, HizmetOrani = null, DropOrani = 5.25m
        });

        var y = await svc.GetAsync(id);
        Assert.Equal("Booking", y!.Tedarikci);
        Assert.Equal(20m, y.KiraOrani);
        Assert.Null(y.HizmetOrani);                           // temizleme de bir güncellemedir
        Assert.Equal(5.25m, y.DropOrani);
    }

    [Fact]
    public async Task FAZ24_oran_araligi_disi_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "N1", Ad = "Negatif", KiraOrani = -1m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "N2", Ad = "Aşırı", HizmetOrani = 100.01m }));
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(new ReservationSourceInput
        { Kod = "N3", Ad = "Aşırı drop", DropOrani = 250m }));

        Assert.Empty(await svc.ListAsync());
    }

    [Fact]
    public async Task FAZ24_asagiya_yansit_yalniz_AKTIF_kayitlari_gunceller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        // ELLE KURULAN SENARYO: 1 oranlı kaynak, 2 boş AKTİF kaynak, 1 boş PASİF kaynak.
        var kaynak = await svc.CreateAsync(new ReservationSourceInput
        { Kod = "SRC", Ad = "Kaynak", KiraOrani = 15m, HizmetOrani = 7.5m, DropOrani = 3m });
        var bos1 = await svc.CreateAsync(new ReservationSourceInput { Kod = "B1", Ad = "Boş 1" });
        var bos2 = await svc.CreateAsync(new ReservationSourceInput { Kod = "B2", Ad = "Boş 2" });
        var pasif = await svc.CreateAsync(new ReservationSourceInput { Kod = "P1", Ad = "Pasif" });
        await svc.UpdateAsync(pasif, new ReservationSourceInput { Kod = "P1", Ad = "Pasif", Aktif = false });

        var adet = await svc.OranlariYansitAsync(kaynak);
        Assert.Equal(2, adet);                                 // ELLE: yalnız B1 + B2

        foreach (var id in new[] { bos1, bos2 })
        {
            var r = await svc.GetAsync(id);
            Assert.Equal(15m, r!.KiraOrani);
            Assert.Equal(7.5m, r.HizmetOrani);
            Assert.Equal(3m, r.DropOrani);
        }

        // PASİF kayıt DEĞİŞMEDİ — tarihsel tanım toplu işlemle diriltilmemeli.
        var p = await svc.GetAsync(pasif);
        Assert.Null(p!.KiraOrani);
        Assert.Null(p.HizmetOrani);
        Assert.Null(p.DropOrani);
        Assert.False(p.Aktif);

        // Kaynağın kendisi de bozulmadı.
        var k = await svc.GetAsync(kaynak);
        Assert.Equal(15m, k!.KiraOrani);

        // Kod/Ad/Aktif üçlüsüne DOKUNULMADI — yansıtma yalnız oran kolonlarını yazar.
        Assert.Equal("B1", (await svc.GetAsync(bos1))!.Kod);
        Assert.Equal("Boş 1", (await svc.GetAsync(bos1))!.Ad);
    }

    [Fact]
    public async Task FAZ24_yansitma_bos_oranlari_da_kopyalar_ve_liste_cachei_tazelenir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReservationSourceService>();

        // ELLE: kaynak BOŞ oranlı, hedef DOLU → yansıtma hedefi TEMİZLEMELİ. "Yalnız doluları
        // kopyala" deseydik "hepsini temizle" işlemi sessizce yarım kalırdı.
        var kaynak = await svc.CreateAsync(new ReservationSourceInput { Kod = "SRC", Ad = "Boş kaynak" });
        var dolu = await svc.CreateAsync(new ReservationSourceInput
        { Kod = "D1", Ad = "Dolu", KiraOrani = 30m, HizmetOrani = 30m, DropOrani = 30m });

        await svc.ListAsync();                                 // cache'i ISIT — yansıtma sonrası bayat kalmamalı
        Assert.Equal(1, await svc.OranlariYansitAsync(kaynak));

        var d = await svc.GetAsync(dolu);
        Assert.Null(d!.KiraOrani);
        Assert.Null(d.HizmetOrani);
        Assert.Null(d.DropOrani);

        // Cache invalidate edilmemiş olsaydı liste hâlâ 30'ları gösterirdi.
        var liste = await svc.ListAsync();
        Assert.All(liste, r => Assert.Null(r.KiraOrani));
    }

    [Fact]
    public async Task FAZ24_yansitma_yetki_ve_tenant_citi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid kaynak;
        using (var admin = host.ScopeFor(tenant))
            kaynak = await admin.ServiceProvider.GetRequiredService<ReservationSourceService>()
                .CreateAsync(new ReservationSourceInput { Kod = "SRC", Ad = "Kaynak", KiraOrani = 10m });

        // Yetkisiz rol yansıtamaz (OperationsWrite).
        using (var muh = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe))
            await Assert.ThrowsAsync<YetkiYokException>(
                () => muh.ServiceProvider.GetRequiredService<ReservationSourceService>().OranlariYansitAsync(kaynak));

        // BAŞKA tenant'ın kaynağı GÖRÜNMEZ → "bulunamadı" (çapraz-tenant yansıtma imkânsız).
        using var baska = host.ScopeFor(Guid.NewGuid());
        var svc = baska.ServiceProvider.GetRequiredService<ReservationSourceService>();
        await svc.CreateAsync(new ReservationSourceInput { Kod = "X", Ad = "Yabancı", KiraOrani = 99m });
        await Assert.ThrowsAsync<ValidationException>(() => svc.OranlariYansitAsync(kaynak));

        // Diğer tenant'ın kaydı da etkilenmedi.
        using var geri = host.ScopeFor(tenant);
        Assert.Equal(10m, (await geri.ServiceProvider.GetRequiredService<ReservationSourceService>()
            .GetAsync(kaynak))!.KiraOrani);
    }

    /// <summary>
    /// FAZ-24 Exit çıtası: <b>oranlar hiçbir para hesabına girmez.</b> Kaynak → Kanal eşlemesi
    /// üzerinden gerçek fiyat çözülür; sonra kaynağa %50'lik oranlar yazılıp yansıtılır ve AYNI
    /// senaryo yeniden fiyatlanır. Tutar KURUŞU KURUŞUNA aynı kalmalı — bu test, ileride biri
    /// oranı sessizce motora bağladığında KIRILIR (kasıtlı kilit).
    /// </summary>
    [Fact]
    public async Task FAZ24_oranlar_FIYATA_ETKI_ETMEZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var kaynaklar = sp.GetRequiredService<ReservationSourceService>();

        await sp.GetRequiredService<RentACar.Application.VehicleGroups.VehicleGroupService>()
            .CreateAsync(new RentACar.Application.VehicleGroups.VehicleGroupInput { Kod = "EKO", Ad = "Ekonomik" });
        await sp.GetRequiredService<RentACar.Application.RateMatrices.RateMatrixService>()
            .CreateAsync(new RentACar.Application.RateMatrices.RateMatrixInput
            {
                Kod = "EKO-WEB", Ad = "Eko Web", Kanal = "WEB", AracGrupKod = "EKO",
                Gun1 = 1000m, Gun2 = 950m, Gun3 = 900m, OnayDurumu = TarifeOnayDurumu.Onayli
            });
        var kaynak = await kaynaklar.CreateAsync(new ReservationSourceInput { Kod = "WEB", Ad = "Web Sitesi" });
        await kaynaklar.CreateAsync(new ReservationSourceInput { Kod = "TEL", Ad = "Telefon" });

        var arac = await sp.GetRequiredService<RentACar.Application.Vehicles.VehicleService>()
            .CreateAsync(new RentACar.Application.Vehicles.VehicleInput { Plaka = "34 RK 24", Grup = "EKO" });

        var bas = new DateTimeOffset(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
        var fiyat = sp.GetRequiredService<RentACar.Application.Pricing.PricingService>();

        RentACar.Application.Bookings.BookingInput Istek() => new()
        {
            VehicleId = arac, BasTar = bas, BitTar = bas.AddDays(3),
            Kaynak = "WEB", FiyatTuru = "Otomatik"
        };

        var once = await fiyat.PriceAsync(Istek());
        Assert.Equal(3, once.Gun);
        Assert.Equal(2700m, once.Tutar);          // ELLE: 3 gün × Gun3 (900) = 2700

        // Oranlar %50 — bir yerde çarpan olarak kullanılsa tutar KESİN değişirdi.
        await kaynaklar.UpdateAsync(kaynak, new ReservationSourceInput
        {
            Kod = "WEB", Ad = "Web Sitesi", Aktif = true, Tedarikci = "Acente",
            KiraOrani = 50m, HizmetOrani = 50m, DropOrani = 50m
        });
        await kaynaklar.OranlariYansitAsync(kaynak);

        var sonra = await fiyat.PriceAsync(Istek());
        Assert.Equal(once.Gun, sonra.Gun);
        Assert.Equal(2700m, sonra.Tutar);         // DEĞİŞMEDİ

        // Kaynak→Kanal eşlemesi de oran eklemekten ETKİLENMEDİ: tanımsız kaynak kanalı null
        // bırakır (kanal-özel tarife seçtiremez) ve motor kanal-agnostik seçime düşer — mevcut
        // davranış, bu fazda değişmedi.
        var tanimsiz = await fiyat.PriceAsync(new RentACar.Application.Bookings.BookingInput
        {
            VehicleId = arac, BasTar = bas, BitTar = bas.AddDays(3),
            Kaynak = "TANIMSIZ-KAYNAK", FiyatTuru = "Otomatik"
        });
        Assert.Equal(2700m, tanimsiz.Tutar);
    }
}
