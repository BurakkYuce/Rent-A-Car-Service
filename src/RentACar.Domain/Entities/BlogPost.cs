using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

public enum BlogPostDurum
{
    Taslak = 0,
    Yayinda = 1
}

/// <summary>
/// Halka açık site blog yazısı (PR-6). Kapak görseli <see cref="VehiclePhoto"/> ile AYNI bytea+thumbnail
/// desenini kullanır (paylaşılan ImageValidation/ImageProcessing), tek fark: çoklu galeri değil TEK kapak
/// (TenantSettings.LogoBytes gibi) — Sira/MoveAsync YOK.
///
/// İçerik DÜZ METİN saklanır (Markdown/HTML editör YOK): PublicSite'ta paragraflara bölünüp normal Razor
/// ifadesiyle basılır (`&lt;p&gt;@p&lt;/p&gt;`), MarkupString KULLANILMAZ → Razor otomatik HTML-encode eder,
/// XSS risk sınıfı tasarım gereği ortadan kalkar.
/// </summary>
public class BlogPost : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Baslik { get; set; } = string.Empty;

    /// <summary>URL parçası — `TurkishText.Normalize` ile başlıktan türetilir, (TenantId, Slug) benzersiz.
    /// Yazı İLK KEZ yayınlandıktan (<see cref="YayinTarihi"/> dolduktan) SONRA DONAR: aksi halde başlık
    /// düzenlemesi mevcut linkleri ve sitemap'i kırar.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Liste kartında gösterilen kısa özet (opsiyonel).</summary>
    public string? Ozet { get; set; }

    public string Icerik { get; set; } = string.Empty;

    public byte[]? KapakBytes { get; set; }

    /// <summary>Küçük resim (VehiclePhoto ile aynı üretim: uzun kenar 400px, JPEG q80, EXIF-döndürülmüş).</summary>
    public byte[]? KapakThumbBytes { get; set; }

    /// <summary>"image/png" | "image/jpeg" | "image/webp" — magic-byte'tan belirlenir.</summary>
    public string? KapakContentType { get; set; }

    // ---- SEO / yayıncılık alanları ----
    // Hepsi NULLABLE ve hepsi opsiyoneldir: doldurulmayan alan için sayfa mevcut davranışına düşer
    // (ör. SeoBaslik yoksa <title> Baslik'tan kurulur). Migration'da varsayılan VERİLMEZ — "girilmedi"
    // ile "boş bırakıldı" aynı şey ve ikisi de eski yazılarda doğru anlam.

    /// <summary>Başlığın altında görünen tamamlayıcı cümle (dek/subtitle). Sayfada h1'in altında
    /// basılır; SEO açıklaması DEĞİLDİR (o <see cref="MetaAciklama"/>).</summary>
    public string? AltBaslik { get; set; }

    /// <summary>
    /// Arama sonucunda görünecek başlık (<c>&lt;title&gt;</c> + og:title). Boşsa <see cref="Baslik"/>
    /// kullanılır.
    ///
    /// <para><b>Neden ayrı alan:</b> sayfadaki h1 okuyucuya yazılır ("Uzun dönem kiralamada nelere
    /// dikkat etmeli?"), arama başlığı ise anahtar kelime ve marka taşımalıdır ("Uzun Dönem Araç
    /// Kiralama Rehberi | Antalya"). İkisini tek alana sıkıştırmak birini bozar.</para>
    /// </summary>
    public string? SeoBaslik { get; set; }

    /// <summary>Arama sonucu açıklaması (meta description + og:description). Boşsa önce
    /// <see cref="Ozet"/>, o da yoksa gövdeden türetilir.</summary>
    public string? MetaAciklama { get; set; }

    /// <summary>
    /// Virgülle ayrılmış anahtar kelimeler. <c>meta name="keywords"</c> olarak basılır ve JSON-LD
    /// <c>keywords</c> alanına geçer.
    ///
    /// <para>Google bu etiketi sıralamada KULLANMIYOR (2009'dan beri); alan yine de var çünkü
    /// (a) JSON-LD <c>keywords</c> alanını AI arama yüzeyleri okuyor, (b) yazarın konu etiketlerini
    /// tek yerde tutması iç bağlantı/kümeleme için gerçek bir işe yarıyor. Sıralama vaadi YOKTUR.</para>
    /// </summary>
    public string? AnahtarKelimeler { get; set; }

    /// <summary>Yazar adı — JSON-LD <c>author</c> ve sayfada künye satırı. E-E-A-T sinyali.</summary>
    public string? Yazar { get; set; }

    /// <summary>Kapak görselinin alternatif metni. Boşsa başlık kullanılır (erişilebilirlik + görsel arama).</summary>
    public string? KapakAlt { get; set; }

    /// <summary>
    /// <c>true</c> → sayfaya <c>noindex</c> basılır VE sitemap'e girmez. Yayında ama aranmasın
    /// istenen yazılar için (kampanya sayfası, iç duyuru). Varsayılan <c>false</c>.
    /// </summary>
    public bool AramaDisi { get; set; }

    public BlogPostDurum Durum { get; set; }

    /// <summary>null = HİÇ yayınlanmadı (slug hâlâ değiştirilebilir). Dolduğu an slug donar; liste
    /// sıralama anahtarı. `default(DateTimeOffset)` sentinel'i yerine bilinçli nullable.</summary>
    public DateTimeOffset? YayinTarihi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
