using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// PR-16 — halka açık sitenin firma tarafından yazılan içerik sayfası ("Hakkımızda", "Nasıl
/// Çalışır", "Kiralama Koşulları", KVKK metni…). Her firma kendi metnini yazar; şablon yok.
///
/// <para><b>Gövde DÜZ METİNDİR</b>, HTML ya da Markdown değil. Boş satır paragraf ayırır ve
/// <c>IcerikMetni.Paragraflar</c> ile bölünüp Razor'un otomatik encode'una bırakılır — blog
/// içeriğiyle aynı kural (<c>MarkupString</c> KULLANILMAZ). Bu, XSS risk sınıfını tasarım gereği
/// ortadan kaldırıyor: firma personeli gövdeye <c>&lt;script&gt;</c> yazsa ekranda METİN çıkar.</para>
///
/// <para><b>Slug kök seviyede yaşar</b> (<c>/hakkimizda</c>), çünkü SEO'da ve kartvizitte daha iyi
/// duruyor. Bunun bedeli: rezerve adlar (<c>blog</c>, <c>musaitlik</c>, <c>araclar</c>…) yazma anında
/// REDDEDİLMEK zorunda — aksi halde firma kendi blogunu gölgeleyen bir sayfa açabilir
/// (<c>SiteIcerikService.RezerveSluglar</c>).</para>
/// </summary>
public class SayfaIcerik : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>URL parçası — başlıktan <c>TurkishText.Slugify</c> ile türetilir; (TenantId, Slug) benzersiz.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Baslik { get; set; } = string.Empty;

    /// <summary>Düz metin; boş satır = paragraf. HTML kabul EDİLMEZ (bkz. sınıf notu).</summary>
    public string Govde { get; set; } = string.Empty;

    /// <summary>Arama motoru özeti (&lt;meta description&gt;). Boşsa etiket basılmaz.</summary>
    public string? MetaAciklama { get; set; }

    /// <summary>Alt bilgideki (footer) sıralama; eşitlikte başlığa göre.</summary>
    public int Sira { get; set; }

    /// <summary>Yayından alınan sayfa sitede <b>404</b> döner (soft-404 değil — SEO sinyali doğru olmalı).</summary>
    public bool Yayinda { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}

/// <summary>
/// PR-16 — sık sorulan soru. Ayrı bir tablo (içerik sayfasının gövdesine gömmek yerine), çünkü
/// sitede <c>&lt;details&gt;</c> ile açılır-kapanır liste olarak ve ana sayfada "ilk N soru" özeti
/// olarak İKİ farklı yerde kullanılıyor; tek metin bloğu bunu veremez.
/// </summary>
public class SssKaydi : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Soru { get; set; } = string.Empty;

    /// <summary>Düz metin; <see cref="SayfaIcerik.Govde"/> ile aynı render kuralı.</summary>
    public string Cevap { get; set; } = string.Empty;

    public int Sira { get; set; }
    public bool Yayinda { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
