using RentACar.Domain.Enums;

namespace RentACar.Application.Finance;

/// <summary>
/// Fatura SATIRI seviyesinde birleştirilmiş görünüm (canlı <c>fatura_detay_listesi.aspx</c>).
/// Her satır bir <c>InvoiceLine</c>'dır; fatura başlığı, cari, kira ve rezervasyon bilgisi yanına
/// çözümlenir.
///
/// <para><b>Para yeniden HESAPLANMAZ:</b> <see cref="SatirNet"/>/<see cref="SatirKdv"/>/
/// <see cref="SatirToplam"/> faturanın kesildiği andaki değerlerdir, doğrudan satırdan okunur.
/// Rapor katmanında yeniden hesaplamak, oran/yuvarlama farkıyla faturadan sapma üretirdi.</para>
///
/// <para><see cref="Kur"/> satır tutarlarının TL karşılığını çıkarmak için taşınır; satır tutarları
/// faturanın KENDİ dövizindedir.</para>
/// </summary>
public sealed record FaturaSatirDto(
    // Fatura başlığı
    Guid FaturaId, string FaturaNo, DateTimeOffset Tarih, DateTimeOffset? VadeTarihi,
    InvoiceStatus Durum, bool IadeMi, bool ManuelMi, string Doviz, decimal Kur,
    // Cari
    Guid CariId, string CariAd, string? CariSehir, string? CariEmail, string? CariVergiNo,
    // Satır
    string Aciklama, decimal Miktar, decimal BirimNetFiyat, decimal KdvOrani,
    decimal SatirNet, decimal SatirKdv, decimal SatirToplam,
    // Kira / araç / rezervasyon (fatura kiraya bağlı değilse null)
    Guid? RentalId, string? SozlesmeNo, string? Plaka, string? CikisOfisi, string? RezervasyonKaynagi)
{
    /// <summary>İptal edilmiş fatura satırı mı (toplamlara HİÇ girmez).</summary>
    public bool Iptal => Durum == InvoiceStatus.Iptal;

    /// <summary>
    /// Toplamlarda kullanılacak İŞARET. İade faturaları veritabanında POZİTİF tutarla saklanır
    /// (<c>InvoiceService.CreateIadeAsync</c>: satırlar kaynak faturadan aynen aynalanır, defter
    /// tarafı ters yönde yazılır). Tüketici işareti KENDİSİ vermek zorundadır — verilmezse bir iade,
    /// ciroyu düşürmek yerine ARTIRIR. KDV raporu da aynı kuralı uygular.
    /// </summary>
    private decimal Isaret => IadeMi ? -1m : 1m;

    /// <summary>Satır net tutarı, iade işaretiyle (TL-baz).</summary>
    public decimal IsaretliNetTl => Isaret * SatirNet * Kur;
    /// <summary>Satır KDV tutarı, iade işaretiyle (TL-baz).</summary>
    public decimal IsaretliKdvTl => Isaret * SatirKdv * Kur;
    /// <summary>Satır brüt tutarı, iade işaretiyle (TL-baz). Liste toplamı BUNU kullanır.</summary>
    public decimal IsaretliToplamTl => Isaret * SatirToplam * Kur;
}

/// <summary>Fatura detay listesi filtresi. Boş filtre = tüm satırlar.</summary>
public sealed class FaturaSatirFilter
{
    public Guid? CariId { get; set; }
    /// <summary>Fatura no / satır açıklaması / sözleşme no içinde geçen metin.</summary>
    public string? Ara { get; set; }
    /// <summary>Plaka (kısmi). Araç tablosundan çözülür.</summary>
    public string? Plaka { get; set; }
    /// <summary>Kiranın çıkış ofisi (tam eşleşme).</summary>
    public string? Ofis { get; set; }
    public DateTimeOffset? Bas { get; set; }
    public DateTimeOffset? Bit { get; set; }
    /// <summary><c>false</c> (varsayılan) → iptal edilmiş faturaların satırları da listelenir
    /// (durum kolonuyla ayrışır). <c>true</c> → yalnız geçerli satırlar.</summary>
    public bool IptalleriGizle { get; set; }
    /// <summary>Sayfa yükü sınırı — fatura satırı sayısı hızla büyür.</summary>
    public int EnFazla { get; set; } = 2000;
}
