using RentACar.Application.Common;
using RentACar.Application.Fleet;

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

    public static ListingCard From(FleetShowcaseCard c, string quoteLink, string? mark = null, bool priority = false)
        => new(c.Slug, c.Baslik, c.CoverPhotoId, c.YilAralik, c.GunlukFiyat, c.KdvDahil, c.Ozellikler, c.Adet,
            null, null, quoteLink, mark, priority);

    public static ListingCard From(PublicAvailabilityResult r, string quoteLink, string? mark = null, bool priority = false)
        => new(r.Slug, r.Baslik, r.CoverPhotoId, r.YilAralik, r.GunlukFiyat, r.KdvDahil, r.Ozellikler, r.Adet,
            r.Gun, r.Toplam, quoteLink, mark, priority);
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
