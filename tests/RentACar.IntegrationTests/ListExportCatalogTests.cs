using RentACar.Domain.Entities;
using RentACar.Domain.Enums;
using RentACar.Web.Reports;

namespace RentACar.IntegrationTests;

/// <summary>
/// Export paritesi PR1 — ListExportCatalog saf projeksiyonları (başlık + hücre eşlemesi). Bağımsız oracle:
/// elle kurulan entity → beklenen başlık dizisi + hücre değerleri. PII (TC) decrypt edilmiş değerle export'a geçer
/// (okuma yolu decrypt eder; endpoint ViewReports-gate'li). DB gerektirmez.
/// </summary>
public sealed class ListExportCatalogTests
{
    [Fact]
    public void Araclar_zengin_basliklar_ve_hucreler()
    {
        var v = new Vehicle
        {
            Plaka = "34ABC01", Marka = "Fiat", Tip = "Egea", DetayTipi = "Sedan", Grup = "B", Sube = "Merkez",
            ModelYili = 2023, Renk = "Beyaz", Yakit = FuelType.Dizel, Vites = Vites.Manuel, Sipp = "CDMR",
            Km = 45000, Durum = VehicleStatus.Musait, OzelKod1 = "K1", KasaTipi = "Sedan"
        };
        var t = ListExportCatalog.Araclar([v]);

        Assert.Equal(15, t.Headers.Count);          // 6 → 15 zenginleşti
        Assert.Equal("Plaka", t.Headers[0]);
        Assert.Equal("Model Yılı", t.Headers[6]);
        Assert.Single(t.Rows);
        Assert.Equal("34ABC01", t.Rows[0][0]);
        Assert.Equal(2023, t.Rows[0][6]);
        Assert.Equal("Dizel", t.Rows[0][8]);        // Yakıt enum → metin
        Assert.Equal(45000, t.Rows[0][11]);         // KM
        Assert.Equal("K1", t.Rows[0][13]);          // Özel Kod
    }

    [Fact]
    public void Cariler_PII_TC_decrypt_degeriyle_export_a_gecer()
    {
        var c = new Customer
        {
            Tip = CariType.Bireysel, Ad = "Ali", Soyad = "Veli", TcKimlik = "12345678901", VergiNo = null,
            CepTel = "5551112233", Email = "a@b.c", Il = "İstanbul", Ilce = "Kadıköy", Kaynak = "Web", VadeGun = 30
        };
        var t = ListExportCatalog.Cariler([c]);

        Assert.Equal(10, t.Headers.Count);          // 4 → 10
        Assert.Equal("TC Kimlik", t.Headers[2]);
        Assert.Equal("12345678901", t.Rows[0][2]);  // decrypt edilmiş TC export'ta (KVKK: gate'li uç)
        Assert.Equal("5551112233", t.Rows[0][4]);
        Assert.Equal("Kadıköy", t.Rows[0][7]);
    }

    [Fact]
    public void Faturalar_satir_sayisi_ve_basliklar()
    {
        var f = new Invoice { No = "FT-000001", Tarih = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            NetTutar = 100m, KdvTutar = 20m, GenelToplam = 120m, Durum = InvoiceStatus.Kesildi };
        var t = ListExportCatalog.Faturalar([f]);

        Assert.Equal(6, t.Headers.Count);
        Assert.Equal("FT-000001", t.Rows[0][0]);
        Assert.Equal("2026-01-15", t.Rows[0][1]);
        Assert.Equal(120m, t.Rows[0][4]);
    }
}
