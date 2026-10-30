using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Application.SiteContent;
using RentACar.Domain.Entities;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>Menü/alt bilgi bağlantısı. (Ad `NavLink` DEĞİL: Blazor'ın Routing.NavLink bileşeniyle çakışır.)</summary>
public sealed record SiteLink(string Href, string Label);

/// <summary>Seçilen kiralama aralığı (Card slotunun isteğe bağlı <c>Dates</c> parametresi).</summary>
public sealed record StayDates(DateTimeOffset Start, DateTimeOffset End, int Days);

/// <summary>
/// Card slotunun <c>Card</c> parametresi — ilan kartının TEMADAN BAĞIMSIZ verisi. Sayfa kurar, tema yalnız basar.
/// <see cref="Days"/>/<see cref="Total"/> null ise ziyaretçi tarih seçmemiştir: "müsait" DENMEZ, günlük fiyat
/// gösterilir (T1). <see cref="GunlukFiyat"/> tarih seçiliyse o gün sayısının kademesine düşen günlük karşılıktır.
/// Domain alanını birebir yansıtan adlar (<c>Baslik</c>, <c>GunlukFiyat</c>, <c>KdvDahil</c>, <c>AylikToplam</c>)
/// Türkçe kalır; görünüm modeli adları İngilizce.
/// </summary>
public sealed record ListingCard(
    string Slug, string Baslik, Guid? CoverPhotoId, string? YearRange,
    decimal GunlukFiyat, bool KdvDahil, IReadOnlyList<FeatureItem> Features, int VehicleCount,
    int? Days, decimal? Total, string QuoteUrl,
    /// <summary>Köşe etiketi (ör. "Baktığınız araç"); null → basılmaz.</summary>
    string? Badge = null,
    /// <summary>İlk ekrandaki ilk kart: görsel tembel değil, yüksek öncelik (LCP).</summary>
    bool Priority = false)
{
    public string DetailUrl => $"/araclar/{Slug}";

    /// <summary>"WhatsApp'tan sor" (araç + seçilen tarihlerle hazır mesaj, <see cref="WhatsAppAsk"/>); null → basılmaz.</summary>
    public string? WhatsAppLink { get; init; }

    /// <summary>Görünür özelliklerin TAMAMI (<see cref="Features"/> kart çipleridir: başlıkta geçen değer atılmış).
    /// Süzgeç (<see cref="FleetFilter"/>) ve "Manuel, Dizel, 5 kişilik" gibi teknik satır buna bakar.</summary>
    public IReadOnlyList<FeatureItem> AllFeatures { get; init; } = [];

    /// <summary>İlanın 30 günlük toplamı (girilmişse; ana sayfa ve tarihsiz liste). Tarihli aramada null — o zaman
    /// <see cref="Total"/> seçilen günlerin toplamıdır.</summary>
    public decimal? AylikToplam { get; init; }

    public static ListingCard From(FleetShowcaseCard c, string quoteUrl, string? badge = null, bool priority = false)
        => new(c.Slug, c.Baslik, c.CoverPhotoId, c.YearRange, c.GunlukFiyat, c.KdvDahil, c.Features, c.VehicleCount,
            null, null, quoteUrl, badge, priority) { AllFeatures = c.AllFeatures ?? c.Features, AylikToplam = c.AylikToplam };

    public static ListingCard From(PublicAvailabilityResult r, string quoteUrl, string? badge = null, bool priority = false)
        => new(r.Slug, r.Baslik, r.CoverPhotoId, r.YearRange, r.GunlukFiyat, r.KdvDahil, r.Features, r.VehicleCount,
            r.Days, r.Total, quoteUrl, badge, priority) { AllFeatures = r.AllFeatures ?? r.Features };
}

/// <summary>Fleet slotunun basıldığı yer. Veri her yerde aynı biçimdedir; tema yerleşimi buna göre seçebilir
/// (ör. ana sayfada yatay şerit, müsaitlikte ızgara, benzer araçlarda kısa liste).</summary>
public enum FleetPlacement
{
    /// <summary>Ana sayfanın filo bölümü (tarihsiz, başlıklı).</summary>
    Home,
    /// <summary>/musaitlik sonucu — tarihli (Dates dolu) ya da tarihsiz filo gezintisi.</summary>
    Availability,
    /// <summary>İlan detayındaki "Filodaki diğer araçlar" (başlık sayfada).</summary>
    Similar,
    /// <summary>404 sayfasındaki kısa filo listesi (başlık sayfada).</summary>
    NotFound,
}

/// <summary>Sonuç özeti ("3 araç müsait" + açıklama). <paramref name="Live"/> → <c>role="status"</c> (arama
/// sonucu ekran okuyucuya duyurulur).</summary>
public sealed record FleetSummary(string Headline, string Description, bool Live = false);

/// <summary>Koleksiyon boşken gösterilen durum: başlık, bir cümle, çıkış bağlantıları. Metin sayfadan gelir ve
/// vaat içermez (T1). <paramref name="Live"/> → <c>role="status"</c>.</summary>
public sealed record FleetEmptyState(string Title, string Text, IReadOnlyList<SiteLink> Links, bool Live = false);

/// <summary>
/// Fleet slotunun modeli — ilan koleksiyonunun TEMADAN BAĞIMSIZ verisi. Sayfa kurar (kart verisi, teklif
/// bağlantısı, işaret, öncelik, özet metni); tema yalnız sunar: ızgara, liste, şerit.
/// Kartlar boşsa ve <see cref="Empty"/> null ise slot HİÇBİR ŞEY basmaz (T2).
/// </summary>
/// <param name="Cards">Basılacak ilanlar (sıra sayfanınki; ilk ekrandaki ilk kart <c>Priority</c>).</param>
/// <param name="Placement">Basıldığı yer.</param>
public sealed record FleetModel(IReadOnlyList<ListingCard> Cards, FleetPlacement Placement)
{
    /// <summary>Seçilen aralık (tüm kartlar için aynı); null → tarihsiz gezinti, "müsait" DENMEZ (T1).</summary>
    public StayDates? Dates { get; init; }
    /// <summary>Bölüm başlığı (h2); null → başlığı sayfa ya da çağıran slot basar.</summary>
    public string? Title { get; init; }
    /// <summary>Başlığın id'si — çağıran bölüm <c>aria-labelledby</c> ile buna bağlanır.</summary>
    public string? TitleId { get; init; }
    /// <summary>Kartların üstündeki sonuç özeti; yalnız kart varken basılır.</summary>
    public FleetSummary? Summary { get; init; }
    /// <summary>Özetin altındaki bilgi notu (ör. "X bu tarihlerde müsait değil"); yalnız kart varken basılır.</summary>
    public string? Notice { get; init; }
    /// <summary>Kart yokken gösterilecek durum; null → hiçbir şey basılmaz.</summary>
    public FleetEmptyState? Empty { get; init; }
    /// <summary>Firmanın "kiralamadan önce" beyanları (<see cref="FleetBranding.Facts"/>: depozito, km, yaş …) — tema
    /// isterse kartlarda/özette basar; null ya da alan boşsa basılmaz (T2). Ana sayfa ve müsaitlik doldurur.</summary>
    public SiteFacts? Facts { get; init; }
    /// <summary>Müsaitlik süzgeci (sınıf/vites/yakıt/koltuk; JS'siz GET formu). Yalnız /musaitlik doldurur; tema
    /// isterse formu basar (Kıyas). Süzme SAYFADA yapılır — <see cref="Cards"/> zaten süzülmüştür.</summary>
    public FleetFilterModel? Filter { get; init; }
}

/// <summary>
/// Home slotunun modeli — ana sayfa GÖVDESİNİN temadan bağımsız verisi. Sayfa (Pages/Home.razor) toplar; tema
/// bölümleri seçer ve sıralar. SEO (başlık, meta, JSON-LD) sayfada kalır, temaya taşınmaz.
/// </summary>
/// <param name="Branding">Firma bilgisi (ad, iletişim, adres, logo sürümü); null → nötr.</param>
/// <param name="Cards">Yayındaki ilanlar, ham (Hero slotunun girdisi).</param>
/// <param name="Fleet">Filo bölümünün hazır koleksiyonu (Fleet slotuna aynen verilir; başlık "Filomuz").</param>
/// <param name="Faq">Yayındaki SSS'nin TAMAMI (FaqSection "Tüm sorular (N)" bağlantısı için).</param>
/// <param name="FaqCount">Ana sayfada GÖRÜNEN soru adedi. FAQPage JSON-LD tam bu kadar soru yayınlar (SEO L-4):
/// SSS basan tema bu sayıyı değiştirmez.</param>
/// <param name="BranchNames">Aktif şube adları (arama formunun seçim kutusu).</param>
/// <param name="Branches">Aktif şubeler (adres, il/ilçe, konum, çalışma saati — ofis/harita bloğu için).</param>
/// <param name="Pages">Firmanın yayındaki içerik sayfaları (yasal metinler hariç; servis sırasıyla).</param>
/// <param name="Area">Hizmet bölgesi ("Esenyurt, İstanbul"); null → bölge cümlesi basılmaz (T2).</param>
/// <param name="TermsPage">Kiralama koşulları sayfasının adresi; null → bağlantı basılmaz (T2).</param>
public sealed record HomeModel(
    FleetBranding? Branding,
    IReadOnlyList<FleetShowcaseCard> Cards,
    FleetModel Fleet,
    IReadOnlyList<SssSatiri> Faq,
    int FaqCount,
    IReadOnlyList<string> BranchNames,
    IReadOnlyList<Branch> Branches,
    IReadOnlyList<SiteLink> Pages,
    string? Area,
    string? TermsPage)
{
    /// <summary>Marka adı (Hero slotunun <c>BrandName</c> parametresi); boşsa tema kendi nötr metnini kullanır.</summary>
    public string? BrandName => Branding?.BrandName;
}

/// <summary>İlan tarifesinin bir satırı: "Haftalık, 7 gün" — toplam ve gün başı karşılığı (yuvarlanmış; günlük
/// satırda null). Kademe metni ve yuvarlama TEK yerde (sayfa); tema yalnız basar.</summary>
public sealed record DetailRate(string Label, decimal Amount, decimal? PerDay);

/// <summary>
/// DetailHero slotunun modeli — ilan detayının üst bölümü (başlık, kapak, galeri, fiyat, tarife, eylemler,
/// tarih formu). Sayfa (Pages/ListingDetail.razor) kurar. Teknik özellikler, benzer araçlar, breadcrumb, JSON-LD
/// ve meta sayfada kalır.
/// </summary>
/// <param name="Listing">İlanın tüm verisi (başlık, fotoğraflar, fiyatlar, KDV işareti, adet).</param>
/// <param name="Rates">Tarife satırları (günlük + varsa haftalık/aylık).</param>
/// <param name="QuoteUrl">Talep formu adresi.</param>
/// <param name="BranchNames">Aktif şube adları (tarih formunun seçim kutusu).</param>
public sealed record DetailHeroModel(
    FleetShowcaseDetail Listing,
    IReadOnlyList<DetailRate> Rates,
    string QuoteUrl,
    IReadOnlyList<string> BranchNames)
{
    /// <summary>"2023 model, filoda 3 araç"; null → satır basılmaz.</summary>
    public string? MetaLine { get; init; }
    /// <summary>İlana özel WhatsApp bağlantısı; null → düğme basılmaz (T2).</summary>
    public string? WhatsAppLink { get; init; }
    /// <summary>Firma bilgisi; null → nötr.</summary>
    public FleetBranding? Branding { get; init; }

    /// <summary>"KDV dahil" / "KDV hariç" — ilanın kendi işaretinden.</summary>
    public string VatText => Listing.KdvDahil ? "KDV dahil" : "KDV hariç";
}

/// <summary>Firma iletişim bağlantıları — üst bar, alt bilgi ve alt eylem çubuğu AYNI kuraldan kurar.
/// Ofis telefonu yoksa mobil; WhatsApp numarası yoksa mobil (firmaların çoğu aynı numarayı kullanıyor).</summary>
public sealed record ContactLinks(string? Phone, string? TelLink, string? WaLink, string? Email)
{
    public static ContactLinks From(FleetBranding? b, string? brandName = null)
    {
        if (b is null) return new(null, null, null, null);
        var phone = string.IsNullOrWhiteSpace(b.Phone) ? b.MobilePhone : b.Phone;
        var name = brandName ?? b.BrandName;
        return new(phone, PhoneLink.Tel(phone),
            PhoneLink.Wa(string.IsNullOrWhiteSpace(b.WhatsApp) ? b.MobilePhone : b.WhatsApp,
                $"Merhaba, {name} araç kiralama hakkında bilgi almak istiyorum."),
            string.IsNullOrWhiteSpace(b.Email) ? null : b.Email);
    }
}
