using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.AracKredileri;
using RentACar.Application.Common;
using RentACar.Application.Kur;
using RentACar.Application.Periods;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ1-1.3 — Kredi taksiti GERÇEK GİDER olur (Expense Tip=Finansman + Borç Gider/Alacak Kasa-Banka,
/// sayaçla AYNI transaction). BAĞIMSIZ ORACLE (elle): 12.000 kredi %20 faiz 12 taksit →
/// toplamFaiz = 12.000×0,20×12/12 = 2.400; aylık = 14.400/12 = 1.200. Karne gider-kategorisi
/// "Finansman"; araçsız kredi → Atanmamış; çift-submit anahtar reddi (sayaç DA geri alınır);
/// dönem kilidi; EUR kredi 1.1 kur sözleşmesi; yetki artık FinanceWrite.
/// </summary>
[Collection("postgres")]
public sealed class AracKrediGiderTests(PostgresFixture fx)
{
    private static async Task<Guid> KrediAsync(IServiceProvider sp, Guid? vehicleId,
        decimal tutar = 12_000m, decimal faiz = 0.20m, int taksit = 12, string doviz = "TRY")
        => await sp.GetRequiredService<AracKrediService>().CreateAsync(new AracKrediInput
        {
            BankaAdi = "Banka", VehicleId = vehicleId, KrediTutari = tutar,
            FaizOran = faiz, TaksitSayisi = taksit, Doviz = doviz, Kur = 1m
        });

    [Fact]
    public async Task Taksit_odeme_gider_postlar_ve_karneye_duser()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KG 01" });
        var krediId = await KrediAsync(sp, vehicle);
        var svc = sp.GetRequiredService<AracKrediService>();

        Assert.True(await svc.TaksitOdeAsync(krediId));           // aylık 1.200 (elle)

        var rs = sp.GetRequiredService<ReportService>();
        Assert.Equal(1200m, (await rs.GetGelirGiderAsync()).GiderToplam);

        // Karne: kategori "Finansman" 1200 + kredi bilgi olayı (DeftereYansir=false) + gider olayı (true).
        var karne = await rs.GetAracKarneAsync(vehicle);
        var kategori = Assert.Single(karne!.GiderKategori);
        Assert.Equal("Finansman", kategori.Kategori);
        Assert.Equal(1200m, kategori.Tutar);
        Assert.Contains(karne.Olaylar, o => o.Tur == "Kredi" && o.Tutar == 12_000m && !o.DeftereYansir);
        Assert.Contains(karne.Olaylar, o => o.Tur.StartsWith("Gider") && o.Tutar == 1200m && o.DeftereYansir);
        // Parite: karne == Karlilik satırı.
        var satir = Assert.Single((await rs.GetKarlilikAsync()).Satirlar);
        Assert.Equal(satir.Gider, karne.ToplamGider);

        // Sayaç ilerledi.
        Assert.Equal(1, (await svc.GetAsync(krediId))!.OdenenTaksit);
    }

    [Fact]
    public async Task Aracsiz_kredi_taksiti_atanmamista()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var krediId = await KrediAsync(sp, vehicleId: null);

        await sp.GetRequiredService<AracKrediService>().TaksitOdeAsync(krediId);

        var k = await sp.GetRequiredService<ReportService>().GetKarlilikAsync();
        var satir = Assert.Single(k.Satirlar);
        Assert.Null(satir.VehicleId);                             // Atanmamış — görünür
        Assert.Equal(1200m, satir.Gider);
    }

    [Fact]
    public async Task Cift_submit_anahtari_sayaci_da_geri_alir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KG 02" });
        var krediId = await KrediAsync(sp, vehicle);
        var svc = sp.GetRequiredService<AracKrediService>();

        var anahtar = Guid.NewGuid();
        Assert.True(await svc.TaksitOdeAsync(krediId, islemAnahtari: anahtar));
        // Aynı anahtar tekrar → Expense kısmi unique index çakışır → TÜM tx (sayaç dahil) geri alınır.
        await Assert.ThrowsAsync<ValidationException>(() => svc.TaksitOdeAsync(krediId, islemAnahtari: anahtar));

        Assert.Equal(1, (await svc.GetAsync(krediId))!.OdenenTaksit);   // 2 DEĞİL
        Assert.Equal(1200m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);
    }

    [Fact]
    public async Task Donem_kilidi_taksiti_engeller()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var krediId = await KrediAsync(sp, null);
        await sp.GetRequiredService<DonemKilidiService>().LockAsync(new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<AracKrediService>().TaksitOdeAsync(krediId));
        Assert.Equal(0, (await sp.GetRequiredService<AracKrediService>().GetAsync(krediId))!.OdenenTaksit);
    }

    [Fact]
    public async Task Eur_kredi_taksiti_kur_sozlesmesi()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<SabitKurService>().UpsertAsync(new SabitKurInput { Kod = "EUR", Kur = 40m, Aktif = true });
        var vehicle = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KG 03" });
        // 1.200 EUR, %0 faiz, 12 taksit → aylık 100 EUR × kur 40 = 4.000 base (elle).
        var krediId = await KrediAsync(sp, vehicle, tutar: 1200m, faiz: 0m, taksit: 12, doviz: "EUR");

        await sp.GetRequiredService<AracKrediService>().TaksitOdeAsync(krediId);

        Assert.Equal(4000m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);

        // Kur'suz döviz kredisi (DKK) → taksit reddi, sayaç ilerlemez.
        var dkk = await KrediAsync(sp, vehicle, tutar: 120m, faiz: 0m, taksit: 12, doviz: "DKK");
        await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<AracKrediService>().TaksitOdeAsync(dkk));
        Assert.Equal(0, (await sp.GetRequiredService<AracKrediService>().GetAsync(dkk))!.OdenenTaksit);
    }

    [Fact]
    public async Task Son_taksit_kapatir_ve_gider_yazar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // 2 taksitlik mini kredi: 2.000, %0 → aylık 1.000.
        var krediId = await KrediAsync(sp, null, tutar: 2000m, faiz: 0m, taksit: 2);
        var svc = sp.GetRequiredService<AracKrediService>();

        await svc.TaksitOdeAsync(krediId);
        await svc.TaksitOdeAsync(krediId);

        var k = (await svc.GetAsync(krediId))!;
        Assert.Equal(KrediDurum.Kapandi, k.Durum);                // mevcut kapanış davranışı korunur
        Assert.Equal(2000m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);
        // Kapanmış krediye üçüncü ödeme → false (mevcut davranış), gider yazmaz.
        Assert.False(await svc.TaksitOdeAsync(krediId));
        Assert.Equal(2000m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);
    }

    [Fact]
    public async Task Iptal_krediye_repo_citi_taksit_yazdirmaz() // adversarial 1.3 M1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var krediId = await KrediAsync(sp, null);
        var svc = sp.GetRequiredService<AracKrediService>();
        await svc.IptalAsync(krediId);

        // Servis ön-kontrolünü ATLAYIP doğrudan repo (yarış penceresinin simülasyonu):
        // Durum çiti artık KİLİDİN ARKASINDA → para yazılamaz, İptal ezilemez.
        var repo = sp.GetRequiredService<IAracKrediRepository>();
        await Assert.ThrowsAsync<ValidationException>(() => repo.TaksitOdeAsync(krediId, sira =>
            throw new InvalidOperationException("posting çağrılmamalı")));

        var k = (await svc.GetAsync(krediId))!;
        Assert.Equal(KrediDurum.Iptal, k.Durum);                 // İptal KORUNDU (Kapandi ezmesi yok)
        Assert.Equal(0, k.OdenenTaksit);
        Assert.Equal(0m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);
    }

    [Fact]
    public async Task Kalan_yontemi_son_taksit_kurus_birebir() // adversarial 1.3 L1
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        // 10.000 / 3 taksit %0: aylık 3.333,33 + son 3.333,34 → Σ TAM 10.000,00 (9.999,99 DEĞİL).
        var krediId = await KrediAsync(sp, null, tutar: 10_000m, faiz: 0m, taksit: 3);
        var svc = sp.GetRequiredService<AracKrediService>();

        await svc.TaksitOdeAsync(krediId);
        await svc.TaksitOdeAsync(krediId);
        await svc.TaksitOdeAsync(krediId);

        Assert.Equal(10_000.00m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);
        var ozet = AracKrediService.Hesapla((await svc.GetAsync(krediId))!);
        Assert.Equal(0.00m, ozet.KalanBakiye);                   // kapanmışta "0,01 kalan" hayaleti yok
        Assert.Equal(3333.34m, ozet.Taksitler[^1].Tutar);        // son taksit farkı emer (elle)

        // Ters yön: 100/7 → 6×14,29 + son 14,26 = TAM 100,00 (100,03 DEĞİL).
        var k2 = await KrediAsync(sp, null, tutar: 100m, faiz: 0m, taksit: 7);
        for (var i = 0; i < 7; i++) await svc.TaksitOdeAsync(k2);
        Assert.Equal(10_100.00m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam);
    }

    [Fact]
    public async Task Gelecek_tarihli_taksit_reddedilir() // adversarial 1.3 L2
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var krediId = await KrediAsync(sp, null);

        await Assert.ThrowsAsync<ValidationException>(() => sp.GetRequiredService<AracKrediService>()
            .TaksitOdeAsync(krediId, odemeTarih: DateTimeOffset.UtcNow.AddDays(2)));
        Assert.Equal(0, (await sp.GetRequiredService<AracKrediService>().GetAsync(krediId))!.OdenenTaksit);
    }

    [Fact]
    public async Task Muhasebe_taksit_odeyebilir() // adversarial 1.3 M2 (servis düzeyi)
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid krediId;
        using (var admin = host.ScopeFor(tenant))
            krediId = await KrediAsync(admin.ServiceProvider, null);

        using var muh = host.ScopeFor(tenant, Guid.NewGuid(), "muh", UserRole.Muhasebe);
        Assert.True(await muh.ServiceProvider.GetRequiredService<AracKrediService>().TaksitOdeAsync(krediId));
    }

    [Fact]
    public async Task Operator_taksit_odeyemez_artik_finance_write()
    {
        using var host = new TestHost(fx.AppConnectionString);
        var tenant = Guid.NewGuid();
        Guid krediId;
        using (var admin = host.ScopeFor(tenant))
            krediId = await KrediAsync(admin.ServiceProvider, null);

        using var op = host.ScopeFor(tenant, Guid.NewGuid(), "op", UserRole.Operator, assignedBranch: "Merkez");
        await Assert.ThrowsAsync<YetkiYokException>(
            () => op.ServiceProvider.GetRequiredService<AracKrediService>().TaksitOdeAsync(krediId));
    }
}
