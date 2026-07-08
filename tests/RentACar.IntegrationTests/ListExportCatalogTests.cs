using RentACar.Application.Bookings;
using RentACar.Domain.Common;
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

    [Fact]
    public void Cezalar_projeksiyon()
    {
        var p = new Penalty { No = "CZ-1", CezaTuru = "Hız", TebligTarihi = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            VadeTarihi = new(2026, 1, 16, 0, 0, 0, TimeSpan.Zero), Tutar = 500m, Sebep = "Radar" };
        var t = ListExportCatalog.Cezalar([p]);
        Assert.Equal(7, t.Headers.Count);
        Assert.Equal("CZ-1", t.Rows[0][0]);
        Assert.Equal(500m, t.Rows[0][4]);
        Assert.Equal("Radar", t.Rows[0][6]);
    }

    [Fact]
    public void Giderler_projeksiyon()
    {
        var e = new Expense { No = "GD-1", Tarih = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), Sube = "Merkez",
            EvrakNo = "E1", NetTutar = 100m, KdvOrani = 0.20m, KdvTutar = 20m, GenelToplam = 120m, Currency = "TRY", Aciklama = "Yakıt" };
        var t = ListExportCatalog.Giderler([e]);
        Assert.Equal(13, t.Headers.Count);
        Assert.Equal("GD-1", t.Rows[0][0]);
        Assert.Equal(120m, t.Rows[0][8]);
        Assert.Equal("Yakıt", t.Rows[0][12]);
    }

    [Fact]
    public void NakitIslemler_projeksiyon()
    {
        var n = new CashTransaction { No = "TH-1", Tarih = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            Amount = new Money(250m, "TRY", 1m), KarsiHesap = LedgerAccountType.Kasa, TersKayitMi = false, Aciklama = "Peşin" };
        var t = ListExportCatalog.NakitIslemler([n]);
        Assert.Equal(8, t.Headers.Count);
        Assert.Equal("TH-1", t.Rows[0][0]);
        Assert.Equal(250m, t.Rows[0][3]);
        Assert.Equal("TRY", t.Rows[0][4]);
        Assert.Equal("Hayır", t.Rows[0][6]);
    }

    [Fact]
    public void AracSatislari_projeksiyon()
    {
        var s = new VehicleSale { No = "AS-1", Tarih = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero), NoterNo = "N1",
            SatisNet = 500000m, KdvOrani = 0.20m, KdvTutar = 100000m, GenelToplam = 600000m, Currency = "TRY", Aciklama = "2.el" };
        var t = ListExportCatalog.AracSatislari([s]);
        Assert.Equal(10, t.Headers.Count);
        Assert.Equal("AS-1", t.Rows[0][0]);
        Assert.Equal(600000m, t.Rows[0][6]);
    }

    [Fact]
    public void AracSiparisleri_projeksiyon()
    {
        var s = new AracSiparis { No = "SP-1", Tedarikci = "Fiat Bayi", SiparisTarihi = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
            Marka = "Fiat", Tip = "Egea", Grup = "B", Adet = 5, BirimFiyat = 800000m, Currency = "TRY" };
        var t = ListExportCatalog.AracSiparisleri([s]);
        Assert.Equal(12, t.Headers.Count);
        Assert.Equal("SP-1", t.Rows[0][0]);
        Assert.Equal("Fiat Bayi", t.Rows[0][1]);
        Assert.Equal(5, t.Rows[0][7]);
    }

    [Fact]
    public void AracKredileri_projeksiyon()
    {
        var k = new AracKredi { No = "KR-1", BankaAdi = "Ziraat", KrediTutari = 1000000m, FaizOran = 2.5m,
            TaksitSayisi = 36, OdenenTaksit = 12, BaslangicTarihi = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), Currency = "TRY" };
        var t = ListExportCatalog.AracKredileri([k]);
        Assert.Equal(10, t.Headers.Count);
        Assert.Equal("Ziraat", t.Rows[0][1]);
        Assert.Equal(36, t.Rows[0][4]);
        Assert.Equal(12, t.Rows[0][5]);
    }

    [Fact]
    public void Baflar_projeksiyon()
    {
        var b = new Baf { No = "BF-1", CikisTarihi = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero), CikisKm = 1000,
            CikisYakit = 80, Sube = "Merkez", Aciklama = "zimmet" };
        var t = ListExportCatalog.Baflar([b]);
        Assert.Equal(10, t.Headers.Count);
        Assert.Equal("BF-1", t.Rows[0][0]);
        Assert.Equal(1000, t.Rows[0][2]);
        Assert.Equal("Merkez", t.Rows[0][7]);
    }

    [Fact]
    public void Kiralar_projeksiyon()
    {
        var r = new RentalRow { SozlesmeNo = "RZ-1", MusteriAd = "Ali Veli", Plaka = "34ABC01",
            BasTar = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), BitTar = new(2026, 1, 4, 0, 0, 0, TimeSpan.Zero),
            Gun = 3, Tutar = 300m, Bakiye = 100m, Durum = RentalStatus.Kirada, Faturali = false };
        var t = ListExportCatalog.Kiralar([r]);
        Assert.Equal(10, t.Headers.Count);
        Assert.Equal("RZ-1", t.Rows[0][0]);
        Assert.Equal("Ali Veli", t.Rows[0][1]);
        Assert.Equal(3, t.Rows[0][5]);
        Assert.Equal("Hayır", t.Rows[0][9]);
    }

    [Fact]
    public void Rezervasyonlar_projeksiyon()
    {
        var r = new Reservation { ReservationNo = "RE-1", BasTar = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            BitTar = new(2026, 2, 3, 0, 0, 0, TimeSpan.Zero), CikisOfisi = "Merkez", DonusOfisi = "Ankara",
            Gun = 2, GunlukUcret = 150m, Tutar = 300m };
        var t = ListExportCatalog.Rezervasyonlar([r]);
        Assert.Equal(9, t.Headers.Count);
        Assert.Equal("RE-1", t.Rows[0][0]);
        Assert.Equal("Merkez", t.Rows[0][4]);
        Assert.Equal(300m, t.Rows[0][8]);
    }

    [Fact]
    public void Lokasyonlar_projeksiyon()
    {
        var l = new Location { Kod = "IST", Ad = "İstanbul Havalimanı", Adres = "Arnavutköy", Telefon = "5551112233",
            Eposta = "ist@x.c", CalismaSaatleri = "09-18", TeslimUcreti = 50m, Sube = "Merkez", Aktif = true };
        var t = ListExportCatalog.Lokasyonlar([l]);
        Assert.Equal(9, t.Headers.Count);
        Assert.Equal("IST", t.Rows[0][0]);
        Assert.Equal(50m, t.Rows[0][6]);
        Assert.Equal("Evet", t.Rows[0][8]);
    }

    [Fact]
    public void DropTanimlari_projeksiyon()
    {
        var d = new DropTanim { Lokasyon = "IST", Sube = "Merkez", KarsilamaSekli = "Kapıda", CalismaSekli = "7/24",
            OzelIletisim = "x", Aktif = true };
        var t = ListExportCatalog.DropTanimlari([d]);
        Assert.Equal(6, t.Headers.Count);
        Assert.Equal("IST", t.Rows[0][0]);
        Assert.Equal("Kapıda", t.Rows[0][2]);
        Assert.Equal("Evet", t.Rows[0][5]);
    }

    [Fact]
    public void Personel_PII_decrypt_uygulanir()
    {
        // HASSAS PII: TC + maaş cipher'ları decrypt Func'ıyla çözülür (uçta ISecretProtector; burada sahte decrypt).
        // Bağımsız oracle: cipher→düz eşlemesi doğru sütunlara girer.
        var p = new Personel { Kod = "P1", Ad = "Ayşe", Soyad = "Yıldız", TcKimlikEnc = "TC_ENC", MaasEnc = "MAAS_ENC",
            Sube = "Merkez", Aktif = true, IseGiris = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var t = ListExportCatalog.Personel([p], c => c == "TC_ENC" ? "12345678901" : c == "MAAS_ENC" ? "45000" : c);
        Assert.Equal(10, t.Headers.Count);
        Assert.Equal("TC Kimlik", t.Headers[3]);
        Assert.Equal("Maaş", t.Headers[7]);
        Assert.Equal("P1", t.Rows[0][0]);
        Assert.Equal("12345678901", t.Rows[0][3]);   // decrypt uygulandı
        Assert.Equal("45000", t.Rows[0][7]);
        Assert.Equal("Aktif", t.Rows[0][9]);
    }
}
