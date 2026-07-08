using RentACar.Application.Bookings;
using RentACar.Domain.Entities;

namespace RentACar.Web.Reports;

/// <summary>
/// Liste export sütun tanımları — saf, test-edilebilir projeksiyonlar. Her export'un başlıkları + hücre eşlemesi
/// TEK yerde: "sütun değiştir/ekle" burada yapılır. Tam referans sistem sütun setleri docs/parite/09-export-karsilastirma.md'de
/// (genişletme menüsü — şimdi anlamlı alt-küme). PII (TC/Vergi) okuma yolunda zaten decrypt edilir; export ucu gate'li (ViewReports).
/// </summary>
public static class ListExportCatalog
{
    public static ExportTable Araclar(IReadOnlyList<Vehicle> v) => new(
        "Araclar",
        ["Plaka", "Marka", "Tip", "Detay Tipi", "Grup", "Şube", "Model Yılı", "Renk", "Yakıt", "Vites", "SIPP", "KM", "Durum", "Özel Kod", "Kasa Tipi"],
        v.Select(x => new object?[]
        {
            x.Plaka, x.Marka, x.Tip, x.DetayTipi, x.Grup, x.Sube, x.ModelYili, x.Renk,
            x.Yakit.ToString(), x.Vites?.ToString(), x.Sipp, x.Km, x.Durum.ToString(), x.OzelKod1, x.KasaTipi
        }).ToList());

    public static ExportTable Cariler(IReadOnlyList<Customer> c) => new(
        "Cariler",
        ["Ünvan/Ad", "Tip", "TC Kimlik", "Vergi No", "Telefon", "E-posta", "İl", "İlçe", "Kaynak", "Vade Gün"],
        c.Select(x => new object?[]
        {
            x.DisplayName, x.Tip.ToString(), x.TcKimlik, x.VergiNo, x.CepTel, x.Email, x.Il, x.Ilce, x.Kaynak, x.VadeGun
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
}
