using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Firmanın KENDİ yüklediği PDF belgeleri ("Dokümanlar"): sahada çıktı alınması gereken boş
/// sözleşme nüshası, teslim formu, ruhsat fotokopisi, vekaletname örneği…
///
/// <para><b><see cref="PlatformBelge"/> ile KARIŞTIRILMAMALI — yön TERSTİR.</b> PlatformBelge
/// SaaS operatörünün tenant'lara DAĞITTIĞI dosyadır (firma yalnız indirir, yükleyemez) ve bir
/// PLATFORM tablosudur (RLS yok, izolasyon uygulama katmanında). Bu tablo tam tersi: firmanın
/// kendi yüklediği, kendi indirdiği dosya → <see cref="ITenantOwned"/> + Postgres RLS. İzolasyon
/// veritabanındadır, uygulama koduna emanet edilmez.</para>
///
/// <para><b>Mali belge DEĞİL</b> → değişmezlik (rc_prevent_mutation) trigger'ı YOKTUR, tam CRUD
/// serbesttir; firma yanlış yüklediği dosyayı silip yenisini koyabilmelidir.</para>
///
/// <para><b>Sıra = SLOT.</b> <see cref="Sira"/> yalnız görüntü sırası değil, 1..10 arası bir
/// yuvadır: tenant başına benzersiz + CHECK ile 1..10'a kapalı. Böylece "en fazla 10 belge"
/// sınırı yalnızca serviste sayı sayarak değil, <b>yapısal olarak</b> (unique index + CHECK)
/// da dayatılır — iki eşzamanlı yükleme sayımı atlatıp 11. satırı yazamaz.</para>
/// </summary>
public class FirmaDokuman : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Listede görünen ad (ör. "Boş Kira Sözleşmesi").</summary>
    public string Baslik { get; set; } = string.Empty;

    /// <summary>Ne işe yaradığı / ne zaman kullanılacağı (opsiyonel).</summary>
    public string? Aciklama { get; set; }

    /// <summary>İndirmede kullanılacak dosya adı. ASCII'ye slug'lanır — Türkçe karakter
    /// `Content-Disposition` başlığında RFC 5987 encode gerektirir, gereksiz karmaşa
    /// (<c>PdfValidation.GuvenliDosyaAdi</c>).</summary>
    public string DosyaAdi { get; set; } = string.Empty;

    /// <summary>PDF içeriği. <b>Liste sorgularına ASLA girmez</b> — 10 belgeyi listelerken 10 PDF'i
    /// (en kötü 100 MB) belleğe almak demek olurdu; yalnız indirme ucu okur.</summary>
    public byte[] Bytes { get; set; } = [];

    /// <summary>Bayt cinsinden boyut. Listede göstermek için ayrı kolon: aksi halde boyutu öğrenmenin
    /// tek yolu bytea'yı SELECT etmek olurdu.</summary>
    public long Boyut { get; set; }

    /// <summary>MIME türü. İstemcinin gönderdiği değer DEĞİL — yalnız PDF kabul edildiği için
    /// sunucu sabit yazar (magic-byte doğrulaması geçmişse "application/pdf").</summary>
    public string ContentType { get; set; } = "application/pdf";

    /// <summary>1..10 arası yuva numarası (bkz. sınıf açıklaması); liste sırası da budur.</summary>
    public int Sira { get; set; }

    /// <summary>Yükleyen kullanıcının adı (denetim kolaylığı; PII değil, oturum adı).</summary>
    public string? YukleyenKullanici { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
