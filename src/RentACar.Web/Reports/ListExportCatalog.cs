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
}
