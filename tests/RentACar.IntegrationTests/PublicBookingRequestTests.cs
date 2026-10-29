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
    private static readonly DateTimeOffset Start = new(DateTime.UtcNow.Date.AddDays(10), TimeSpan.Zero);
    private static readonly DateTimeOffset Bit = new(DateTime.UtcNow.Date.AddDays(13), TimeSpan.Zero);

    private static PublicBookingRequestInput Input(string name = "Ayşe Yılmaz", string tel = "0555 111 22 33") => new()
    {
        AdSoyad = name, Telefon = tel, Email = "a@ornek.com",
        // PR-14: ilan/fiyat artık SUNUCUDAN çözülüyor (form değerine güvenilmez) → girdide yok.
        BasTar = Start, BitTar = Bit, Not = "Bebek koltuğu olsun",
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

    private static async Task<PublicBookingRequest> SingleRequestAsync(TestHost host, Guid tenantId)
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

        var request = await SingleRequestAsync(host, tenantId);
        Assert.Equal("Ayşe Yılmaz", request.AdSoyad);
        Assert.Equal(PublicBookingRequestDurum.Yeni, request.Durum);
        // PR-14: ilan bağlanmadan gelen talepte fiyat snapshot'ı YOKTUR (ilan bazlı akış
        // VitrinIlanTests'te test edilir) — doğrudan forma gelen talep hâlâ desteklenir.
        Assert.Null(request.GosterilenGunlukUcretKdvDahil);
        Assert.Null(request.DonusenReservationId);
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

        var unnamed = Input(name: "  ");
        Assert.Equal("ad_soyad", (await Assert.ThrowsAsync<BookingRequestException>(() => svc.CreateAsync(unnamed))).Code);

        var wireless = Input(tel: " ");
        Assert.Equal("telefon", (await Assert.ThrowsAsync<BookingRequestException>(() => svc.CreateAsync(wireless))).Code);

        var reverseDate = Input();
        (reverseDate.BasTar, reverseDate.BitTar) = (Bit, Start);
        Assert.Equal("tarih_sirasi", (await Assert.ThrowsAsync<BookingRequestException>(() => svc.CreateAsync(reverseDate))).Code);
    }

    // ---- PR #412 adversarial L2: talep tarih sınırları. ORACLE: İstanbul günü testte ayrıca hesaplanır (UTC+3 bölgesi),
    // sınırlar elle: alış günü < bugün → red; saatli alış şimdiden 2 sa önce → red; alış > +1 yıl → red; süre > 5 yıl → red.
    private static readonly TimeZoneInfo IstanbulZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>İstanbul'da bugünden <paramref name="offset"/> gün sonrası, saatsiz talebin yazıldığı biçimde (günün UTC gece yarısı).</summary>
    private static DateTimeOffset IstanbulDayAsUtcMidnight(int offset)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, IstanbulZone).DateTime).AddDays(offset);
        return new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
    }

    public static TheoryData<string, string> RejectedDates() => new()
    {
        { "dun_saatsiz", "tarih_gecmis" },
        { "iki_saat_once_saatli", "tarih_gecmis" },
        { "0001_01_01", "tarih_gecmis" },
        { "bir_yil_iki_gun_sonra", "tarih_ileri" },
        { "9999_12_31", "tarih_ileri" },
        { "bes_yil_bir_gun_sure", "sure_uzun" },
        { "bitis_9999", "sure_uzun" },
    };

    [Theory]
    [MemberData(nameof(RejectedDates))]
    public async Task Talep_tarihi_gecmiste_cok_ileride_ya_da_cok_uzunsa_kodla_reddedilir_yazilmaz(string scenario, string expectedCode)
    {
        var now = DateTimeOffset.UtcNow;
        var (start, end, hasTime) = scenario switch
        {
            "dun_saatsiz" => (IstanbulDayAsUtcMidnight(-1), IstanbulDayAsUtcMidnight(2), false),
            "iki_saat_once_saatli" => (now.AddHours(-2), now.AddDays(3), true),
            "0001_01_01" => (DateTimeOffset.MinValue, IstanbulDayAsUtcMidnight(3), false),
            "bir_yil_iki_gun_sonra" => (now.AddYears(1).AddDays(2), now.AddYears(1).AddDays(5), false),
            "9999_12_31" => (DateTimeOffset.MaxValue.AddMinutes(-1), DateTimeOffset.MaxValue, true),
            "bes_yil_bir_gun_sure" => (IstanbulDayAsUtcMidnight(10), IstanbulDayAsUtcMidnight(10).AddYears(5).AddDays(1), false),
            "bitis_9999" => (IstanbulDayAsUtcMidnight(10), DateTimeOffset.MaxValue, false),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using (var pub = host.ScopeFor(tenantId, role: null))
        {
            var input = Input();
            (input.BasTar, input.BitTar, input.HasTime) = (start, end, hasTime);
            var ex = await Assert.ThrowsAsync<BookingRequestException>(
                () => pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(input));
            Assert.Equal(expectedCode, ex.Code);
        }

        using var scope = host.ScopeFor(tenantId);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync());
    }

    [Fact]
    public async Task Talep_bugun_saatsiz_ve_bir_saat_sonra_saatli_kabul_edilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using (var pub = host.ScopeFor(tenantId, role: null))
        {
            var svc = pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
            var today = Input();
            (today.BasTar, today.BitTar) = (IstanbulDayAsUtcMidnight(0), IstanbulDayAsUtcMidnight(3));
            await svc.CreateAsync(today);

            var timed = Input(tel: "0555 999 88 77");
            (timed.BasTar, timed.BitTar, timed.HasTime) = (DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(3), true);
            await svc.CreateAsync(timed);
        }

        using var scope = host.ScopeFor(tenantId);
        Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync()).Count);
    }

    [Fact]
    public async Task Donustur_yeni_cari_ve_rezervasyon_olusturur_kaynak_web()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var request = await SingleRequestAsync(host, tenantId);

        Guid reservationId;
        using (var staff = host.ScopeFor(tenantId))
            reservationId = await staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>()
                .ConvertAsync(request.Id, vehicleId);

        using var verify = host.ScopeFor(tenantId);
        var after = Assert.Single(await verify.ServiceProvider.GetRequiredService<PublicBookingRequestService>().ListAsync());
        Assert.Equal(PublicBookingRequestDurum.Donustu, after.Durum);
        Assert.Equal(reservationId, after.DonusenReservationId);

        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var res = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);
        Assert.Equal("Web", res.Kaynak); // MasterDataSeeder'daki mevcut kaynak — atıf doğru
        Assert.Equal(vehicleId, res.VehicleId);

        var account = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == res.MusteriId);
        Assert.Equal("Ayşe Yılmaz", account.Ad);
        Assert.Equal("0555 111 22 33", account.CepTel);
    }

    [Fact]
    public async Task Donustur_telefonu_eslesen_mevcut_cariyi_yeniden_yaratmaz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);

        // Mevcut cari — telefon FARKLI FORMATTA yazılı (normalize eşleşme sınanır).
        Guid existingCustomerId;
        using (var staff = host.ScopeFor(tenantId))
            existingCustomerId = await staff.ServiceProvider.GetRequiredService<CustomerService>().CreateAsync(
                new CustomerInput { Tip = CustomerType.Bireysel, Ad = "Eski Müşteri", CepTel = "05551112233" });

        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input(tel: "0555 111 22 33"));
        var request = await SingleRequestAsync(host, tenantId);

        Guid reservationId;
        using (var staff = host.ScopeFor(tenantId))
            reservationId = await staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>()
                .ConvertAsync(request.Id, vehicleId);

        using var verify = host.ScopeFor(tenantId);
        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var res = await db.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);

        Assert.Equal(existingCustomerId, res.MusteriId);                 // MEVCUT cari kullanıldı
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
        var request = await SingleRequestAsync(host, tenantId);

        // İki personel AYNI ANDA "Dönüştür"e basıyor — ayrı scope'lar (ayrı DbContext'ler).
        using var s1 = host.ScopeFor(tenantId);
        using var s2 = host.ScopeFor(tenantId);
        var svc1 = s1.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        var svc2 = s2.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

        var results = await Task.WhenAll(
            Try(() => svc1.ConvertAsync(request.Id, vehicleId)),
            Try(() => svc2.ConvertAsync(request.Id, vehicleId)));

        Assert.Single(results, r => r.Basarili);  // YALNIZ BİRİ geçer (atomik claim)
        Assert.Single(results, r => !r.Basarili);

        using var verify = host.ScopeFor(tenantId);
        var factory = verify.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Equal(1, await db.Reservations.AsNoTracking().CountAsync()); // TEK rezervasyon — çift-kayıt YOK
        Assert.Equal(1, await db.Customers.AsNoTracking().CountAsync());    // TEK cari
    }

    private static async Task<(bool Basarili, Exception? Hata)> Try(Func<Task<Guid>> f)
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

        // GERÇEKÇİ hata senaryosu: talep bekletilirken tarihi GEÇMİŞ. Dönüştürme `DatePolicy.ReservationStart` ile
        // REDDEDİLİR → claim GERİ ALINMALI. Talep doğrudan repoya yazılır: geçmiş tarihli talep artık site
        // yolundan (CreateAsync) giremiyor (PR #412 L2), ama bekleyen talebin tarihi zamanla yine geçer.
        using (var pub = host.ScopeFor(tenantId, role: null))
        {
            await pub.ServiceProvider.GetRequiredService<IPublicBookingRequestRepository>().AddAsync(new PublicBookingRequest
            {
                AdSoyad = "Ayşe Yılmaz", Telefon = "0555 111 22 33", Durum = PublicBookingRequestDurum.Yeni,
                BasTar = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-30), TimeSpan.Zero),
                BitTar = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-27), TimeSpan.Zero),
            });
        }
        var request = await SingleRequestAsync(host, tenantId);

        using (var staff = host.ScopeFor(tenantId))
            await Assert.ThrowsAnyAsync<ValidationException>(
                () => staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>()
                    .ConvertAsync(request.Id, vehicleId));

        var after = await SingleRequestAsync(host, tenantId);
        Assert.Equal(PublicBookingRequestDurum.Yeni, after.Durum); // YARIM kalmadı — tekrar denenebilir
        Assert.Null(after.DonusenReservationId);

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
        var request = await SingleRequestAsync(host, tenantId);

        using var staff = host.ScopeFor(tenantId);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        await svc.RejectAsync(request.Id);

        var after = Assert.Single(await svc.ListAsync());
        Assert.Equal(PublicBookingRequestDurum.Reddedildi, after.Durum);
        Assert.Null(after.DonusenReservationId);

        await Assert.ThrowsAsync<ValidationException>(() => svc.RejectAsync(request.Id)); // zaten işlenmiş
    }

    [Fact]
    public async Task Reddedilen_talep_donusturulemez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        var vehicleId = await SeedVehicleAsync(host, tenantId);
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var request = await SingleRequestAsync(host, tenantId);

        using var staff = host.ScopeFor(tenantId);
        var svc = staff.ServiceProvider.GetRequiredService<PublicBookingRequestService>();
        await svc.RejectAsync(request.Id);

        await Assert.ThrowsAsync<ValidationException>(() => svc.ConvertAsync(request.Id, vehicleId));
    }

    [Fact]
    public async Task Staff_islemleri_OperationsWrite_ister()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenantId = Guid.NewGuid();
        using (var pub = host.ScopeFor(tenantId, role: null))
            await pub.ServiceProvider.GetRequiredService<PublicBookingRequestService>().CreateAsync(Input());
        var request = await SingleRequestAsync(host, tenantId);

        using var accounting = host.ScopeFor(tenantId, role: UserRole.Muhasebe); // OperationsWrite YOK
        var svc = accounting.ServiceProvider.GetRequiredService<PublicBookingRequestService>();

        await Assert.ThrowsAsync<NoPermissionException>(() => svc.ListAsync());
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.RejectAsync(request.Id));
        await Assert.ThrowsAsync<NoPermissionException>(() => svc.ConvertAsync(request.Id, Guid.NewGuid()));
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
