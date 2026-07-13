using RentACar.Application.Pricing;
using RentACar.Application.Reporting;
using RentACar.Domain.Enums;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// Araç-karne PR6 — export projeksiyonları (KarneExportKatalog; ListExportCatalog test deseni).
/// BAĞIMSIZ ORACLE: elle kurulmuş DTO'lar → beklenen hücre/satır değerleri elle. Para mantığı yok —
/// yalnız eşleme doğruluğu (null→404 yolu, TOPLAM/Atanmamış satırları, bölüm başlıkları).
/// </summary>
public sealed class KarneExportTests
{
    private static AracKarneDto OrnekKarne() => new(
        new AracKarneHeaderDto(Guid.NewGuid(), "34KR99", "Fiat", "Egea", "EKO", "Ekonomik", "Merkez", "Bizim",
            VehicleStatus.Musait, 12000, 1000m, new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero), 800m,
            null, null, null, null),
        ToplamGelir: 500m, ToplamGider: 150m, ToplamNetKar: 350m,
        YillikPnl: [new AracYilPnlRow(2024, 300m, 100m, 200m), new AracYilPnlRow(2025, 200m, 50m, 150m)],
        GelirKaynak: [new AracKirilimRow("Kira/Fatura", 500m, 100m)],
        GiderKategori: [new AracKirilimRow("MTV", 50m, 10m), new AracKirilimRow("Araç Gideri", 100m, 20m)],
        Olaylar: [],
        Kpi: new AracKpiDto(31, 3, 3, 25, 9.68m, 5.38m, 55.56m, 0.50m, 10.00m, 1.67m, 60, 1150m,
            200m, 200m, -183.33m, 300, 1),
        MaliyetModel: new MaliyetHesapSonuc(800m, 200m, 0m, 0m, 0m, 150m, 350m, 350m, 70m, 420m, 420m, 504m));

    [Fact]
    public void Karne_null_ise_null_doner() // uç 404'e çevirir
        => Assert.Null(KarneExportKatalog.AracKarne(null));

    [Fact]
    public void Karne_kv_alanlari_ve_bolumler_elle()
    {
        var t = KarneExportKatalog.AracKarne(OrnekKarne())!;
        Assert.Equal("Araç Karnesi 34KR99", t.Sheet);
        Assert.Equal(new[] { "Metrik", "Değer" }, t.Headers);

        object? Deger(string metrik) => Assert.Single(t.Rows, r => (string?)r[0] == metrik)[1];
        Assert.Equal("34KR99", Deger("Plaka"));
        Assert.Equal(500m, Deger("Gelir"));
        Assert.Equal(350m, Deger("Net Kâr (defter)"));
        Assert.Equal(9.68m, Deger("Doluluk %"));
        Assert.Equal(55.56m, Deger("ADR"));
        Assert.Equal(-183.33m, Deger("Ekonomik Kâr"));
        Assert.Equal(1150m, Deger("TCO"));
        Assert.Equal("31 / 3 / 3 / 25", Deger("Sahiplik / Kiralanan / Servis / Boş (gün)"));
        Assert.Equal(350m, Deger("Başabaş (aylık)"));                    // model bölümü
        Assert.Equal("300 / 100 / 200", Deger("2024 Gelir / Gider / Net")); // yıllık satır
        Assert.Equal(500m, Deger("Gelir: Kira/Fatura"));                 // kırılım
        Assert.Equal(50m, Deger("Gider: MTV"));
    }

    [Fact]
    public void Karne_modelsiz_bolum_atlanir()
    {
        var d = OrnekKarne() with { MaliyetModel = null };
        var t = KarneExportKatalog.AracKarne(d)!;
        Assert.DoesNotContain(t.Rows, r => (string?)r[0] == "Başabaş (aylık)");
    }

    [Fact]
    public void Filo_satirlar_atanmamis_ve_toplam_elle()
    {
        var d = new FiloAnalizDto(
            [
                new FiloAnalizRow(Guid.NewGuid(), "34FA99", "EKO", null, "Merkez",
                    310m, 0m, 310m, 9.68m, 31.00m, 0.00m, 31, 3, 6),
                new FiloAnalizRow(Guid.NewGuid(), "34FA98", null, null, null,
                    100m, 150m, -50m, null, null, null, 0, 0, null)
            ],
            ToplamGelir: 450m, ToplamGider: 190m, ToplamNetKar: 260m,
            AtanmamisGelir: 40m, AtanmamisGider: 40m,
            YasKohortu: []);
        var t = KarneExportKatalog.FiloAnaliz(d);

        Assert.Equal(11, t.Headers.Count);
        Assert.Equal(4, t.Rows.Count);                       // 2 satır + Atanmamış + TOPLAM
        Assert.Equal("34FA99", t.Rows[0][0]);
        Assert.Equal(31.00m, t.Rows[0][8]);                  // ROI kolonu
        Assert.Equal("(Atanmamış)", t.Rows[2][0]);
        Assert.Equal(40m, t.Rows[2][4]);
        Assert.Equal("TOPLAM", t.Rows[3][0]);
        Assert.Equal(450m, t.Rows[3][4]);                    // toplam Gelir (defter mutabakat satırı)
    }

    [Fact]
    public void Filo_atanmamis_sifirsa_satir_yok()
    {
        var d = new FiloAnalizDto([], 0m, 0m, 0m, 0m, 0m, []);
        var t = KarneExportKatalog.FiloAnaliz(d);
        var satir = Assert.Single(t.Rows);                   // yalnız TOPLAM
        Assert.Equal("TOPLAM", satir[0]);
    }
}
