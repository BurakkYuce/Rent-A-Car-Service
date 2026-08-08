using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Reporting;
using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ-77 — filo şube kırılımı + gün-bazlı doluluk. D4 deseni: bu iki rapor PARA/DEFTER TOPLAMI
/// YAPMAZ; sayılan şey araç adedi ve gün adedidir, tek kaynaktan (çift-sayım riski yok).
///
/// <para><b>Bağımsız oracle:</b> her beklenen sayı senaryodan ELLE türetilir; hiçbiri servisin
/// kendi çıktısından okunmaz.</para>
///
/// <para><b>Geriye uyum:</b> mevcut <c>GetFleetUtilizationAsync</c> / <c>GetDolulukAsync</c>
/// imzaları ve sonuçları değişmedi — aynı veri kümesinde çapraz doğrulanıyor.</para>
/// </summary>
[Collection("postgres")]
public sealed class FiloDolulukDerinlikTests(PostgresFixture fx)
{
    private static DateTimeOffset D(int gun) => new(2026, 6, gun, 0, 0, 0, TimeSpan.Zero);

    private static Vehicle Arac(string plaka, string? sube, VehicleStatus durum,
        string? grup = null, FiloStatus? filo = null)
        => new() { Plaka = plaka, Sube = sube, Durum = durum, Grup = grup, FiloDurum = filo };

    private static RentalContract Kira(string no, Guid arac, DateTimeOffset bas, DateTimeOffset bit,
        RentalStatus durum = RentalStatus.Kirada, DateTimeOffset? gercekDonus = null)
        => new()
        {
            SozlesmeNo = no, MusteriId = Guid.NewGuid(), VehicleId = arac, Durum = durum,
            BasTar = bas, BitTar = bit, GercekDonusTar = gercekDonus, CreatedAtUtc = bas
        };

    // ---------------- A) Şube kırılımlı filo ----------------

    [Fact]
    public async Task Filo_sube_kirilimi_ELLE_kurulan_sayimi_dondurur_ve_TOPLAM_eski_APIye_esit()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        // ELLE: A şubesi 3 araç (Musait, Kirada, Serviste), B şubesi 2 araç (Kirada, Satildi),
        // şubesiz 1 araç (Pasif).
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Vehicles.AddRange(
                Arac("34A001", "Sube A", VehicleStatus.Musait),
                Arac("34A002", "Sube A", VehicleStatus.Kirada),
                Arac("34A003", "Sube A", VehicleStatus.Serviste),
                Arac("34B001", "Sube B", VehicleStatus.Kirada),
                Arac("34B002", "Sube B", VehicleStatus.Satildi, filo: FiloStatus.IkinciElSatis),
                Arac("34X001", null, VehicleStatus.Pasif));
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var r = await svc.GetFleetUtilizationBySubeAsync();

        Assert.Equal(3, r.Satirlar.Count);
        var a = r.Satirlar.Single(x => x.Sube == "Sube A");
        var b = r.Satirlar.Single(x => x.Sube == "Sube B");
        var x = r.Satirlar.Single(x => x.Sube == "(Atanmamış)");

        Assert.Equal(3, a.Filo);
        Assert.Equal(1, a.Bos);
        Assert.Equal(1, a.Kirada);
        Assert.Equal(1, a.Bakimda);
        Assert.Equal(0, a.Satildi);
        // ELLE: kullanılabilir = 3 − 0 satılmış − 0 pasif = 3; 1/3 = %33,33
        Assert.Equal(33.33m, a.DolulukYuzde);

        Assert.Equal(2, b.Filo);
        Assert.Equal(1, b.Satildi);
        Assert.Equal(1, b.Satilik);            // FiloDurum = IkinciElSatis
        // ELLE: kullanılabilir = 2 − 1 = 1; 1/1 = %100 (satılmış araç PAYDADAN düşer)
        Assert.Equal(100m, b.DolulukYuzde);

        // ELLE: şubesiz araç tek pasif → kullanılabilir 0 → oran YOK (0 değil, null)
        Assert.Equal(1, x.Filo);
        Assert.Null(x.DolulukYuzde);

        // Geriye uyum: eski parametresiz API aynı veriyle değişmeden çalışıyor ve toplamlar tutuyor.
        var eski = await svc.GetFleetUtilizationAsync();
        Assert.Equal(6, eski.Toplam);
        Assert.Equal(eski.Toplam, r.ToplamFilo);
        Assert.Equal(eski.Kirada, r.Satirlar.Sum(s => s.Kirada));
        Assert.Equal(eski.Serviste, r.Satirlar.Sum(s => s.Bakimda));
        Assert.Equal(eski.Satildi, r.Satirlar.Sum(s => s.Satildi));
        Assert.Equal(eski.Pasif, r.Satirlar.Sum(s => s.Pasif));
    }

    [Fact]
    public async Task Filo_kira_kolonlari_ARACIN_subesine_yazilir_cikis_ofisine_DEGIL()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        var bugun = DateTimeOffset.UtcNow.UtcDateTime.Date;
        Guid aracA;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var va = Arac("34A001", "Sube A", VehicleStatus.Kirada);
            var vb = Arac("34B001", "Sube B", VehicleStatus.Musait);
            db.Vehicles.AddRange(va, vb);
            aracA = va.Id;

            // A'nın aracı, ama sözleşmenin ÇIKIŞ OFİSİ B şubesinde. Kira A'ya yazılmalı.
            var k = Kira("KS-1", va.Id,
                new DateTimeOffset(bugun, TimeSpan.Zero),
                new DateTimeOffset(bugun.AddDays(3), TimeSpan.Zero));
            k.CikisOfisi = "Sube B";
            db.Rentals.Add(k);
            await db.SaveChangesAsync();
        }

        var r = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetFleetUtilizationBySubeAsync();

        var a = r.Satirlar.Single(x => x.Sube == "Sube A");
        var b = r.Satirlar.Single(x => x.Sube == "Sube B");
        Assert.Equal(1, a.Cikislar);      // bugün başlayan kira ARACIN şubesinde
        Assert.Equal(0, b.Cikislar);      // çıkış ofisi B olsa da B'ye YAZILMAZ
        Assert.Equal(1, a.Donecekler);    // bugün+3 pencere içinde
        Assert.Equal(0, a.Donusler);
        Assert.NotEqual(Guid.Empty, aracA);
    }

    [Fact]
    public async Task Filo_BAF_yalniz_ACIK_olanlari_ve_SATILIK_yalniz_IkinciElSatisi_sayar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var v1 = Arac("34A001", "Sube A", VehicleStatus.Musait, filo: FiloStatus.IkinciElSatis);
            var v2 = Arac("34A002", "Sube A", VehicleStatus.Musait, filo: FiloStatus.Havuz);
            db.Vehicles.AddRange(v1, v2);
            db.Baflar.AddRange(
                new Baf { No = "BAF-000001", VehicleId = v1.Id, Durum = BafDurum.Acik },
                new Baf { No = "BAF-000002", VehicleId = v2.Id, Durum = BafDurum.Acik },
                new Baf { No = "BAF-000003", VehicleId = v2.Id, Durum = BafDurum.Kapandi });
            await db.SaveChangesAsync();
        }

        var a = (await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetFleetUtilizationBySubeAsync()).Satirlar.Single(x => x.Sube == "Sube A");

        Assert.Equal(2, a.Baf);        // ELLE: 3 BAF'tan yalnız 2'si Açık
        Assert.Equal(1, a.Satilik);    // ELLE: 2 araçtan yalnız 1'i IkinciElSatis
    }

    // ---------------- B) Gün-kırılımlı doluluk ----------------

    [Fact]
    public async Task Gunluk_doluluk_TOPLAMI_eski_donem_APIsiyle_AYNI()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var v1 = Arac("34A001", "Sube A", VehicleStatus.Kirada);
            var v2 = Arac("34B001", "Sube B", VehicleStatus.Kirada);
            db.Vehicles.AddRange(v1, v2);
            // ELLE: 3–7 (5 gün), 6–8 (3 gün), dönem dışı 15–18 (0 gün), iptal 2–9 (0 gün).
            db.Rentals.AddRange(
                Kira("KS-A", v1.Id, D(3), D(7)),
                Kira("KS-B", v2.Id, D(6), D(8)),
                Kira("KS-OUT", v1.Id, D(15), D(18)),
                Kira("KS-IPT", v2.Id, D(2), D(9), RentalStatus.Iptal));
            await db.SaveChangesAsync();
        }

        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();
        var donem = await svc.GetDolulukAsync(D(1), D(10));
        var gunluk = await svc.GetDolulukGunlukAsync(D(1), D(10));

        Assert.Equal(8, donem.KiraGun);                       // ELLE: 5 + 3
        Assert.Equal(10, gunluk.DonemGun);
        Assert.Equal(10, gunluk.Satirlar.Count);              // kırılımsız → gün başına 1 satır
        // AYNI OverlapDays helper'ı → gün toplamı dönem toplamına EŞİT olmalı (kalıcı kilit).
        Assert.Equal(donem.KiraGun, gunluk.ToplamKiraGun);

        // ELLE gün gün: 1,2 → 0 · 3,4,5 → 1 (A) · 6,7 → 2 (A+B) · 8 → 1 (B) · 9,10 → 0
        int[] beklenen = [0, 0, 1, 1, 1, 2, 2, 1, 0, 0];
        Assert.Equal(beklenen, gunluk.Satirlar.Select(x => x.KiraGun).ToArray());

        // Payda 2 araç → 6. günde 2/2 = %100, 3. günde 1/2 = %50, 1. günde %0.
        Assert.Equal(100m, gunluk.Satirlar.Single(x => x.Gun == new DateOnly(2026, 6, 6)).KiraYuzde);
        Assert.Equal(50m, gunluk.Satirlar.Single(x => x.Gun == new DateOnly(2026, 6, 3)).KiraYuzde);
        Assert.Equal(0m, gunluk.Satirlar.Single(x => x.Gun == new DateOnly(2026, 6, 1)).KiraYuzde);
    }

    [Fact]
    public async Task Sube_kirilimi_KENDI_paydasiyla_hesaplar_karisik_payda_YOK()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            // ELLE: A şubesi 1 araç, B şubesi 3 araç.
            var a1 = Arac("34A001", "Sube A", VehicleStatus.Kirada, "EKO");
            var b1 = Arac("34B001", "Sube B", VehicleStatus.Kirada, "EKO");
            var b2 = Arac("34B002", "Sube B", VehicleStatus.Musait, "LUX");
            var b3 = Arac("34B003", "Sube B", VehicleStatus.Musait, "LUX");
            db.Vehicles.AddRange(a1, b1, b2, b3);
            db.Rentals.AddRange(
                Kira("KS-A", a1.Id, D(1), D(2)),      // A aracı, 1-2
                Kira("KS-B", b1.Id, D(1), D(1)));     // B aracı, yalnız 1
            await db.SaveChangesAsync();
        }

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetDolulukGunlukAsync(D(1), D(2), DolulukBoyut.Sube);

        Assert.Equal(4, g.Satirlar.Count);     // 2 gün × 2 şube
        var a1g = g.Satirlar.Single(x => x.Seri == "Sube A" && x.Gun == new DateOnly(2026, 6, 1));
        var b1g = g.Satirlar.Single(x => x.Seri == "Sube B" && x.Gun == new DateOnly(2026, 6, 1));
        var b2g = g.Satirlar.Single(x => x.Seri == "Sube B" && x.Gun == new DateOnly(2026, 6, 2));

        Assert.Equal(1, a1g.AracSayisi);
        Assert.Equal(100m, a1g.KiraYuzde);              // ELLE: 1/1
        Assert.Equal(3, b1g.AracSayisi);                // payda B'nin KENDİ araçları
        Assert.Equal(33.33m, b1g.KiraYuzde);            // ELLE: 1/3
        Assert.Equal(0m, b2g.KiraYuzde);                // 2. gün B'de kira yok

        // Şube yüzdelerinin toplamı genel doluluğa EŞİT DEĞİLDİR (havuz KPI dersi) — DTO bunu yazıyor.
        Assert.Contains("KENDİ araçları", g.PaydaAciklama);

        // Gün toplamı yine kırılımsız toplamla aynı (kırılım kaybetmez).
        var duz = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetDolulukGunlukAsync(D(1), D(2));
        Assert.Equal(duz.ToplamKiraGun, g.ToplamKiraGun);
    }

    [Fact]
    public async Task Arac_grubu_kirilimi_ve_gruprsuz_arac_ATANMAMIS_kovasina_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var e1 = Arac("34E001", "Sube A", VehicleStatus.Kirada, "EKO");
            var e2 = Arac("34E002", "Sube A", VehicleStatus.Musait, "EKO");
            var x1 = Arac("34X001", "Sube A", VehicleStatus.Kirada, grup: null);
            db.Vehicles.AddRange(e1, e2, x1);
            db.Rentals.AddRange(Kira("KS-E", e1.Id, D(1), D(1)), Kira("KS-X", x1.Id, D(1), D(1)));
            await db.SaveChangesAsync();
        }

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetDolulukGunlukAsync(D(1), D(1), DolulukBoyut.AracGrubu);

        Assert.Equal(2, g.Satirlar.Count);   // EKO + (Atanmamış)
        var eko = g.Satirlar.Single(x => x.Seri == "EKO");
        var bos = g.Satirlar.Single(x => x.Seri == "(Atanmamış)");
        Assert.Equal(2, eko.AracSayisi);
        Assert.Equal(50m, eko.KiraYuzde);    // ELLE: 1/2
        Assert.Equal(1, bos.AracSayisi);
        Assert.Equal(100m, bos.KiraYuzde);   // ELLE: 1/1 — grupsuz araç sessizce kaybolmadı
    }

    [Fact]
    public async Task Rezervasyon_kaynak_kirilimi_TUM_FILO_paydasi_kullanir_ve_kira_AYRI_kovada()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var v1 = Arac("34A001", "Sube A", VehicleStatus.Kirada);
            var v2 = Arac("34A002", "Sube A", VehicleStatus.Musait);
            var v3 = Arac("34A003", "Sube A", VehicleStatus.Musait);
            var v4 = Arac("34A004", "Sube A", VehicleStatus.Musait);
            db.Vehicles.AddRange(v1, v2, v3, v4);
            db.Rentals.Add(Kira("KS-1", v1.Id, D(1), D(1)));
            db.Reservations.AddRange(
                new Reservation { ReservationNo = "RZ-000001", MusteriId = Guid.NewGuid(), VehicleId = v2.Id, BasTar = D(1), BitTar = D(1), Kaynak = "Web" },
                new Reservation { ReservationNo = "RZ-000002", MusteriId = Guid.NewGuid(), VehicleId = v3.Id, BasTar = D(1), BitTar = D(1), Kaynak = "Telefon" },
                // Kiraya çevrilmiş rezervasyon: kira tarafında sayıldığı için REZ olarak sayılmamalı.
                new Reservation { ReservationNo = "RZ-000003", MusteriId = Guid.NewGuid(), VehicleId = v4.Id, BasTar = D(1), BitTar = D(1), Kaynak = "Web", Durum = ReservationStatus.KirayaCevrildi });
            await db.SaveChangesAsync();
        }

        var g = await scope.ServiceProvider.GetRequiredService<ReportService>()
            .GetDolulukGunlukAsync(D(1), D(1), DolulukBoyut.RezervasyonKaynagi);

        // Seriler: Web, Telefon + kiraların ayrı kovası.
        var web = g.Satirlar.Single(x => x.Seri == "Web");
        var tel = g.Satirlar.Single(x => x.Seri == "Telefon");
        var kira = g.Satirlar.Single(x => x.Seri == ReportService.TumFiloKira);

        Assert.Equal(4, web.AracSayisi);          // payda TÜM FİLO (kaynak filo bölüntüsü değil)
        Assert.Equal(1, web.RezGun);              // ELLE: kiraya çevrilen SAYILMADI
        Assert.Equal(25m, web.RezYuzde);          // ELLE: 1/4
        Assert.Equal(1, tel.RezGun);
        Assert.Equal(0, web.KiraGun);             // kira kaynak satırına yazılmaz
        Assert.Equal(1, kira.KiraGun);            // kiralar ayrı kovada
        Assert.Equal(0, kira.RezGun);
        Assert.Contains("TÜM FİLO", g.PaydaAciklama);
    }

    [Fact]
    public async Task Ters_aralik_duzeltilir_ve_asiri_genis_donem_kirpilir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var svc = scope.ServiceProvider.GetRequiredService<ReportService>();

        var ters = await svc.GetDolulukGunlukAsync(D(10), D(1));
        Assert.Equal(10, ters.DonemGun);          // boş sayfa yerine düzeltilir

        var genis = await svc.GetDolulukGunlukAsync(D(1), D(1).AddYears(5));
        Assert.Equal(ReportService.DolulukMaxGun, genis.DonemGun);
    }

    [Fact]
    public async Task Filo_ve_doluluk_kirilimlari_tenant_izolasyonlu()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var t1 = Guid.NewGuid();
        using (var s1 = host.ScopeFor(t1))
        {
            var f = s1.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await f.CreateDbContextAsync();
            var v = Arac("34G001", "Gizli Sube", VehicleStatus.Kirada, "GIZLI");
            db.Vehicles.Add(v);
            db.Rentals.Add(Kira("KS-G", v.Id, D(1), D(5)));
            await db.SaveChangesAsync();
        }

        using var s2 = host.ScopeFor(Guid.NewGuid());
        var svc = s2.ServiceProvider.GetRequiredService<ReportService>();
        Assert.Empty((await svc.GetFleetUtilizationBySubeAsync()).Satirlar);
        var g = await svc.GetDolulukGunlukAsync(D(1), D(5), DolulukBoyut.Sube);
        Assert.Equal(0, g.ToplamKiraGun);
        Assert.DoesNotContain(g.Satirlar, x => x.Seri == "Gizli Sube");
    }
}
