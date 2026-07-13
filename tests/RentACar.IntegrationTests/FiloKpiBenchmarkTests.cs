using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Customers;
using RentACar.Application.Expenses;
using RentACar.Application.Finance;
using RentACar.Application.Reporting;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ2-2.3 — Sınıf kıyas endeksi + filo HAVUZ KPI'ları + başabaş-günlük-vs-ADR.
/// BAĞIMSIZ ORACLE (elle):
/// (1) EKO grubu km-maliyetleri 40/100=0.40 ve 80/100=0.80 → ort 0.60 → endeksler 0.67 / 1.33.
/// (2) Havuz (ömür): sahiplik 10+10=20 gün, kiralanan 3+3=6 gün, gelir 200+200=400 →
///     Doluluk 6/20=30.00, RevPACD 400/20=20.00, ADR 400/6=66.67 — satır ORTALAMASI DEĞİL havuz oranı.
/// (3) Başabaş günlük: Alım 3044, İkinciEl yok → residual %30 → amortisman 2130.80, süre 1 ay →
///     aylık 2130.80 → günlük 2130.80/30.44 = 70.00; model yoksa (alım bedelsiz) null → UI "—".
/// Kenarlar: tek-araç grup endeks 1.00 (kendisi=ortalama; bilgi endeksi, sinyal değil); km'siz araç
/// endeks null ve ortalamayı bozmaz; sahiplik-günsüz filoda havuz oranları null (pozitif-payda).
/// </summary>
[Collection("postgres")]
public sealed class FiloKpiBenchmarkTests(PostgresFixture fx)
{
    private static Task GiderAsync(IServiceProvider sp, Guid veh, decimal net)
        => sp.GetRequiredService<ExpenseService>().CreateAsync(new ExpenseInput
        {
            Tip = ExpenseType.Arac, VehicleId = veh, NetTutar = net, KdvOrani = 0m,
            Tarih = DateTimeOffset.UtcNow.AddDays(-1), OdemeYontemi = OdemeYontemi.Nakit
        });

    /// <summary>Kira + teslim + dönüş (km yazımı); faturala=true ise %0 KDV ile deftere gelir düşer.</summary>
    private static async Task KiraAsync(IServiceProvider sp, Guid cari, Guid veh,
        DateTimeOffset bas, DateTimeOffset bit, int donusKm, bool faturala)
    {
        var rentals = sp.GetRequiredService<RentalService>();
        var r = await rentals.CreateDirectAsync(new BookingInput
        { MusteriId = cari, VehicleId = veh, BasTar = bas, BitTar = bit, GunlukUcret = 100m });
        if (faturala) await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(r, kdvRate: 0m);
        await rentals.DeliverAsync(r, cikisKm: 0, cikisYakit: 8);
        await rentals.ReturnAsync(r, donusKm: donusKm, donusYakit: 8, bit);
    }

    [Fact]
    public async Task Sinif_endeksi_ve_havuz_kpi_elle_oracle()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "KPI", Soyad = "C" });
        var simdi = DateTimeOffset.UtcNow;

        // İki EKO aracı, sahiplik penceresi 10'ar gün (bugün-9 → bugün, kapsayıcı takvim günü).
        var v1 = await veh.CreateAsync(new VehicleInput
        { Plaka = "34 KP 01", Grup = "EKO", FiloGirisTarih = simdi.AddDays(-9) });
        var v2 = await veh.CreateAsync(new VehicleInput
        { Plaka = "34 KP 02", Grup = "EKO", FiloGirisTarih = simdi.AddDays(-9) });

        // V1: 2 ücret-günü × 100 = 200 gelir (fatura %0 KDV), takvim-kiralanan 3 gün, km 100, gider 40.
        await KiraAsync(sp, cari, v1, simdi.AddDays(-4), simdi.AddDays(-2), donusKm: 100, faturala: true);
        await GiderAsync(sp, v1, 40m);
        // V2: aynı yapı — gelir 200, kiralanan 3 gün, km 100, gider 80.
        await KiraAsync(sp, cari, v2, simdi.AddDays(-5), simdi.AddDays(-3), donusKm: 100, faturala: true);
        await GiderAsync(sp, v2, 80m);

        var filo = await sp.GetRequiredService<ReportService>().GetFiloAnalizAsync();
        var r1 = filo.Satirlar.Single(x => x.VehicleId == v1);
        var r2 = filo.Satirlar.Single(x => x.VehicleId == v2);

        // Sınıf endeksi (elle): km-maliyetler 0.40/0.80, ort 0.60 → 0.67 ve 1.33.
        Assert.Equal(0.40m, r1.KmBasinaMaliyet);
        Assert.Equal(0.80m, r2.KmBasinaMaliyet);
        Assert.Equal(0.67m, r1.SinifEndeks);
        Assert.Equal(1.33m, r2.SinifEndeks);

        // Havuz KPI (elle): Σ havuzlardan — satır KPI'larının ortalaması değil.
        var h = filo.HavuzKpi!;
        Assert.Equal(20, h.SahiplikGun);
        Assert.Equal(6, h.KiralananGun);
        Assert.Equal(400m, h.OmurGelir);
        Assert.Equal(30.00m, h.DolulukYuzde);  // 6×100/20
        Assert.Equal(20.00m, h.RevPacd);       // 400/20
        Assert.Equal(66.67m, h.Adr);           // 400/6
    }

    [Fact]
    public async Task Basabas_gunluk_model_vs_adr()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var rs = sp.GetRequiredService<ReportService>();

        // Alım 3044, İkinciEl yok → residual %30: amortisman 3044×0.7=2130.80; sahiplik 0 → süre 1 ay,
        // gider 0 → aylık başabaş 2130.80 → GÜNLÜK 2130.80/30.44 = 70.00 (elle, tam bölünme).
        var vb = await veh.CreateAsync(new VehicleInput { Plaka = "34 KP 03", AlimBedeli = 3044m });
        var karne = (await rs.GetAracKarneAsync(vb))!;
        Assert.Equal(70.00m, karne.BasaBasGunluk);
        Assert.Null(karne.Kpi.Adr);            // hiç kiralanmamış → ADR yok (UI "—")

        // Alım bedelsiz araçta model yok → başabaş günlük null (uydurma değer basılmaz).
        var vc = await veh.CreateAsync(new VehicleInput { Plaka = "34 KP 04" });
        Assert.Null((await rs.GetAracKarneAsync(vc))!.BasaBasGunluk);
        Assert.Null((await rs.GetAracKarneAsync(vc))!.MaliyetModel);
    }

    [Fact]
    public async Task Kenarlar_tek_arac_grup_ve_bos_havuz()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var veh = sp.GetRequiredService<VehicleService>();
        var cari = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "KPI", Soyad = "K" });
        var simdi = DateTimeOffset.UtcNow;

        // SOLO grubunda km-maliyetli TEK araç: 50/100=0.50, ort=kendisi → endeks 1.00.
        // Aynı grupta km'siz ikinci araç: endeksi null VE ortalamayı bozmaz (değersiz üye sayılmaz).
        var v1 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KP 05", Grup = "SOLO" });
        await KiraAsync(sp, cari, v1, simdi.AddDays(-4), simdi.AddDays(-2), donusKm: 100, faturala: false);
        await GiderAsync(sp, v1, 50m);
        var v2 = await veh.CreateAsync(new VehicleInput { Plaka = "34 KP 06", Grup = "SOLO" });

        var filo = await sp.GetRequiredService<ReportService>().GetFiloAnalizAsync();
        Assert.Equal(1.00m, filo.Satirlar.Single(x => x.VehicleId == v1).SinifEndeks);
        Assert.Null(filo.Satirlar.Single(x => x.VehicleId == v2).SinifEndeks);

        // Havuz: hiçbir araçta filo-giriş/alım tarihi yok → Σ sahiplik 0 → oranlar null (pozitif payda).
        var h = filo.HavuzKpi!;
        Assert.Equal(0, h.SahiplikGun);
        Assert.Null(h.DolulukYuzde);
        Assert.Null(h.RevPacd);
        Assert.Null(h.Adr);
    }
}
