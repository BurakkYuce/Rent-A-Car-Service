using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.Vehicles;
using RentACar.Domain.Enums;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Import;

namespace RentACar.IntegrationTests;

/// <summary>
/// FAZ 6.1 — toplu tarife içe-aktarımı (xml_fiyat_aktar karşılığı) — BAĞIMSIZ ORACLE.
/// GÜVENLİK ÇİTİ: satırlar DAİMA Beklemede girer (dosyadaki onay kolonları yok sayılır) ve fiyat
/// motoru/çözümleyici onaysız tarifeyi SEÇMEZ — toplu import onaysız fiyatı canlıya çıkaramaz.
/// </summary>
[Collection("postgres")]
public sealed class TarifeImportTests(PostgresFixture fx)
{
    private static IReadOnlyList<Dictionary<string, string>> Rows(string csv)
        => ImportService.Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)), "t.csv");

    private static ImportService Svc(IServiceProvider sp)
        => new(sp.GetRequiredService<VehicleService>(), sp.GetRequiredService<CustomerService>(),
               sp.GetRequiredService<RateMatrixService>());

    [Fact]
    public async Task Tarife_import_bekliyor_girer_onay_kolonu_yok_sayilir_motor_secmez()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // 3 satır; TR ve EN sayı biçimi karışık; dosya "Onay Durumu=Onaylı" enjekte etmeye çalışıyor.
        var csv = "Kod;Ad;Kanal;Grup;Başlangıç;Bitiş;Gün 1;Gün 7;Haftalık;Aylık;Para Birimi;Onay Durumu;Onaylayan\n" +
                  "dlx;Deluxe Yaz;WEB;SUV;2026-06-01;31.08.2026;1.250,50;1100;950.25;800;try;Onaylı;hacker\n" +
                  "EKO-Y;Ekonomi Yaz;;EKO;;;700;600;;;;Onaylı;x\n" +
                  "STD-K;Standart Kış;ACENTA;;01.11.2026;28.02.2027;500;450;400;350;EUR;;";
        var r = await Svc(sp).ImportTarifelerAsync(Rows(csv));

        Assert.Equal(3, r.Eklenen);
        Assert.Equal(0, r.Atlanan);
        Assert.Equal(0, r.Hatali);

        var matrisler = sp.GetRequiredService<RateMatrixService>();
        var liste = await matrisler.ListAsync();
        Assert.Equal(3, liste.Count);
        Assert.All(liste, m => Assert.Equal(TarifeOnayDurumu.Bekliyor, m.OnayDurumu)); // enjeksiyon yok sayıldı
        Assert.All(liste, m => Assert.Null(m.Onaylayan));

        var dlx = Assert.Single(liste, m => m.Kod == "DLX");                 // kod büyük harfe normalize
        Assert.Equal(1250.50m, dlx.Gun1);                                    // "1.250,50" (TR biçimi)
        Assert.Equal(1100m, dlx.Gun7);
        Assert.Equal(950.25m, dlx.GunHaftalik);                              // "950.25" (EN biçimi)
        Assert.Equal(800m, dlx.GunAylik);
        Assert.Equal("TRY", dlx.ParaBirimi);
        Assert.Equal("WEB", dlx.Kanal);
        Assert.Equal("SUV", dlx.AracGrupKod);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), dlx.BasTar);   // ISO
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero), dlx.BitTar);  // TR "gg.aa.yyyy"

        // ÇİT: çözümleyici (Onaylı+Aktif filtresi — motorla aynı kural) Beklemede'yi SEÇMEZ.
        var sorgu = new RateMatrisSorgu(Kanal: "WEB", Sube: null, AracGrupKod: "SUV",
            Tarih: new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), GunSayisi: 1);
        Assert.Null(await matrisler.CozumleAsync(sorgu));
    }

    [Fact]
    public async Task Tarife_import_tekrari_atlar_bozuk_satiri_raporlar_digerleri_girer()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;
        var matrisler = sp.GetRequiredService<RateMatrixService>();

        // Mevcut kayıt: STD (import öncesi elle açılmış).
        await matrisler.CreateAsync(new RateMatrixInput { Kod = "STD", Ad = "Standart", Gun1 = 100m });

        // 5 satır: mevcut-tekrar + yeni + dosya-içi-tekrar + negatif fiyat + kodsuz.
        var csv = "Kod;Ad;Gün 1\n" +
                  "STD;Standart Kopya;999\n" +      // mevcut → atlanır (fiyatı EZMEZ)
                  "DLX;Deluxe;200\n" +              // eklenir
                  "dlx;Deluxe Kopya;300\n" +        // dosya-içi tekrar (normalize DLX) → atlanır
                  "NEG;Negatif;-50\n" +             // hata: negatif fiyat
                  ";Adsız;100";                     // hata: kod zorunlu
        var r = await Svc(sp).ImportTarifelerAsync(Rows(csv));

        Assert.Equal(1, r.Eklenen);
        Assert.Equal(2, r.Atlanan);
        Assert.Equal(2, r.Hatali);
        Assert.Contains(r.Hatalar, h => h.StartsWith("NEG:"));
        Assert.Contains(r.Hatalar, h => h.Contains("kodu zorunlu"));

        var liste = await matrisler.ListAsync();
        Assert.Equal(2, liste.Count);                                        // STD (eski) + DLX
        Assert.Equal(100m, Assert.Single(liste, m => m.Kod == "STD").Gun1);  // mevcut kayıt ezilmedi
        Assert.Equal(200m, Assert.Single(liste, m => m.Kod == "DLX").Gun1);  // ilk satır kazandı
    }
}
