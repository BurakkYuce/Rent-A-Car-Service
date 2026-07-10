using RentACar.Domain.Entities;

namespace RentACar.Web.Components.Pages.Bookings.KiraFormPaneller;

/// <summary>
/// Kira mega-form ortak görünüm modeli — sayfa (KiraForm.razor) doldurur, sekme panelleri parametreyle alır.
/// Create modu: Rental=null. Edit modu (PR-D): Rental dolu, formlar prefill + operasyon formları canlanır.
/// </summary>
public sealed class KiraFormVm
{
    public IReadOnlyList<Customer> Customers { get; set; } = [];
    public IReadOnlyList<Vehicle> Vehicles { get; set; } = [];
    public IReadOnlyList<Location> Locations { get; set; } = [];
    public IReadOnlyList<EkHizmetTanim> EkHizmetler { get; set; } = [];
    public IReadOnlyList<CoverageProduct> Sigortalar { get; set; } = [];
    public IReadOnlyList<string> Kaynaklar { get; set; } = [];
    public IReadOnlyList<string> OzelKodlar { get; set; } = [];
    public IReadOnlyList<KurKaydi> Kurlar { get; set; } = [];

    /// <summary>Edit modu (PR-D): mevcut sözleşme; create'te null.</summary>
    public RentalContract? Rental { get; set; }
    public bool Edit => Rental is not null;

    // Müsaitlik penceresi (GET alt-formundan; araç listesi filtreli + tarihler prefill)
    public bool MusaitMod { get; set; }
    public string? Vfrom { get; set; }
    public string? Vto { get; set; }
    public string? Vgrup { get; set; }

    // Sabit seçenek listeleri (RentalList ile aynı — referans sistem parite)
    public static readonly string[] KiralamaTurleri = ["Kısa Kiralama", "Uzun Kiralama", "İkame", "Aylık"];
    public static readonly string[] FaturalamaTipleri = ["Müşteri Ödemeli", "Full Credit", "Extralar Müşteriye Ait", "Drop Dahil", "Diğer"];
    public static readonly string[] FiyatTurleri = ["Otomatik", "KDV Dahil Günlük", "Günlük", "KDV Dahil Toplam", "Toplam"];
    public static readonly string[] Dovizler = ["TL", "EURO", "USD"];

    // Tarih sınırları (UI aynası; asıl koruma sunucuda — TarihPolitikasi: kira geçmişe açık, gelecek ≤ +1 yıl)
    public static string BugunStr => DateTime.Today.ToString("yyyy-MM-dd");
    public static string MaxBasTarStr => DateTimeOffset.Now.AddYears(1).ToString("yyyy-MM-ddTHH:mm");

    /// <summary>Müsaitlik penceresinden tarih prefill (yoksa boş).</summary>
    public string BasTarPrefill => MusaitMod && !string.IsNullOrEmpty(Vfrom) ? $"{Vfrom}T09:00" : "";
    public string BitTarPrefill => MusaitMod && !string.IsNullOrEmpty(Vto) ? $"{Vto}T09:00" : "";

    /// <summary>Arama datalist görüntü değeri — benzersizlik için kısa id son eki (JS birebir eşleşme ile çözer).</summary>
    public static string MusteriGoruntu(Customer c) => $"{c.DisplayName} · #{c.Id.ToString()[..8]}";
    public static string AracGoruntu(Vehicle v) => $"{v.Plaka} · {v.Marka} {v.Tip} · #{v.Id.ToString()[..8]}";
}
