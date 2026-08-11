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
    /// <summary>Kira sözleşmesi belge şablonları (Ek Koşullar sekmesindeki seçim dropdown'ı).</summary>
    public IReadOnlyList<RentACar.Domain.Entities.BelgeSablon> Sablonlar { get; set; } = [];

    /// <summary>Edit modu (PR-D): mevcut sözleşme; create'te null.</summary>
    public RentalContract? Rental { get; set; }
    public bool Edit => Rental is not null;

    // Edit modu verileri (sayfa şube-kapsamlı GetAsync BAŞARILI olduktan sonra doldurur — sızıntı kapısı)
    public IReadOnlyList<RentalAddOn> Kalemler { get; set; } = [];
    public IReadOnlyList<RentACar.Application.Personnel.PersonelSecim> Personeller { get; set; } = [];
    public IReadOnlyList<Invoice> Faturalar { get; set; } = [];
    /// <summary>FAZ 4.2-B3: dönem planı + tahakkuk önizlemesi (uygun kirada; boş = plansız kira).</summary>
    public IReadOnlyList<RentACar.Application.FaturaDonemleri.FaturaDonemOnizleme> FaturaDonemleri { get; set; } = [];
    /// <summary>FAZ 4.3: kiraya bağlı B2B dış hizmet alımları (grid).</summary>
    public IReadOnlyList<DisHizmetAlimi> DisHizmetler { get; set; } = [];
    public IReadOnlyList<Penalty> Cezalar { get; set; } = [];
    public string? TeslimAlanAd { get; set; }
    public string? DovizNot { get; set; }

    /// <summary>
    /// FAZ-47 — sözleşmenin İŞLEM ŞUBESİ adı. TÜRETİLMİŞTİR: <c>CikisOfisi → Location → Branch</c>
    /// (OfficeBranchInterceptor her kayıtta yeniden çözer). Ekranda bu yüzden SALT-OKUNUR gösterilir;
    /// şube değiştirmek için Çıkış Ofisi değiştirilir. Ofis Location master'ında eşleşmiyorsa null
    /// (salt-metin davranış — kilitlenme yok).
    /// </summary>
    public string? IslemSubeAdi { get; set; }
    /// <summary>Kullanıcının FinanceWrite yetkisi var mı (tahsilat/fatura butonları — 403 sürprizi yerine disabled).</summary>
    public bool FinansYetkisi { get; set; }
    /// <summary>OperationsWrite var mı (Kaydet/teslim/dönüş/uzat/ek hizmet/iptal — Muhasebe'de disabled + not).</summary>
    public bool OperasyonYetkisi { get; set; } = true;
    public bool Kirada => Edit && Rental!.Durum == RentACar.Domain.Enums.RentalStatus.Kirada;
    public bool TeslimEdildi => Edit && Rental!.CikisKm is not null;

    // ---- Edit prefill yardımcıları (create'te boş) ----
    public string S(Func<RentalContract, string?> f) => Edit ? f(Rental!) ?? "" : "";
    /// <summary>Sayı input'u prefill — InvariantCulture (tr virgülü number input'u boşaltıyordu; PR#27 dersi).</summary>
    public string D(Func<RentalContract, decimal?> f)
        => Edit && f(Rental!) is decimal d ? d.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    public string I(Func<RentalContract, int?> f) => Edit && f(Rental!) is int i ? i.ToString() : "";

    /// <summary>FAZ 4.4: aracın ömür-boyu doluluk yüzdesi (karne KPI'sından; edit+FinansYetkisi'nde dolar).</summary>
    public decimal? AracDolulukYuzde { get; set; }
    /// <summary>FAZ 4.4: kira dönemindeki HGS geçişleri (IHgsService — stub boş döner; entegrasyon bekleniyor).</summary>
    public IReadOnlyList<RentACar.Application.Integrations.TollCrossing> HgsGecisleri { get; set; } = [];
    /// <summary>FAZ 4.5: kiranın kaynak rezervasyonu (varsa) — Web Api OTA kutuları buradan okur.</summary>
    public Reservation? KaynakRezervasyon { get; set; }
    public bool B(Func<RentalContract, bool?> f) => Edit && f(Rental!) == true;
    /// <summary>Select option seçili mi (edit prefill).</summary>
    public bool Sel(Func<RentalContract, string?> f, string option) => Edit && f(Rental!) == option;

    // ---- FAZ-82: tenant form varsayılanları (yalnız ÖN-DOLDURMA; kaydedilen değer daima formdan gelir) ----
    /// <summary>Teslim (çıkış) formundaki "Çıkış Yakıt" ön-değeri. Ayar yoksa
    /// <see cref="RentACar.Application.TenantSettings.FormVarsayilanCozucu.YakitVarsayilan"/> (8) —
    /// yani sayfaya gömülü olan eski sabit.</summary>
    public int TenantVarsayilanYakit { get; set; }
        = RentACar.Application.TenantSettings.FormVarsayilanCozucu.YakitVarsayilan;

    /// <summary>Yeni kira formundaki "Hesaplama Tipi" dropdown'ının ön-seçili değeri; null = "—"
    /// (bugünkü davranış). Düzenleme modunda KULLANILMAZ — orada alan zaten salt-okunur.</summary>
    public string? TenantVarsayilanFiyatTuru { get; set; }

    /// <summary>Müsaitlik yenilemesi/yönlendirme sonrası korunan müşteri seçimi (?musteriId= query).</summary>
    public Guid? QMusteriId { get; set; }
    public string QMusteriGoruntu =>
        QMusteriId is Guid q && Customers.FirstOrDefault(c => c.Id == q) is { } m ? MusteriGoruntu(m) : "";

    // Müsaitlik penceresi (GET alt-formundan; araç listesi filtreli + tarihler prefill)
    public bool MusaitMod { get; set; }
    public string? Vfrom { get; set; }
    public string? Vto { get; set; }
    public string? Vgrup { get; set; }

    /// <summary>
    /// FAZ-19: müsaitlik listesindeki "Kirala" linkinden gelen araç ön-seçimi (?varac=).
    /// Yalnız YENİ kira formunda anlamlıdır; düzenlemede sözleşmenin kendi aracı gösterilir.
    /// </summary>
    public Guid? Varac { get; set; }

    // Sabit seçenek listeleri (RentalList ile aynı — TürevRent parite)
    public static readonly string[] KiralamaTurleri = ["Kısa Kiralama", "Uzun Kiralama", "İkame", "Aylık"];
    public static readonly string[] FaturalamaTipleri = ["Müşteri Ödemeli", "Full Credit", "Extralar Müşteriye Ait", "Drop Dahil", "Diğer"];
    // FAZ-82: liste artık Application'daki TEK kaynaktan gelir. Kopya kalsaydı, tenant varsayılanı o
    // listeye karşı doğrulandığı için en küçük yazım sapması ön-seçimi sessizce hiç eşleşmez yapardı.
    public static readonly string[] FiyatTurleri = RentACar.Application.Pricing.FiyatTuruSecenek.Hepsi;
    public static readonly string[] Dovizler = ["TL", "EURO", "USD"];
    /// <summary>FAZ-47 — Ödeme Şekli ComboBox önerileri (seç-veya-yaz; serbest metin de kabul edilir).
    /// BİLGİ alanıdır: gerçek tahsilat Kasa/Banka akışından geçer, bu seçim deftere yazmaz.</summary>
    public static readonly string[] OdemeSekilleri =
        ["Nakit", "Kredi Kartı", "Havale/EFT", "Cari Hesap (Vadeli)", "Çek", "Senet", "Ödeme Yok (Bedelsiz)"];

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
