using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Bookings;
using RentACar.Application.Common;
using RentACar.Application.Customers;
using RentACar.Application.Finance;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ1-1.4 — Kira-seviyesi ÖZEL KDV oranı + DAMGA vergisi (S2→S1). Oran zinciri:
/// kdvRate ?? RentalContract.OzelKdvOran ?? 0.20; NET-mod guard'ı ZİNCİR SONUCUNA bakar.
/// Damga: parametrede yoksa kiradaki fatura InvoiceTaxInfo'suna kopyalanır (bilgi — defter değişmez).
/// BAĞIMSIZ ORACLE (elle): 3g×100=300 brüt, oran 0.10 → net 272,73 / KDV 27,27; fark 600 → 545,45/54,55.
/// Sözleşme Tutar'ı DEĞİŞMEZ (bilgi alanı); whitelist tip-düzeyi korunur (para alanları tipte yok).
/// </summary>
[Collection("postgres")]
public sealed class KiraDamgaOzelKdvTests(PostgresFixture fx)
{
    private static readonly DateTimeOffset Bas =
        new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).AddDays(-3).AddHours(9);

    private static async Task<(Guid rental, Guid cari)> KiraAsync(IServiceProvider sp, string plaka,
        decimal? ozelKdv = null, decimal? damga = null, string? fiyatTuru = null)
    {
        var v = await sp.GetRequiredService<VehicleService>().CreateAsync(new VehicleInput { Plaka = plaka });
        var m = await sp.GetRequiredService<CustomerService>()
            .CreateAsync(new CustomerInput { Tip = CariType.Bireysel, Ad = "Vergi", Soyad = "Cari" });
        var r = await sp.GetRequiredService<RentalService>().CreateDirectAsync(new BookingInput
        {
            MusteriId = m, VehicleId = v, BasTar = Bas, BitTar = Bas.AddDays(3), GunlukUcret = 100m,
            KmLimit = 300, FazlaKmUcret = 2m, OzelKdvOran = ozelKdv, DamgaVergisi = damga, FiyatTuru = fiyatTuru
        });
        return (r, m);
    }

    [Fact]
    public async Task Ozel_kdv_ve_damga_faturaya_varsayilan_olur()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, _) = await KiraAsync(sp, "34 VG 01", ozelKdv: 0.10m, damga: 50m);

        // Sözleşme tutarı bilgi alanlarından ETKİLENMEZ: 3×100 = 300 (elle).
        Assert.Equal(300m, (await sp.GetRequiredService<RentalService>().GetAsync(rental))!.GenelToplam);

        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental); // parametresiz
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!;
        Assert.Equal(272.73m, inv.NetTutar);       // 300/1.10 (elle)
        Assert.Equal(27.27m, inv.KdvTutar);
        Assert.Equal(300m, inv.GenelToplam);
        Assert.Equal(50m, inv.DamgaVergisi);       // kiradan kopyalandı (bilgi)
    }

    [Fact]
    public async Task Parametre_kira_varsayilanini_ezer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, _) = await KiraAsync(sp, "34 VG 02", ozelKdv: 0.10m, damga: 50m);

        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental,
            kdvRate: 0m, vergi: new InvoiceTaxInfo(null, null, null, 75m, false, false));
        var inv = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!;
        Assert.Equal(300m, inv.NetTutar);          // %0: net = brüt (elle)
        Assert.Equal(0m, inv.KdvTutar);
        Assert.Equal(75m, inv.DamgaVergisi);       // parametre kazandı
    }

    [Fact]
    public async Task Net_modda_ozel_kdv_giriste_reddedilir() // adversarial D: guard GİRİŞ noktasında
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // CREATE: "Günlük" NET mod + özel oran → çelişki hiç oluşmaz (fark-kilidi senaryosu imkânsız).
        await Assert.ThrowsAsync<ValidationException>(
            () => KiraAsync(sp, "34 VG 03", ozelKdv: 0.10m, fiyatTuru: "Günlük"));

        // UPDATE: oransız net-mod kira sonradan oran alamaz (aynı çit). (Farklı plaka: reddedilen
        // create yine de aracı oluşturmuştu — duplicate-plaka çakışması.)
        var (rental, _) = await KiraAsync(sp, "34 VG 03B", fiyatTuru: "Günlük");
        await Assert.ThrowsAsync<ValidationException>(
            () => sp.GetRequiredService<RentalService>()
                .UpdateOpenAsync(rental, new RentalUpdateInput { OzelKdvOran = 0.10m }));
        // Fatura yine kesilebilir (kilitlenme yok) — NET mod: 100 net/gün → brüt 120 × 3g = 360 (elle, KURAL B).
        var invId = await sp.GetRequiredService<InvoiceService>().CreateFromRentalAsync(rental);
        Assert.Equal(360m, (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(invId))!.GenelToplam);
    }

    [Fact]
    public async Task Fark_faturasi_damgayi_tekrarlamaz() // adversarial Low: sözleşme-başı tek pul
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, _) = await KiraAsync(sp, "34 VG 06", damga: 50m);
        var invoices = sp.GetRequiredService<InvoiceService>();
        var rentals = sp.GetRequiredService<RentalService>();
        var repo = sp.GetRequiredService<IInvoiceRepository>();

        var baseId = await invoices.CreateFromRentalAsync(rental);
        await rentals.DeliverAsync(rental, cikisKm: 1000, cikisYakit: 8);
        await rentals.ReturnAsync(rental, donusKm: 1600, donusYakit: 8, Bas.AddDays(3));
        var farkId = await invoices.CreateFromRentalAsync(rental);

        Assert.Equal(50m, (await repo.FindAsync(baseId))!.DamgaVergisi);   // base'de pul VAR
        Assert.Null((await repo.FindAsync(farkId))!.DamgaVergisi);         // fark'ta TEKRARLANMAZ
    }

    [Fact]
    public async Task Fark_faturasi_ayni_ozel_orani_kullanir()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, _) = await KiraAsync(sp, "34 VG 04", ozelKdv: 0.10m);
        var invoices = sp.GetRequiredService<InvoiceService>();
        var rentals = sp.GetRequiredService<RentalService>();

        await invoices.CreateFromRentalAsync(rental);                                   // base 300 → 272,73/27,27
        await rentals.DeliverAsync(rental, cikisKm: 1000, cikisYakit: 8);
        await rentals.ReturnAsync(rental, donusKm: 1600, donusYakit: 8, Bas.AddDays(3)); // 300 aşım×2=600 fark
        var farkId = await invoices.CreateFromRentalAsync(rental);                       // fark da 0.10 (zincir)

        var fark = (await sp.GetRequiredService<IInvoiceRepository>().FindAsync(farkId))!;
        Assert.Equal(545.45m, fark.NetTutar);      // 600/1.10 (elle)
        Assert.Equal(54.55m, fark.KdvTutar);
        Assert.Equal(600m, fark.GenelToplam);
    }

    [Fact]
    public async Task Update_ile_degistirilebilir_ve_dogrulama()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var (rental, _) = await KiraAsync(sp, "34 VG 05");
        var rentals = sp.GetRequiredService<RentalService>();

        // Açık kirada bilgi alanı güncellenebilir; para/tarih alanları RentalUpdateInput TİPİNDE YOK.
        await rentals.UpdateOpenAsync(rental, new RentalUpdateInput { OzelKdvOran = 0.18m, DamgaVergisi = 30m });
        var c = (await rentals.GetAsync(rental))!;
        Assert.Equal(0.18m, c.OzelKdvOran);
        Assert.Equal(30m, c.DamgaVergisi);
        Assert.Equal(300m, c.GenelToplam);         // tutar değişmedi

        // Doğrulama: oran 0..1 dışı ve negatif damga reddedilir (create + update aynı helper).
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.UpdateOpenAsync(rental, new RentalUpdateInput { OzelKdvOran = 1.5m }));
        await Assert.ThrowsAsync<ValidationException>(
            () => rentals.UpdateOpenAsync(rental, new RentalUpdateInput { DamgaVergisi = -5m }));
    }
}
