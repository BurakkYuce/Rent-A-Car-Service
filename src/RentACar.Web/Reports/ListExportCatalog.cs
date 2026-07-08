using RentACar.Application.Bookings;
using RentACar.Domain.Entities;

namespace RentACar.Web.Reports;

/// <summary>
/// Liste export sütun tanımları — saf, test-edilebilir projeksiyonlar. Her export'un başlıkları + hücre eşlemesi
/// TEK yerde: "sütun değiştir/ekle" burada yapılır. Tam TürevRent sütun setleri docs/parite/09-export-karsilastirma.md'de
/// (genişletme menüsü — şimdi anlamlı alt-küme). PII (TC/Vergi) okuma yolunda zaten decrypt edilir; export ucu gate'li (ViewReports).
/// </summary>
public static class ListExportCatalog
{
    public static ExportTable Araclar(IReadOnlyList<Vehicle> v) => new(
        "Araclar",
        // İlk 15 kolon geriye-uyum için SABİT sırada; kalanlar (parite derinliği) sona eklendi.
        ["Plaka", "Marka", "Tip", "Detay Tipi", "Grup", "Şube", "Model Yılı", "Renk", "Yakıt", "Vites", "SIPP", "KM", "Durum", "Özel Kod", "Kasa Tipi",
         "Segment", "Filo Durumu", "Şasi No", "Motor No", "Motor Gücü", "Silindir Hacmi", "Ruhsat No", "Tescil Tarihi", "Araç Sahibi",
         "Alım Bedeli", "Alım Tarihi", "Alış Vergisiz", "Alış ÖTV", "Alış KDV", "Aylık Maliyet", "Filo Yön. Maliyeti", "2.El Değer",
         "Filo Giriş", "Filo Çıkış", "HGS No", "OGS No", "Kira KM Limiti", "Son Bakım Tarih", "Son Bakım KM", "Lastik Durumu"],
        v.Select(x => new object?[]
        {
            x.Plaka, x.Marka, x.Tip, x.DetayTipi, x.Grup, x.Sube, x.ModelYili, x.Renk,
            x.Yakit.ToString(), x.Vites?.ToString(), x.Sipp, x.Km, x.Durum.ToString(), x.OzelKod1, x.KasaTipi,
            x.Segment, x.FiloDurum?.ToString(), x.SasiNo, x.MotorNo, x.MotorGucu, x.SilindirHacmi, x.RuhsatNo, D(x.TescilTarihi), x.AracSahibi,
            x.AlimBedeli, D(x.AlimTarihi), x.AlisVergisiz, x.AlisOtv, x.AlisKdv, x.AylikMaliyet, x.FiloYonetimMaliyeti, x.IkinciElDeger,
            D(x.FiloGirisTarih), D(x.FiloCikisTarih), x.HgsNo, x.OgsNo, x.KiraKmLimiti, D(x.SonBakimTarih), x.SonBakimKm, x.LastikDurumu
        }).ToList());

    public static ExportTable Cariler(IReadOnlyList<Customer> c) => new(
        "Cariler",
        // İlk 10 kolon SABİT; kalanlar (CRM/finans parite derinliği) sona eklendi.
        ["Ünvan/Ad", "Tip", "TC Kimlik", "Vergi No", "Telefon", "E-posta", "İl", "İlçe", "Kaynak", "Vade Gün",
         "Vergi Dairesi", "GSM2", "Adres", "Sınıf", "Müşteri Temsilcisi", "İYS İzinli", "Fatura Dönemi", "Risk Limiti", "HGS Yansıtma", "Özel Cari Tip"],
        c.Select(x => new object?[]
        {
            x.DisplayName, x.Tip.ToString(), x.TcKimlik, x.VergiNo, x.CepTel, x.Email, x.Il, x.Ilce, x.Kaynak, x.VadeGun,
            x.VergiDairesi, x.Gsm2, x.Adres, x.Sinif, x.MusteriTemsilcisi, E(x.IysIzinli), x.FaturaDonemi, x.RiskLimiti, x.HgsYansitmaTuru, x.OzelCariTip
        }).ToList());

    public static ExportTable Faturalar(IReadOnlyList<Invoice> f) => new(
        "Faturalar",
        ["No", "Tarih", "Net", "KDV", "Toplam", "Durum"],
        f.Select(x => new object?[]
        {
            x.No, x.Tarih.ToString("yyyy-MM-dd"), x.NetTutar, x.KdvTutar, x.GenelToplam, x.Durum.ToString()
        }).ToList());

    public static ExportTable Cezalar(IReadOnlyList<Penalty> c) => new(
        "Cezalar",
        ["No", "Ceza Türü", "Tebliğ Tarihi", "Vade", "Tutar", "Durum", "Sebep"],
        c.Select(x => new object?[]
        {
            x.No, x.CezaTuru, x.TebligTarihi.ToString("yyyy-MM-dd"), x.VadeTarihi.ToString("yyyy-MM-dd"),
            x.Tutar, x.Durum.ToString(), x.Sebep
        }).ToList());

    public static ExportTable Giderler(IReadOnlyList<Expense> g) => new(
        "Giderler",
        ["No", "Tip", "Tarih", "Şube", "Evrak No", "Net", "KDV Oranı", "KDV", "Genel Toplam", "Döviz", "Ödeme", "Hesap", "Açıklama"],
        g.Select(x => new object?[]
        {
            x.No, x.Tip.ToString(), x.Tarih.ToString("yyyy-MM-dd"), x.Sube, x.EvrakNo, x.NetTutar, x.KdvOrani,
            x.KdvTutar, x.GenelToplam, x.Currency, x.OdemeYontemi.ToString(), x.KasaBankaHesap.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable NakitIslemler(IReadOnlyList<CashTransaction> n) => new(
        "Nakit İşlemler",
        ["No", "Tip", "Tarih", "Tutar", "Döviz", "Karşı Hesap", "Ters mi", "Açıklama"],
        n.Select(x => new object?[]
        {
            x.No, x.Tip.ToString(), x.Tarih.ToString("yyyy-MM-dd"), x.Amount.Amount, x.Amount.Currency,
            x.KarsiHesap.ToString(), x.TersKayitMi ? "Evet" : "Hayır", x.Aciklama
        }).ToList());

    public static ExportTable AracSatislari(IReadOnlyList<VehicleSale> s) => new(
        "Araç Satışları",
        ["No", "Tarih", "Noter No", "Net", "KDV Oranı", "KDV", "Genel Toplam", "Döviz", "Durum", "Açıklama"],
        s.Select(x => new object?[]
        {
            x.No, x.Tarih.ToString("yyyy-MM-dd"), x.NoterNo, x.SatisNet, x.KdvOrani, x.KdvTutar,
            x.GenelToplam, x.Currency, x.Durum.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable AracSiparisleri(IReadOnlyList<AracSiparis> s) => new(
        "Araç Siparişleri",
        ["No", "Tedarikçi", "Sipariş Tarihi", "Beklenen Teslim", "Marka", "Tip", "Grup", "Adet", "Birim Fiyat", "Döviz", "Durum", "Açıklama"],
        s.Select(x => new object?[]
        {
            x.No, x.Tedarikci, x.SiparisTarihi.ToString("yyyy-MM-dd"), x.BeklenenTeslim?.ToString("yyyy-MM-dd"),
            x.Marka, x.Tip, x.Grup, x.Adet, x.BirimFiyat, x.Currency, x.Durum.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable AracKredileri(IReadOnlyList<AracKredi> k) => new(
        "Araç Kredileri",
        ["No", "Banka", "Kredi Tutarı", "Faiz %", "Taksit", "Ödenen Taksit", "Başlangıç", "Döviz", "Durum", "Açıklama"],
        k.Select(x => new object?[]
        {
            x.No, x.BankaAdi, x.KrediTutari, x.FaizOran, x.TaksitSayisi, x.OdenenTaksit,
            x.BaslangicTarihi.ToString("yyyy-MM-dd"), x.Currency, x.Durum.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable Baflar(IReadOnlyList<Baf> b) => new(
        "BAF (Personel Araç Tahsis)",
        ["No", "Çıkış Tarihi", "Çıkış KM", "Çıkış Yakıt", "Dönüş Tarihi", "Dönüş KM", "Dönüş Yakıt", "Şube", "Durum", "Açıklama"],
        b.Select(x => new object?[]
        {
            x.No, x.CikisTarihi.ToString("yyyy-MM-dd"), x.CikisKm, x.CikisYakit, x.DonusTarihi?.ToString("yyyy-MM-dd"),
            x.DonusKm, x.DonusYakit, x.Sube, x.Durum.ToString(), x.Aciklama
        }).ToList());

    public static ExportTable Kiralar(IReadOnlyList<RentalRow> r) => new(
        "Kiralar",
        ["Sözleşme No", "Müşteri", "Plaka", "Başlangıç", "Bitiş", "Gün", "Tutar", "Bakiye", "Durum", "Faturalı"],
        r.Select(x => new object?[]
        {
            x.SozlesmeNo, x.MusteriAd, x.Plaka, x.BasTar.ToString("yyyy-MM-dd"), x.BitTar.ToString("yyyy-MM-dd"),
            x.Gun, x.Tutar, x.Bakiye, x.Durum.ToString(), x.Faturali ? "Evet" : "Hayır"
        }).ToList());

    public static ExportTable Rezervasyonlar(IReadOnlyList<Reservation> r) => new(
        "Rezervasyonlar",
        ["Rez No", "Durum", "Başlangıç", "Bitiş", "Çıkış Ofisi", "Dönüş Ofisi", "Gün", "Günlük Ücret", "Tutar"],
        r.Select(x => new object?[]
        {
            x.ReservationNo, x.Durum.ToString(), x.BasTar.ToString("yyyy-MM-dd"), x.BitTar.ToString("yyyy-MM-dd"),
            x.CikisOfisi, x.DonusOfisi, x.Gun, x.GunlukUcret, x.Tutar
        }).ToList());

    public static ExportTable Lokasyonlar(IReadOnlyList<Location> l) => new(
        "Lokasyonlar",
        ["Kod", "Ad", "Adres", "Telefon", "E-posta", "Çalışma Saatleri", "Teslim Ücreti", "Şube", "Aktif"],
        l.Select(x => new object?[]
        {
            x.Kod, x.Ad, x.Adres, x.Telefon, x.Eposta, x.CalismaSaatleri, x.TeslimUcreti, x.Sube, x.Aktif ? "Evet" : "Hayır"
        }).ToList());

    public static ExportTable DropTanimlari(IReadOnlyList<DropTanim> d) => new(
        "Drop Tanımları",
        ["Lokasyon", "Şube", "Karşılama Şekli", "Çalışma Şekli", "Özel İletişim", "Aktif"],
        d.Select(x => new object?[]
        {
            x.Lokasyon, x.Sube, x.KarsilamaSekli, x.CalismaSekli, x.OzelIletisim, x.Aktif ? "Evet" : "Hayır"
        }).ToList());

    /// <summary>Personel — HASSAS PII (TC + maaş). <paramref name="decrypt"/> cipher'ları çözer (ISecretProtector);
    /// katalog saf kalır (test'te sahte decrypt). Uç ManageUsers (Admin) gate'li + KVKK notu (docs/ops/kvkk-export-notu.md).</summary>
    public static ExportTable Personel(IReadOnlyList<Personel> p, Func<string?, string?> decrypt) => new(
        "Personel",
        ["Kod", "Ad", "Soyad", "TC Kimlik", "İşe Giriş", "İşe Çıkış", "Sürücü Belge No", "Maaş", "Şube", "Durum"],
        p.Select(x => new object?[]
        {
            x.Kod, x.Ad, x.Soyad, decrypt(x.TcKimlikEnc), x.IseGiris?.ToString("yyyy-MM-dd"), x.IseCikis?.ToString("yyyy-MM-dd"),
            x.SurucuBelgeNo, decrypt(x.MaasEnc), x.Sube, x.Aktif ? "Aktif" : "Pasif"
        }).ToList());

    // Hücre biçimleyiciler (sütun zenginleştirme için): bool → Evet/Hayır, nullable tarih → yyyy-MM-dd.
    private static string E(bool b) => b ? "Evet" : "Hayır";
    private static string? D(DateTimeOffset? d) => d?.ToString("yyyy-MM-dd");
}
