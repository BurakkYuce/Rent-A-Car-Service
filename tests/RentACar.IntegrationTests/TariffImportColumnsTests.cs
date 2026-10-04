using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RentACar.Application.Customers;
using RentACar.Application.RateMatrices;
using RentACar.Application.Vehicles;
using RentACar.IntegrationTests.Infrastructure;
using RentACar.Web.Import;

namespace RentACar.IntegrationTests;

/// <summary>
/// Kabul bulgusu B (b-tarife-aktar-02, PARA): ekrandaki "Tanınan sütunlar" metninde yazan
/// "Haftalık (8-29 gün)" başlığı sessizce okunmuyordu → GunHaftalik boş kalıyor, 8-29 günlük kirada
/// Gün 1 fiyatı uygulanıyordu (müşteriye fazla fiyat). Kural: ekranın listelediği HER başlık birebir
/// okunur; tanınmayan başlık sessizce düşmez, aktarım sonucunda uyarı olur.
///
/// BAĞIMSIZ ORACLE: başlıklar ekran metninden (i18n fiyatTarife.aktar.sutunlar) elle kopyalandı —
/// "Kod (zorunlu), Ad, Kanal, Grup/Araç Grubu, Şube, Lokasyon, Başlangıç, Bitiş, Gün 1..Gün 7,
/// Haftalık (8-29 gün), Aylık (30+), Para Birimi, Açıklama". Değerler satırda elle yazıldı.
/// </summary>
[Collection("postgres")]
public sealed class TariffImportColumnsTests(PostgresFixture fx)
{
    private const string ScreenHeaders =
        "Kod;Ad;Kanal;Araç Grubu;Şube;Lokasyon;Başlangıç;Bitiş;Gün 1;Gün 2;Gün 3;Gün 4;Gün 5;Gün 6;Gün 7;"
        + "Haftalık (8-29 gün);Aylık (30+);Para Birimi;Açıklama";

    private const string ScreenRow =
        "IMP-1;İthal;WEB;EKO;Merkez;Havalimanı;2026-06-01;2026-08-31;1000;990;980;970;960;950;940;"
        + "900;700;TRY;Yaz tarifesi";

    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static ImportService Svc(IServiceProvider sp)
        => new(sp.GetRequiredService<VehicleService>(), sp.GetRequiredService<CustomerService>(),
               sp.GetRequiredService<RateMatrixService>());

    [Fact]
    public async Task Every_column_listed_on_screen_is_read()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var r = await Svc(sp).ImportTariffsAsync(ImportService.Parse(Csv(ScreenHeaders + "\n" + ScreenRow), "t.csv"));
        Assert.Equal(1, r.Eklenen);
        Assert.Equal(0, r.Hatali);

        var m = Assert.Single(await sp.GetRequiredService<RateMatrixService>().ListAsync());
        Assert.Equal("IMP-1", m.Kod);
        Assert.Equal("İthal", m.Ad);
        Assert.Equal("WEB", m.Kanal);
        Assert.Equal("EKO", m.AracGrupKod);
        Assert.Equal("Merkez", m.Sube);
        Assert.Equal("Havalimanı", m.Lokasyon);
        Assert.Equal(1000m, m.Gun1);
        Assert.Equal(990m, m.Gun2);
        Assert.Equal(980m, m.Gun3);
        Assert.Equal(970m, m.Gun4);
        Assert.Equal(960m, m.Gun5);
        Assert.Equal(950m, m.Gun6);
        Assert.Equal(940m, m.Gun7);
        Assert.Equal(900m, m.GunHaftalik); // bulgu: boş kalıyordu
        Assert.Equal(700m, m.GunAylik);
        Assert.Equal("TRY", m.ParaBirimi);
        Assert.Equal("Yaz tarifesi", m.Aciklama);
    }

    [Fact]
    public async Task Unknown_columns_are_reported_as_warnings_known_ones_are_not()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        // Ekrandaki tüm başlıklar + onay kolonu (ekran "yok sayılır" diyor → uyarı DEĞİL) + iki tanınmayan.
        var parsed = ImportService.ParseWithHeaders(
            Csv(ScreenHeaders + ";Onay Durumu;Hafta Sonu Fiyatı;Not\n" + ScreenRow + ";Onaylı;850;x"), "t.csv");
        var r = await Svc(sp).ImportTariffsAsync(parsed.Rows, parsed.Headers);

        Assert.Equal(1, r.Eklenen);
        Assert.Equal(2, r.Uyarilar.Count);
        Assert.Contains(r.Uyarilar, u => u.Contains("'Hafta Sonu Fiyatı'"));
        Assert.Contains(r.Uyarilar, u => u.Contains("'Not'"));
        Assert.Equal(900m, Assert.Single(await sp.GetRequiredService<RateMatrixService>().ListAsync()).GunHaftalik);
    }

    /// <summary>Adversarial #365 Medium: "1.250" eskiden 1,25 okunuyordu (fiyat 1000 kat düşük). Tek ayraç + ardından
    /// tam 3 hane belirsizdir → satır hatası. Belirsiz olmayan biçimler aynen okunur (elle: 1.250,00 → 1250;
    /// 1250,5 → 1250,5; 1250.5 → 1250,5; 1.250.000,75 → 1250000,75).</summary>
    [Fact]
    public async Task Ambiguous_thousands_or_decimal_value_is_a_row_error()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var csv = "Kod;Gün 1;Gün 2\n" +
                  "AMB-1;1.250;100\n" +          // belirsiz → hata
                  "AMB-2;1,250;100\n" +          // belirsiz → hata
                  "OK-1;1.250,00;1250,5\n" +
                  "OK-2;1250.5;1.250.000,75";
        var r = await Svc(sp).ImportTariffsAsync(ImportService.Parse(Csv(csv), "t.csv"));

        Assert.Equal(2, r.Eklenen);
        Assert.Equal(2, r.Hatali);
        Assert.Contains(r.Hatalar, h => h.StartsWith("AMB-1:") && h.Contains("belirsiz"));
        Assert.Contains(r.Hatalar, h => h.StartsWith("AMB-2:") && h.Contains("belirsiz"));
        var list = await sp.GetRequiredService<RateMatrixService>().ListAsync();
        var ok1 = Assert.Single(list, m => m.Kod == "OK-1");
        Assert.Equal(1250m, ok1.Gun1);
        Assert.Equal(1250.5m, ok1.Gun2);
        var ok2 = Assert.Single(list, m => m.Kod == "OK-2");
        Assert.Equal(1250.5m, ok2.Gun1);
        Assert.Equal(1250000.75m, ok2.Gun2);
    }

    /// <summary>Adversarial L1: iki sütun aynı alana eşleşiyor ("Haftalık"=500 + "Haftalık (8-29 gün)"=450) → başlık
    /// uyarısı + değerler farklı olan satır hatası (hangisinin geçerli olduğu tahmin edilmez); değerler aynıysa satır
    /// girer.</summary>
    [Fact]
    public async Task Two_columns_for_the_same_field_warn_and_conflicting_rows_fail()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var parsed = ImportService.ParseWithHeaders(
            Csv("Kod;Gün 1;Haftalık;Haftalık (8-29 gün)\nDUP-1;1000;500;450\nDUP-2;1000;450;450"), "t.csv");
        var r = await Svc(sp).ImportTariffsAsync(parsed.Rows, parsed.Headers);

        var warning = Assert.Single(r.Uyarilar);
        Assert.Contains("'Haftalık'", warning);
        Assert.Contains("'Haftalık (8-29 gün)'", warning);
        Assert.Equal(1, r.Eklenen);
        Assert.Contains(r.Hatalar, h => h.StartsWith("DUP-1:"));
        Assert.Equal(450m, Assert.Single(await sp.GetRequiredService<RateMatrixService>().ListAsync()).GunHaftalik);
    }

    /// <summary>Adversarial L2: uyarı listesi en çok 20 satır + özet satırı (30 tanınmayan sütun → 21 satır).</summary>
    [Fact]
    public async Task Warnings_are_capped()
    {
        using var host = new TestHost(fx.AppConnectionString);
        using var scope = host.ScopeFor(Guid.NewGuid());
        var sp = scope.ServiceProvider;

        var extra = string.Join(";", Enumerable.Range(1, 30).Select(i => $"Fazla{i}"));
        var parsed = ImportService.ParseWithHeaders(Csv($"Kod;Gün 1;{extra}\nCAP-1;100"), "t.csv");
        var r = await Svc(sp).ImportTariffsAsync(parsed.Rows, parsed.Headers);

        Assert.Equal(21, r.Uyarilar.Count);
        Assert.Contains("+10 uyarı daha", r.Uyarilar[^1]);
    }
}
