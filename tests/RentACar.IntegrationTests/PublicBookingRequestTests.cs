using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.PublicSite;
using RentACar.Application.Vehicles;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// PR-8: halka açık site talebi (lead) + dönüştürme. BAĞIMSIZ ORACLE. `CreateAsync` repo'daki İLK
/// anonim YAZMA yolu (Role=null ile çağrılır); dönüştürme claim/release deseniyle yarış-güvenli.
/// </summary>
[Collection("postgres")]
public sealed class PublicBookingRequestTests(PostgresFixture fx)
{
    // UTC offset ŞART: `DateTimeOffset.UtcNow.Date` bir DateTime (Kind=Unspecified) döndürür ve implicit
    // dönüşümde YEREL offset (+03:00) alır → Npgsql `timestamptz`'e yazamaz. Tarihler now-göreli (ileri)
    // çünkü TarihPolitikasi rezervasyon başlangıcını geçmişe KAPALI tutuyor.
    private static readonly DateTimeOffset Bas = new(DateTime.UtcNow.Date.AddDays(10), TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(DateTime.UtcNow.Date.AddDays(13), TimeSpan.Zero);

    private static PublicBookingRequestInput Input(string ad = "Ayşe Yılmaz", string tel = "0555 111 22 33") => new()
    {
        AdSoyad = ad, Telefon = tel, Email = "a@ornek.com",
        // PR-14: ilan/fiyat artık SUNUCUDAN çözülüyor (form değerine güvenilmez) → girdide yok.
        BasTar = Bas, BitTar = Bit, Not = "Bebek koltuğu olsun",
    };

    private static async Task<Guid> SeedVehicleAsync(TestHost host, Guid tenantId)
    {
        using var scope = host.ScopeFor(tenantId);
        return await scope.ServiceProvider.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput
        {
            Plaka = "34TL" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            Durum = VehicleStatus.Musait, Grup = "Ekonomik",
        });
    }

    private static async Task<PublicBookingRequest> TekTalepAsync(TestHost host, Guid tenantId)
    {
        using var scope = host.ScopeFor(tenantId);
        var list = await scope.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync();
        return Assert.Single(list);
    }

    [Fact]
    public async Task Anonim_ziyaretci_guard_siz_talep_olusturabilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        using (var pub = host.ScopeFor(tenantId, role: null)) // PublicTenantContext'in gerçek şekli
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());

        var talep = await TekTalepAsync(host, tenantId);
        Assert.Equal("Ayşe Yılmaz", talep.AdSoyad);
        Assert.Equal(PublicBookingRequestDurum.Yeni, talep.Durum);
        // PR-14: ilan bağlanmadan gelen talepte fiyat snapshot'ı YOKTUR (ilan bazlı akış
        // VitrinIlanTests'te test edilir) — doğrudan forma gelen talep hâlâ desteklenir.
        Assert.Null(talep.GosterilenGunlukUcretKdvDahil);
        Assert.Null(talep.DonusenReservationId);
    }

    [Fact]
    public async Task Honeypot_dolu_ise_DB_ye_hicbir_sey_yazilmaz_ama_hata_da_verilmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();

        using (var pub = host.ScopeFor(tenantId, role: null))
        {
            var input = Input();
            input.Website = "http://spam.example"; // bot doldurdu
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(input); // İSTİSNA YOK
        }

        using var scope = host.ScopeFor(tenantId);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync());
    }

    [Fact]
    public async Task Eksik_zorunlu_alan_veya_ters_tarih_reddedilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var pub = host.ScopeFor(Guid.NewGuid(), role: null);
        var svc = pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

        var adsiz = Input(ad: "  ");
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(adsiz));

        var telsiz = Input(tel: " ");
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(telsiz));

        var tersTarih = Input();
        (tersTarih.BasTar, tersTarih.BitTar) = (Bit, Bas);
        await Assert.ThrowsAsync<ValidationException>(() => svc.CreateAsync(tersTarih));
    }

    [Fact]
    public async Task Donustur_yeni_cari_ve_rezervasyon_olusturur_kaynak_web()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var talep = await TekTalepAsync(host, tenantId);

        Guid reservationId;
        using (var staff = host.ScopeFor(tenantId))
            reservationId = await staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>()
                .DonusturAsync(talep.Id, vehicleId);

        using var verify = host.ScopeFor(tenantId);
        var sonra = Assert.Single(await verify.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync());
        Assert.Equal(PublicBookingRequestDurum.Donustu, sonra.Durum);
        Assert.Equal(reservationId, sonra.DonusenReservationId);

        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rez = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);
        Assert.Equal("Web", rez.Kaynak); // MasterDataSeeder'daki mevcut kaynak — atıf doğru
        Assert.Equal(vehicleId, rez.VehicleId);

        var cari = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == rez.MusteriId);
        Assert.Equal("Ayşe Yılmaz", cari.Ad);
        Assert.Equal("0555 111 22 33", cari.CepTel);
    }

    [Fact]
    public async Task Donustur_telefonu_eslesen_mevcut_cariyi_yeniden_yaratmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        // Mevcut cari — telefon FARKLI FORMATTA yazılı (normalize eşleşme sınanır).
        Guid mevcutCariId;
        using (var staff = host.ScopeFor(tenantId))
            mevcutCariId = await staff.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(
                new CustomerInput { Tip = CariType.Bireysel, Ad = "Eski Müşteri", CepTel = "05551112233" });

        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input(tel: "0555 111 22 33"));
        var talep = await TekTalepAsync(host, tenantId);

        Guid reservationId;
        using (var staff = host.ScopeFor(tenantId))
            reservationId = await staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>()
                .DonusturAsync(talep.Id, vehicleId);

        using var verify = host.ScopeFor(tenantId);
        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var rez = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);

        Assert.Equal(mevcutCariId, rez.MusteriId);                 // MEVCUT cari kullanıldı
        Assert.Equal(1, await db.Customers.AsNoTracking().CountAsync()); // mükerrer cari YOK
    }

    [Fact]
    public async Task Escaman_iki_donustur_yalniz_biri_basarili_tek_rezervasyon_olusur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var talep = await TekTalepAsync(host, tenantId);

        // İki personel AYNI ANDA "Dönüştür"e basıyor — ayrı scope'lar (ayrı DbContext'ler).
        using var s1 = host.ScopeFor(tenantId);
        using var s2 = host.ScopeFor(tenantId);
        var svc1 = s1.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        var svc2 = s2.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

        var sonuclar = await Task.WhenAll(
            Dene(() => svc1.DonusturAsync(talep.Id, vehicleId)),
            Dene(() => svc2.DonusturAsync(talep.Id, vehicleId)));

        Assert.Single(sonuclar, r => r.Basarili);  // YALNIZ BİRİ geçer (atomik claim)
        Assert.Single(sonuclar, r => !r.Basarili);

        using var verify = host.ScopeFor(tenantId);
        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Reservations.AsNoTracking().CountAsync()); // TEK rezervasyon — çift-kayıt YOK
        Assert.Equal(1, await db.Customers.AsNoTracking().CountAsync());    // TEK cari
    }

    private static async Task<(bool Basarili, Exception? Hata)> Dene(Func<Task<Guid>> f)
    {
        try { await f(); return (true, null); }
        catch (Exception ex) { return (false, ex); }
    }

    [Fact]
    public async Task Donustur_basarisiz_olursa_claim_geri_alinir_talep_yeniden_islenebilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        // GERÇEKÇİ hata senaryosu: talep GEÇMİŞ tarihlerle gelmiş (ziyaretçi eski tarih seçmiş ya da
        // talep bekletilirken tarihi geçmiş). Talep kaydı buna izin verir (lead, rezervasyon değil) ama
        // dönüştürme `TarihPolitikasi.RezervasyonBaslangic` ile REDDEDİLİR → claim GERİ ALINMALI.
        using (var pub = host.ScopeFor(tenantId, role: null))
        {
            var gecmis = Input();
            gecmis.BasTar = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-30), TimeSpan.Zero);
            gecmis.BitTar = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-27), TimeSpan.Zero);
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(gecmis);
        }
        var talep = await TekTalepAsync(host, tenantId);

        using (var staff = host.ScopeFor(tenantId))
            await Assert.ThrowsAnyAsync<ValidationException>(
                () => staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>()
                    .DonusturAsync(talep.Id, vehicleId));

        var sonra = await TekTalepAsync(host, tenantId);
        Assert.Equal(PublicBookingRequestDurum.Yeni, sonra.Durum); // YARIM kalmadı — tekrar denenebilir
        Assert.Null(sonra.DonusenReservationId);

        // Yan etki bırakmamalı: ne cari ne rezervasyon oluşmuş olmalı.
        using var verify = host.ScopeFor(tenantId);
        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(0, await db.Reservations.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Reddet_durumu_degistirir_rezervasyon_olusturmaz_ve_tekrarlanamaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var talep = await TekTalepAsync(host, tenantId);

        using var staff = host.ScopeFor(tenantId);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        await svc.ReddetAsync(talep.Id);

        var sonra = Assert.Single(await svc.ListAsync());
        Assert.Equal(PublicBookingRequestDurum.Reddedildi, sonra.Durum);
        Assert.Null(sonra.DonusenReservationId);

        await Assert.ThrowsAsync<ValidationException>(() => svc.ReddetAsync(talep.Id)); // zaten işlenmiş
    }

    [Fact]
    public async Task Reddedilen_talep_donusturulemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var talep = await TekTalepAsync(host, tenantId);

        using var staff = host.ScopeFor(tenantId);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        await svc.ReddetAsync(talep.Id);

        await Assert.ThrowsAsync<ValidationException>(() => svc.DonusturAsync(talep.Id, vehicleId));
    }

    [Fact]
    public async Task Staff_islemleri_OperationsWrite_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var talep = await TekTalepAsync(host, tenantId);

        using var muhasebe = host.ScopeFor(tenantId, role: UserRole.Muhasebe); // OperationsWrite YOK
        var svc = muhasebe.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

        await Assert.ThrowsAsync<ValidationException>(() => svc.ListAsync());
        await Assert.ThrowsAsync<ValidationException>(() => svc.ReddetAsync(talep.Id));
        await Assert.ThrowsAsync<ValidationException>(() => svc.DonusturAsync(talep.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task Talepler_tenant_izoledir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        using (var pub = host.ScopeFor(t1, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());

        using var staff2 = host.ScopeFor(t2);
        Assert.Empty(await staff2.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync());
    }
}
