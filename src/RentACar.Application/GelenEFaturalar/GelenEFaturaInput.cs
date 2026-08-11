using RentACar.Domain.Enums;

namespace RentACar.Application.GelenEFaturalar;

/// <summary>Gelen e-Fatura elle giriş modeli (GİB'den çekildiğinde EInvoiceInboxItem'dan da beslenir).</summary>
public sealed class GelenEFaturaInput
{
    public string Ettn { get; set; } = string.Empty;
    public string GonderenVkn { get; set; } = string.Empty;
    public string GonderenUnvan { get; set; } = string.Empty;
    public DateTimeOffset? Tarih { get; set; }
    public decimal NetTutar { get; set; }
    public decimal KdvTutar { get; set; }
    public decimal GenelToplam { get; set; }
    public string? Currency { get; set; }
    public string? Aciklama { get; set; }
}

/// <summary>
/// FAZ-55 (a): KDV oran kırılımı + araç/gider-kategori/cari bağlama girdisi. Hepsi BİLGİ alanıdır —
/// deftere kendi başına yazmaz; yalnız "Giderleştir" aksiyonuna girdi olur.
/// null alan = "dokunma" DEĞİL, "temizle": form her kaydedişte tüm kırılımı gönderir (kısmi kayıt
/// kırılımın toplamla tutarlılığını bozardı).
/// </summary>
public sealed class GelenEFaturaBaglamaInput
{
    public Guid Id { get; set; }

    public decimal? Kdv20Matrah { get; set; }
    public decimal? Kdv20 { get; set; }
    public decimal? Kdv10Matrah { get; set; }
    public decimal? Kdv10 { get; set; }
    public decimal? Kdv1Matrah { get; set; }
    public decimal? Kdv1 { get; set; }
    public decimal? Kdv0Matrah { get; set; }

    public Guid? VehicleId { get; set; }
    public Guid? ExpenseCategoryId { get; set; }
    public Guid? CariId { get; set; }
    public ExpenseType? GiderTipi { get; set; }
}

/// <summary>
/// FAZ-55 (b): "Giderleştir" girdisi — gelen faturayı, MEVCUT gider yolundan (Borç Gider(net) +
/// Borç KDV(indirilecek) / Alacak Kasa·Banka·Cari(gross)) deftere yansıtır. Defter şeması DEĞİŞMEZ.
/// </summary>
public sealed class GelenEFaturaGiderInput
{
    public Guid Id { get; set; }

    /// <summary>Karşı hesap seçimi. Varsayılan AçıkHesap: gelen (tedarikçi) faturası tipik olarak
    /// henüz ödenmemiştir → tedarikçi cariye borçlanılır.</summary>
    public OdemeYontemi OdemeYontemi { get; set; } = OdemeYontemi.AcikHesap;

    /// <summary>AçıkHesap'ta tedarikçi cari. Boşsa faturanın kendi <c>CariId</c>'si kullanılır.</summary>
    public Guid? CariId { get; set; }

    // NOT: karşı hesap türü için AYRI alan YOK — <see cref="OdemeYontemi"/> TEK BAŞINA belirler
    // (Nakit→Kasa, Banka→Banka, AçıkHesap→Cari). ExpenseInput.KasaBankaHesap alanını ExpenseService
    // zaten yok sayıyor; burada da açığa çıkarmak "seçtim ama etkisi yok" tuzağı üretirdi.

    /// <summary>Gider belgesine yazılacak şube (kapsam/raporlama).</summary>
    public string? Sube { get; set; }
}
