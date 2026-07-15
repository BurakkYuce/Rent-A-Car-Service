using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.Vehicles;
using RentACar.Infrastructure.Persistence;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Import;

namespace RentACar.IntegrationTests;

/// <summary>
/// Veri göçü içe-aktarımı (Excel/CSV → araç/cari) — BAĞIMSIZ ORACLE. Beklenen sayılar elle senaryodan.
/// KRİTİK: müşteri PII (TC) DÜZ metin DB'ye GİRMEZ (şifreli + hash); plaka/TC tekrarı atlanır.
/// </summary>
[Collection("postgres")]
public sealed class ImportTests(PostgresFixture fx)
{
    private static IReadOnlyList<Dictionary<string, string>> Rows(string csv)
        => ImportService.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "x.csv");

    private static ImportService Svc(IServiceProvider sp)
        => new(sp.GetRequiredService<VehicleService>(), sp.GetRequiredService<CustomerService>(),
               sp.GetRequiredService<RentACar.Application.RateMatrices.RateMatrixService>());

    [Fact]
    public async Task Arac_import_ekler_ve_ayni_plakayi_atlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // 3 satır: 2 farklı plaka + 1 tekrar (normalize edilince aynı) → 2 eklenir, 1 atlanır.
        var csv = "Plaka;Marka;Tipi;Grup;Yakıt Türü;Son Km\n" +
                  "34 ABC 01;Fiat;Egea;EKO;Dizel;45000\n" +
                  "34ABC02;Renault;Clio;EKO;Benzin;12000\n" +
                  "34abc01;Kopya;Kopya;EKO;Benzin;0";
        var r = await Svc(sp).ImportAraclarAsync(Rows(csv));

        Assert.Equal(2, r.Eklenen);
        Assert.Equal(1, r.Atlanan);
        Assert.Equal(0, r.Hatali);

        var vehicles = await sp.GetRequiredService<VehicleService>().ListAsync();
        Assert.Equal(2, vehicles.Count);
        var fiat = Assert.Single(vehicles, v => v.Plaka == "34ABC01");
        Assert.Equal("Fiat", fiat.Marka);     // ilk satır kazanır (tekrar atlandı)
        Assert.Equal("Egea", fiat.Tip);
        Assert.Equal(45000, fiat.Km);
        Assert.Equal(RentACar.Domain.Enums.FuelType.Dizel, fiat.Yakit);
    }

    [Fact]
    public async Task Cari_import_PII_sifreli_saklar_ve_tc_tekrarini_atlar()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // 3 satır: 2 farklı TC + 1 aynı TC → 2 eklenir, 1 atlanır. "Ad Soyad" birleşik → Ad/Soyad'a ayrışır.
        var csv = "Ad Soyad;TC Kimlik;Telefon;İl\n" +
                  "Ali Veli;11111111110;5551112233;İstanbul\n" +
                  "Ayşe Yılmaz;22222222220;5559998877;Ankara\n" +
                  "Ali Kopya;11111111110;5550001122;İzmir";
        var r = await Svc(sp).ImportCarilerAsync(Rows(csv));

        Assert.Equal(2, r.Eklenen);
        Assert.Equal(1, r.Atlanan);

        // KVKK: düz TcKimlik NULL; TcKimlikEnc + TcKimlikHash DOLU (şifreli + blind-index).
        var factory = sp.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var cariler = await db.Customers.AsNoTracking().ToListAsync();
        Assert.Equal(2, cariler.Count);
        Assert.All(cariler, c => Assert.Null(c.TcKimlik));                              // düz metin yazılmadı
        Assert.All(cariler, c => Assert.False(string.IsNullOrEmpty(c.TcKimlikEnc)));    // cipher var
        Assert.All(cariler, c => Assert.False(string.IsNullOrEmpty(c.TcKimlikHash)));   // blind-index var
        Assert.Contains(cariler, c => c.Ad == "Ali" && c.Soyad == "Veli");             // isim ayrıştı
    }
}
