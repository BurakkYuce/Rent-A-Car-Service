using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.FiloPlan;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-19 Bölüm A — filo plan hedefi (kapasite planı).
///
/// <para><b>Gerçekleşen = KİRALANABİLİR filo</b> (satılmış/pasif hariç). Satılmış aracı saymak
/// hedefi tutuyormuş gibi gösterirdi; "Kayıtlı" kolonu farkı görünür kılar.</para>
///
/// <para>Bağımsız oracle: beklenen sayımlar senaryodan ELLE yazılır.</para>
/// </summary>
[Collection("postgres")]
public sealed class FiloPlanTests(PostgresFixture fx)
{
    private static Task<Guid> AracAsync(IServiceProvider sp, string plaka, string? grup, string? sipp = null,
        VehicleStatus durum = VehicleStatus.Musait)
        => sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        { Plaka = plaka, Grup = grup, Sipp = sipp, Durum = durum });

    [Fact]
    public async Task Gerceklesen_sayim_ELLE_beklenen_degeri_verir_ve_SATILMIS_PASIF_haric()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        // ELLE: EKO grubunda 4 araç — 2 müsait, 1 kirada, 1 satılmış. Ayrıca 1 LUX.
        await AracAsync(sp, "34 FP 01", "EKO");
        await AracAsync(sp, "34 FP 02", "EKO");
        await AracAsync(sp, "34 FP 03", "EKO", durum: VehicleStatus.Kirada);
        await AracAsync(sp, "34 FP 04", "EKO", durum: VehicleStatus.Satildi);
        await AracAsync(sp, "34 FP 05", "EKO", durum: VehicleStatus.Pasif);
        await AracAsync(sp, "34 FP 06", "LUX");

        var svc = sp.GetRequiredService<FleetPlanService>();
        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 5 });
        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "LUX", HedefAdet = 1 });

        var rows = await svc.ListWithCountAsync();
        var eko = rows.Single(x => x.Hedef.AracGrupAdi == "EKO");
        var lux = rows.Single(x => x.Hedef.AracGrupAdi == "LUX");

        Assert.Equal(3, eko.Gerceklesen);      // ELLE: 2 müsait + 1 kirada (satılmış/pasif HARİÇ)
        Assert.Equal(5, eko.ToplamKayitli);    // ELLE: hepsi
        Assert.Equal(2, eko.Fark);             // ELLE: 5 − 3
        Assert.Equal("Eksik", eko.Durum);

        Assert.Equal(1, lux.Gerceklesen);
        Assert.Equal(0, lux.Fark);
        Assert.Equal("Tamam", lux.Durum);
    }

    [Fact]
    public async Task Grup_ve_SIPP_birlikte_verilince_kural_DARALIR()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;

        // ELLE: EKO/EDMR 2, EKO/CDAR 1, LUX/EDMR 1.
        await AracAsync(sp, "34 SP 01", "EKO", "EDMR");
        await AracAsync(sp, "34 SP 02", "EKO", "EDMR");
        await AracAsync(sp, "34 SP 03", "EKO", "CDAR");
        await AracAsync(sp, "34 SP 04", "LUX", "EDMR");

        var svc = sp.GetRequiredService<FleetPlanService>();
        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 3 });                 // yalnız grup
        await svc.CreateAsync(new FiloPlanInput { Sipp = "edmr", HedefAdet = 3 });                        // yalnız sipp (küçük harf)
        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", Sipp = "EDMR", HedefAdet = 3 });   // ikisi (AND)

        var rows = await svc.ListWithCountAsync();
        Assert.Equal(3, rows.Single(x => x.Hedef.AracGrupAdi == "EKO" && x.Hedef.Sipp is null).Gerceklesen);
        Assert.Equal(3, rows.Single(x => x.Hedef.AracGrupAdi is null).Gerceklesen);   // SIPP normalize → EDMR
        Assert.Equal(2, rows.Single(x => x.Hedef.AracGrupAdi == "EKO" && x.Hedef.Sipp == "EDMR").Gerceklesen);
    }

    [Fact]
    public async Task Grup_eslesmesi_TURKCE_harf_duyarsiz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        await AracAsync(sp, "34 TR 01", "İş Araçları");
        await AracAsync(sp, "34 TR 02", "iş araçları");

        var svc = sp.GetRequiredService<FleetPlanService>();
        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "İŞ ARAÇLARI", HedefAdet = 2 });

        // ELLE: iki yazım da aynı gruba sayılır (TurkishText — greenfield olduğu için doğru
        // karşılaştırıcı baştan seçildi).
        Assert.Equal(2, Assert.Single(await svc.ListWithCountAsync()).Gerceklesen);
    }

    [Fact]
    public async Task Artir_azalt_calisir_ve_SIFIRIN_ALTINA_inmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<FleetPlanService>();
        var id = await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 1 });

        Assert.True(await svc.ChangeTargetAsync(id, +1));
        Assert.Equal(2, (await svc.GetAsync(id))!.HedefAdet);

        Assert.True(await svc.ChangeTargetAsync(id, -1));
        Assert.True(await svc.ChangeTargetAsync(id, -1));
        Assert.Equal(0, (await svc.GetAsync(id))!.HedefAdet);

        // ELLE: 0'da bir daha azaltmak NEGATİFE düşürmez (negatif hedef "Fazla" kolonunu şişirirdi).
        Assert.True(await svc.ChangeTargetAsync(id, -1));
        Assert.Equal(0, (await svc.GetAsync(id))!.HedefAdet);
    }

    [Fact]
    public async Task Bos_hedef_ve_negatif_adet_reddedilir_ayni_hedef_IKI_KEZ_tanimlanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var svc = s.ServiceProvider.GetRequiredService<FleetPlanService>();

        // Grup ve SIPP ikisi de boş → TÜM filoyu sayan sessiz kural olurdu.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new FiloPlanInput { HedefAdet = 5 }));
        Assert.Contains("en az biri zorunludur", ex.Message);

        await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = -1 }));

        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 5 });
        // Doğal anahtar (grup, sipp, dönem) — NULL'lar da çakışır (NULLS NOT DISTINCT).
        var dup = await Assert.ThrowsAsync<ValidationException>(() =>
            svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 9 }));
        Assert.Contains("zaten tanımlı", dup.Message);

        // Farklı DÖNEM serbest — plan dönemsel olabilir.
        await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", Donem = "2026-Q2", HedefAdet = 9 });
        Assert.Equal(2, (await svc.ListWithCountAsync()).Count);
    }

    [Fact]
    public async Task Guncelle_sil_ve_yetki()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        Guid id;
        using (var s1 = host.ScopeFor(t1))
        {
            var svc = s1.ServiceProvider.GetRequiredService<FleetPlanService>();
            id = await svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 3 });
            Assert.True(await svc.UpdateAsync(id, new FiloPlanInput
            { AracGrupAdi = "LUX", Sipp = "cdar", Donem = " 2026-Q1 ", HedefAdet = 7, Aciklama = " not " }));
            var h = await svc.GetAsync(id);
            Assert.Equal("LUX", h!.AracGrupAdi);
            Assert.Equal("CDAR", h.Sipp);          // SIPP büyük harfe
            Assert.Equal("2026-Q1", h.Donem);      // trim
            Assert.Equal(7, h.HedefAdet);
            Assert.Equal("not", h.Aciklama);
        }

        // Muhasebe OKUYABİLİR (ViewReports) ama YAZAMAZ.
        using (var muh = host.ScopeFor(t1, Guid.NewGuid(), "muh", UserRole.Muhasebe))
        {
            var svc = muh.ServiceProvider.GetRequiredService<FleetPlanService>();
            Assert.Single(await svc.ListWithCountAsync());
            await Assert.ThrowsAsync<NoPermissionException>(() => svc.ChangeTargetAsync(id, 1));
            await Assert.ThrowsAsync<NoPermissionException>(() =>
                svc.CreateAsync(new FiloPlanInput { AracGrupAdi = "X", HedefAdet = 1 }));
        }

        // Operatör YAZABİLİR ve okuyabilir (ViewReports'u yok — RequireAny kilidi).
        using (var op = host.ScopeFor(t1, Guid.NewGuid(), "op", UserRole.Operator))
        {
            var svc = op.ServiceProvider.GetRequiredService<FleetPlanService>();
            Assert.Single(await svc.ListWithCountAsync());
            Assert.True(await svc.ChangeTargetAsync(id, 1));
            Assert.True(await svc.DeleteAsync(id));
        }

        using var s1b = host.ScopeFor(t1);
        Assert.Empty(await s1b.ServiceProvider.GetRequiredService<FleetPlanService>().ListWithCountAsync());
    }

    [Fact]
    public async Task Filo_plani_tenant_izolasyonlu_ve_sayim_SIZDIRMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
        {
            await AracAsync(s1.ServiceProvider, "34 TZ 01", "EKO");
            await s1.ServiceProvider.GetRequiredService<FleetPlanService>()
                .CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 1 });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var sp = s2.ServiceProvider;
        Assert.Empty(await sp.GetRequiredService<FleetPlanService>().ListWithCountAsync());

        // Diğer tenant kendi hedefini kurduğunda SAYIM da yalnız kendi araçlarını görmeli.
        await AracAsync(sp, "34 TZ 02", "EKO");
        await sp.GetRequiredService<FleetPlanService>()
            .CreateAsync(new FiloPlanInput { AracGrupAdi = "EKO", HedefAdet = 5 });
        var row = Assert.Single(await sp.GetRequiredService<FleetPlanService>().ListWithCountAsync());
        Assert.Equal(1, row.Gerceklesen);     // ELLE: kendi 1 aracı — diğer tenant'ınki sayılmadı
    }
}

/// <summary>
/// FAZ-19 Bölüm B — müsaitlik listesi zenginleştirmesi: "boştaki süre" ve "son müşteri".
///
/// <para><b>Sözleşme:</b> yalnız GERÇEKTEN dönmüş kiralar sayılır — geleceğe uzanan bir kira
/// "son kullanım" değildir (araç hâlâ o kirada olabilir). Hiç kiralanmamış araç sonuçta YER ALMAZ
/// (ekran "—" yazar; 0 yazmak "dün döndü" ile karışırdı).</para>
/// </summary>
[Collection("postgres")]
public sealed class MusaitlikSonKullanimTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Now = TestZaman.Simdi();

    [Fact]
    public async Task Son_kullanim_EN_SON_donen_kirayi_verir_gelecek_kira_SAYILMAZ()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var s = host.ScopeFor(Guid.NewGuid());
        var sp = s.ServiceProvider;
        var arac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 SK 10" });
        var bosArac = await sp.GetRequiredService<VehicleService>()
            .CreateAsync(new VehicleInput { Plaka = "34 SK 11" });

        var m1 = await sp.GetRequiredService<RentACar.Application.Customers.CustomerService>()
            .CreateAsync(new RentACar.Application.Customers.CustomerInput
            { Tip = CustomerType.Kurumsal, Unvan = "Eski Müşteri" });
        var m2 = await sp.GetRequiredService<RentACar.Application.Customers.CustomerService>()
            .CreateAsync(new RentACar.Application.Customers.CustomerInput
            { Tip = CustomerType.Kurumsal, Unvan = "Son Müşteri" });

        var kiralar = sp.GetRequiredService<RentACar.Application.Bookings.RentalService>();
        // ELLE: 30 gün önce dönen kira (eski), 10 gün önce dönen kira (son), gelecekte biten kira.
        await kiralar.CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
        { MusteriId = m1, VehicleId = arac, BasTar = Now.AddDays(-40), BitTar = Now.AddDays(-30), GunlukUcret = 100m });
        await kiralar.CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
        { MusteriId = m2, VehicleId = arac, BasTar = Now.AddDays(-20), BitTar = Now.AddDays(-10), GunlukUcret = 100m });
        await kiralar.CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
        { MusteriId = m1, VehicleId = arac, BasTar = Now.AddDays(-1), BitTar = Now.AddDays(9), GunlukUcret = 100m });

        var svc = sp.GetRequiredService<RentACar.Application.Availability.AvailabilityService>();
        var son = await svc.LastUsageAsync([arac, bosArac]);

        Assert.True(son.ContainsKey(arac));
        Assert.False(son.ContainsKey(bosArac));      // hiç kiralanmamış → satır YOK
        Assert.Equal("Son Müşteri", son[arac].MusteriAd);   // ELLE: 10 gün önce dönen
        Assert.Equal(10, RentACar.Application.Availability.AvailabilityService
            .IdleDays(son[arac].SonDonus, Now));            // ELLE: bugüne 10 gün
    }

    [Fact]
    public async Task Bosta_gun_hesabi_ELLE_dogrulanir()
    {
        // Saf fonksiyon.
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, RentACar.Application.Availability.AvailabilityService
            .IdleDays(new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero), now));   // aynı gün
        Assert.Equal(1, RentACar.Application.Availability.AvailabilityService
            .IdleDays(new DateTimeOffset(2026, 6, 14, 23, 0, 0, TimeSpan.Zero), now));
        Assert.Equal(30, RentACar.Application.Availability.AvailabilityService
            .IdleDays(new DateTimeOffset(2026, 5, 16, 0, 0, 0, TimeSpan.Zero), now));
        // Gelecek dönüş → negatif değil 0 (savunmacı).
        Assert.Equal(0, RentACar.Application.Availability.AvailabilityService
            .IdleDays(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), now));
    }

    [Fact]
    public async Task Son_kullanim_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        Guid arac;
        using (var s1 = host.ScopeFor(Guid.NewGuid()))
        {
            var sp = s1.ServiceProvider;
            arac = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 SK 12" });
            var m = await sp.GetRequiredService<RentACar.Application.Customers.CustomerService>()
                .CreateAsync(new RentACar.Application.Customers.CustomerInput
                { Tip = CustomerType.Kurumsal, Unvan = "Gizli" });
            await sp.GetRequiredService<RentACar.Application.Bookings.RentalService>()
                .CreateDirectAsync(new RentACar.Application.Bookings.BookingInput
                { MusteriId = m, VehicleId = arac, BasTar = Now.AddDays(-5), BitTar = Now.AddDays(-2), GunlukUcret = 100m });
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        // Araç id'si bilinse bile başka tenant'ın kirası görünmemeli.
        Assert.Empty(await s2.ServiceProvider
            .GetRequiredService<RentACar.Application.Availability.AvailabilityService>()
            .LastUsageAsync([arac]));
    }
}
