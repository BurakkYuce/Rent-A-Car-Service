using RentACar.Application.Common;
using RentACar.Application.Fleet;
using RentACar.Application.SiteIcerik;
using RentACar.Domain.Entities;

namespace RentACar.PublicSite.Components.Themes;

/// <summary>Menü/alt bilgi bağlantısı. (Ad `NavLink` DEĞİL: Blazor'ın Routing.NavLink bileşeniyle çakışır.)</summary>
public sealed record SiteLink(string Href, string Label);

/// <summary>Seçilen kiralama aralığı (Card slotunun isteğe bağlı <c>Tarih</c> parametresi).</summary>
public sealed record StayDates(DateTimeOffset Bas, DateTimeOffset Bit, int Gun);

/// <summary>
/// Card slotunun <c>Kart</c> parametresi — ilan kartının TEMADAN BAĞIMSIZ verisi. Sayfa kurar, tema yalnız basar.
/// <see cref="Gun"/>/<see cref="Toplam"/> null ise ziyaretçi tarih seçmemiştir: "müsait" DENMEZ, günlük fiyat
/// gösterilir (T1). <see cref="GunlukFiyat"/> tarih seçiliyse o gün sayısının kademesine düşen günlük karşılıktır.
/// </summary>
public sealed record ListingCard(
    string Slug, string Baslik, Guid? CoverPhotoId, string? YilAralik,
    decimal GunlukFiyat, bool KdvDahil, IReadOnlyList<OzellikGoster> Ozellikler, int Adet,
    int? Gun, decimal? Toplam, string TeklifLinki,
    /// <summary>Köşe etiketi (ör. "Baktığınız araç"); null → basılmaz.</summary>
    string? Isaret = null,
    /// <summary>İlk ekrandaki ilk kart: görsel tembel değil, yüksek öncelik (LCP).</summary>
    bool Oncelikli = false)
{
    public string DetayLinki => $"/araclar/{Slug}";

    /// <summary>"WhatsApp'tan sor" (araç + seçilen tarihlerle hazır mesaj, <see cref="WhatsAppAsk"/>); null → basılmaz.</summary>
    public string? WaLinki { get; init; }

    public static ListingCard From(FleetShowcaseCard c, string quoteLink, string? mark = null, bool priority = false)
        => new(c.Slug, c.Baslik, c.CoverPhotoId, c.YilAralik, c.GunlukFiyat, c.KdvDahil, c.Ozellikler, c.Adet,
            null, null, quoteLink, mark, priority);

    public static ListingCard From(PublicAvailabilityResult r, string quoteLink, string? mark = null, bool priority = false)
        => new(r.Slug, r.Baslik, r.CoverPhotoId, r.YilAralik, r.GunlukFiyat, r.KdvDahil, r.Ozellikler, r.Adet,
            r.Gun, r.Toplam, quoteLink, mark, priority);
}

/// <summary>Fleet slotunun basıldığı yer. Veri her yerde aynı biçimdedir; tema yerleşimi buna göre seçebilir
/// (ör. ana sayfada yatay şerit, müsaitlikte ızgara, benzer araçlarda kısa liste).</summary>
public enum FleetPlacement
{
    /// <summary>Ana sayfanın filo bölümü (tarihsiz, başlıklı).</summary>
    Home,
    /// <summary>/musaitlik sonucu — tarihli (Tarih dolu) ya da tarihsiz filo gezintisi.</summary>
    Availability,
    /// <summary>İlan detayındaki "Filodaki diğer araçlar" (başlık sayfada).</summary>
    Similar,
    /// <summary>404 sayfasındaki kısa filo listesi (başlık sayfada).</summary>
    NotFound,
}

/// <summary>Sonuç özeti ("3 araç müsait" + açıklama). <paramref name="Canli"/> → <c>role="status"</c> (arama
/// sonucu ekran okuyucuya duyurulur).</summary>
public sealed record FleetSummary(string Vurgu, string Aciklama, bool Canli = false);

/// <summary>Koleksiyon boşken gösterilen durum: başlık, bir cümle, çıkış bağlantıları. Metin sayfadan gelir ve
/// vaat içermez (T1). <paramref name="Canli"/> → <c>role="status"</c>.</summary>
public sealed record FleetEmptyState(string Baslik, string Metin, IReadOnlyList<SiteLink> Linkler, bool Canli = false);

/// <summary>
/// Fleet slotunun modeli — ilan koleksiyonunun TEMADAN BAĞIMSIZ verisi. Sayfa kurar (kart verisi, teklif
/// bağlantısı, işaret, öncelik, özet metni); tema yalnız sunar: ızgara, liste, şerit.
/// Kartlar boşsa ve <see cref="Bos"/> null ise slot HİÇBİR ŞEY basmaz (T2).
/// </summary>
/// <param name="Kartlar">Basılacak ilanlar (sıra sayfanınki; ilk ekrandaki ilk kart <c>Oncelikli</c>).</param>
/// <param name="Yer">Basıldığı yer.</param>
public sealed record FleetModel(IReadOnlyList<ListingCard> Kartlar, FleetPlacement Yer)
{
    /// <summary>Seçilen aralık (tüm kartlar için aynı); null → tarihsiz gezinti, "müsait" DENMEZ (T1).</summary>
    public StayDates? Tarih { get; init; }
    /// <summary>Bölüm başlığı (h2); null → başlığı sayfa ya da çağıran slot basar.</summary>
    public string? Baslik { get; init; }
    /// <summary>Başlığın id'si — çağıran bölüm <c>aria-labelledby</c> ile buna bağlanır.</summary>
    public string? BaslikId { get; init; }
    /// <summary>Kartların üstündeki sonuç özeti; yalnız kart varken basılır.</summary>
    public FleetSummary? Ozet { get; init; }
    /// <summary>Özetin altındaki bilgi notu (ör. "X bu tarihlerde müsait değil"); yalnız kart varken basılır.</summary>
    public string? Uyari { get; init; }
    /// <summary>Kart yokken gösterilecek durum; null → hiçbir şey basılmaz.</summary>
    public FleetEmptyState? Bos { get; init; }
    /// <summary>Firmanın "kiralamadan önce" beyanları (<see cref="FleetBranding.Bilgi"/>: depozito, km, yaş …) — tema
    /// isterse kartlarda/özette basar; null ya da alan boşsa basılmaz (T2). Ana sayfa ve müsaitlik doldurur.</summary>
    public SiteFacts? Bilgi { get; init; }
}

/// <summary>
/// Home slotunun modeli — ana sayfa GÖVDESİNİN temadan bağımsız verisi. Sayfa (Pages/Home.razor) toplar; tema
/// bölümleri seçer ve sıralar. SEO (başlık, meta, JSON-LD) sayfada kalır, temaya taşınmaz.
/// </summary>
/// <param name="Branding">Firma bilgisi (ad, iletişim, adres, logo sürümü); null → nötr.</param>
/// <param name="Kartlar">Yayındaki ilanlar, ham (Hero slotunun girdisi; fiyat tahtası, sahne vb.).</param>
/// <param name="Filo">Filo bölümünün hazır koleksiyonu (Fleet slotuna aynen verilir; başlık "Filomuz").</param>
/// <param name="Sss">Yayındaki SSS'nin TAMAMI (BolumSss "Tüm sorular (N)" bağlantısı için).</param>
/// <param name="SssAdet">Ana sayfada GÖRÜNEN soru adedi. FAQPage JSON-LD tam bu kadar soru yayınlar (SEO L-4):
/// SSS basan tema bu sayıyı değiştirmez.</param>
/// <param name="SubeAdlari">Aktif şube adları (arama formunun seçim kutusu).</param>
/// <param name="Subeler">Aktif şubeler (adres, il/ilçe, konum, çalışma saati — ofis/harita bloğu için).</param>
/// <param name="Sayfalar">Firmanın yayındaki içerik sayfaları (yasal metinler hariç; servis sırasıyla).</param>
/// <param name="Sehir">Hizmet bölgesi ("Esenyurt, İstanbul"); null → bölge cümlesi basılmaz (T2).</param>
/// <param name="KosulSayfasi">Kiralama koşulları sayfasının adresi; null → bağlantı basılmaz (T2).</param>
public sealed record HomeModel(
    FleetBranding? Branding,
    IReadOnlyList<FleetShowcaseCard> Kartlar,
    FleetModel Filo,
    IReadOnlyList<SssSatiri> Sss,
    int SssAdet,
    IReadOnlyList<string> SubeAdlari,
    IReadOnlyList<Branch> Subeler,
    IReadOnlyList<SiteLink> Sayfalar,
    string? Sehir,
    string? KosulSayfasi)
{
    /// <summary>Marka adı (Hero slotunun <c>Marka</c> parametresi); boşsa tema kendi nötr metnini kullanır.</summary>
    public string? Marka => Branding?.Marka;
}

/// <summary>İlan tarifesinin bir satırı: "Haftalık, 7 gün" — toplam ve gün başı karşılığı (yuvarlanmış; günlük
/// satırda null). Kademe metni ve yuvarlama TEK yerde (sayfa); tema yalnız basar.</summary>
public sealed record DetailRate(string Etiket, decimal Tutar, decimal? GunBasi);

/// <summary>
/// DetailHero slotunun modeli — ilan detayının üst bölümü (başlık, kapak, galeri, fiyat, tarife, eylemler,
/// tarih formu). Sayfa (Pages/IlanDetay.razor) kurar. Teknik özellikler, benzer araçlar, breadcrumb, JSON-LD
/// ve meta sayfada kalır.
/// </summary>
/// <param name="Ilan">İlanın tüm verisi (başlık, fotoğraflar, fiyatlar, KDV işareti, adet).</param>
/// <param name="Tarife">Tarife satırları (günlük + varsa haftalık/aylık).</param>
/// <param name="TeklifLinki">"Teklif iste" adresi.</param>
/// <param name="SubeAdlari">Aktif şube adları (tarih formunun seçim kutusu).</param>
public sealed record DetailHeroModel(
    FleetShowcaseDetail Ilan,
    IReadOnlyList<DetailRate> Tarife,
    string TeklifLinki,
    IReadOnlyList<string> SubeAdlari)
{
    /// <summary>"2023 model, filoda 3 araç"; null → satır basılmaz.</summary>
    public string? MetaSatiri { get; init; }
    /// <summary>İlana özel WhatsApp bağlantısı; null → düğme basılmaz (T2).</summary>
    public string? WaLinki { get; init; }
    /// <summary>Firma bilgisi; null → nötr.</summary>
    public FleetBranding? Branding { get; init; }

    /// <summary>"KDV dahil" / "KDV hariç" — ilanın kendi işaretinden.</summary>
    public string KdvMetni => Ilan.KdvDahil ? "KDV dahil" : "KDV hariç";
}

/// <summary>Firma iletişim bağlantıları — üst bar, alt bilgi ve alt eylem çubuğu AYNI kuraldan kurar.
/// Ofis telefonu yoksa mobil; WhatsApp numarası yoksa mobil (firmaların çoğu aynı numarayı kullanıyor).</summary>
public sealed record ContactLinks(string? Phone, string? TelLink, string? WaLink, string? Email)
{
    public static ContactLinks From(FleetBranding? b, string? brandName = null)
    {
        if (b is null) return new(null, null, null, null);
        var phone = string.IsNullOrWhiteSpace(b.Tel) ? b.MobilTel : b.Tel;
        var name = brandName ?? b.Marka;
        return new(phone, PhoneLink.Tel(phone),
            PhoneLink.Wa(string.IsNullOrWhiteSpace(b.WhatsApp) ? b.MobilTel : b.WhatsApp,
                $"Merhaba, {name} araç kiralama hakkında bilgi almak istiyorum."),
            string.IsNullOrWhiteSpace(b.Email) ? null : b.Email);
    }
}
