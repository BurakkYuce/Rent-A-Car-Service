using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Kur;
using RentACar.Application.Regulation;
using RentACar.Application.Reporting;
using RentACar.Application.ServiceRecords;
using RentACar.Application.VehicleSales;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ1-1.1 — Dövizli ödeme uçlarında kur otomatik çözümü (KurCozucu). Önceden default kur=1m ile
/// EUR poliçe TL'ye eksik yazılabiliyordu (yalnız InvoiceService otomatik çözüyordu).
/// SÖZLEŞME: açık kur (>0) aynen; boş → TRY=1, döviz → sabit-kur/TCMB; bulunamazsa NET RED (sessiz 1 YOK).
/// BAĞIMSIZ ORACLE (elle): prim 100 EUR × sabit kur 40 = 4000 base; açık kur 35 → 3500; TRY 100 → 100.
/// "Kur yok" senaryoları izole kodla (SEK — hiçbir testte seed'lenmez; KurKayitlari platform-tablo sızıntı dersi).
/// </summary>
[Collection("postgres")]
public sealed class OdemeKurOtomatikTests(PostgresFixture fx)
{
    private static Task SabitKurAsync(IServiceProvider sp, string kod, decimal kur)
        => sp.GetRequiredService<SabitKurService>().UpsertAsync(new SabitKurInput { Kod = kod, Kur = kur, Aktif = true });

    private static async Task<decimal> GiderToplamAsync(IServiceProvider sp)
        => (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GiderToplam;

    [Fact]
    public async Task Sigorta_eur_odeme_kuru_otomatik_cozulur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KO 01" });
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await reg.AddInsuranceAsync(veh, InsuranceType.Kasko,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddYears(1), prim: 100m,
            policeNo: "P1", firma: "X", acenta: null, doviz: "EUR");

        await reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa); // kur YOK → otomatik 40

        Assert.Equal(4000m, await GiderToplamAsync(sp)); // 100 EUR × 40 (elle)
    }

    [Fact]
    public async Task Sigorta_acik_kur_otomatigi_ezer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m); // otomatik 40 olurdu
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KO 02" });
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await reg.AddInsuranceAsync(veh, InsuranceType.Trafik,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddYears(1), 100m, "P2", "X", null, "EUR");

        await reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa, kur: 35m); // tarihsel düzeltme senaryosu

        Assert.Equal(3500m, await GiderToplamAsync(sp)); // açık kur kazanır (elle)
    }

    [Fact]
    public async Task Doviz_kuru_bulunamazsa_odeme_reddedilir_sessiz_1_yok()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KO 03" });
        var reg = sp.GetRequiredService<RegulationService>();
        // AllowedCurrencies beyaz-listesi gereği GBP kullan (hiçbir testte kur seed'i yok → bulunamaz).
        var pol = await reg.AddInsuranceAsync(veh, InsuranceType.Kasko,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddYears(1), 100m, "P3", "X", null, "GBP");

        await Assert.ThrowsAsync<ValidationException>(() => reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa));
        Assert.Equal(0m, await GiderToplamAsync(sp)); // hiçbir şey postlanmadı
        Assert.False((await reg.ListInsuranceAsync()).Single(p => p.Id == pol).Odendi);
    }

    [Fact]
    public async Task Try_odemeler_etkilenmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KO 04" });
        var reg = sp.GetRequiredService<RegulationService>();
        var pol = await reg.AddInsuranceAsync(veh, InsuranceType.Trafik,
            DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddYears(1), 100m, "P4", "X", null, "TRY");

        await reg.SigortaOdeAsync(pol, LedgerAccountType.Kasa); // TRY → 1 (kur tablosu gerekmez)

        Assert.Equal(100m, await GiderToplamAsync(sp));
    }

    [Fact]
    public async Task Depozito_eur_al_iade_otomatik_kur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Depo", Soyad = "Cari" });
        var depo = sp.GetRequiredService<DepozitoService>();

        await depo.AlAsync(cari, 100m, LedgerAccountType.Kasa, doviz: "EUR");   // kur otomatik 40
        Assert.Equal(4000m, await depo.GetBakiyeAsync(cari));                    // base 4000 (elle)

        await depo.IadeAsync(cari, 50m, LedgerAccountType.Kasa, doviz: "EUR");  // 2000 base iade
        Assert.Equal(2000m, await depo.GetBakiyeAsync(cari));

        // Kur'suz döviz (SEK): net red + bakiye değişmez.
        await Assert.ThrowsAsync<ValidationException>(
            () => depo.AlAsync(cari, 10m, LedgerAccountType.Kasa, doviz: "SEK"));
        Assert.Equal(2000m, await depo.GetBakiyeAsync(cari));
    }

    [Fact]
    public async Task Satis_eur_otomatik_kur_ve_acik_kur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        await SabitKurAsync(sp, "EUR", 40m);
        var veh = sp.GetRequiredService<VehicleService>();
        var sale = sp.GetRequiredService<VehicleSaleService>();

        // Otomatik: 1000 EUR net × 40 = 40.000 gelir (KDV 0, elle).
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KO 05" });
        await sale.CreateAsync(new VehicleSaleInput
        { VehicleId = v1, AliciCariId = Guid.NewGuid(), SatisNet = 1000m, KdvOrani = 0m, Doviz = "EUR" }); // Kur YOK
        var rs = sp.GetRequiredService<ReportService>();
        Assert.Equal(40_000m, (await rs.GetGelirGiderAsync()).GelirToplam);

        // Açık kur ezer: 100 EUR × 35 = 3500 ek gelir → toplam 43.500.
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KO 06" });
        await sale.CreateAsync(new VehicleSaleInput
        { VehicleId = v2, AliciCariId = Guid.NewGuid(), SatisNet = 100m, KdvOrani = 0m, Doviz = "EUR", Kur = 35m });
        Assert.Equal(43_500m, (await rs.GetGelirGiderAsync()).GelirToplam);
    }

    [Fact]
    public async Task Servis_yansit_default_try_bir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = "34 KO 07" });
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Rucu", Soyad = "Cari" });
        var svc = sp.GetRequiredService<ServiceRecordService>();
        var id = await svc.CreateAsync(new ServiceRecordInput
        {
            VehicleId = veh, Tip = ServisTipi.Ariza, GirisKm = 0,
            HasarSorumlu = HasarSorumlu.Musteri, KusurOrani = 0.5m,
            Lines = [new ServiceLineInput { Aciklama = "X", Tutar = 1000m }]
        });
        await svc.BaslatAsync(id);
        await svc.TamamlaAsync(id, cikisKm: 10);

        await svc.YansitAsync(id, cari); // doviz TRY default, kur boş → 1

        Assert.Equal(500m, (await sp.GetRequiredService<ReportService>().GetGelirGiderAsync()).GelirToplam);
    }
}
