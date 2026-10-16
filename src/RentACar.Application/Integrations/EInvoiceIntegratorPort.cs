using RentACar.Domain.Enums;

namespace RentACar.Application.Integrations;

// ───────────────────────── e-Belge entegratörü (özel entegratör) ─────────────────────────
//
// Türkiye'de e-Fatura / e-Arşiv belgeleri GİB'e doğrudan değil, firmanın sözleşme yaptığı ÖZEL ENTEGRATÖR
// üzerinden gider: belgeyi mali mühürle imzalayan, GİB'e ileten, alıcı sorgusunu yapan ve gelen kutusunu
// tutan odur. Bu port o entegratörün GERÇEK kullanımda gereken beş işini tanımlar.
//
// KİMLİK TENANT BAŞINADIR: her firma kendi entegratör kullanıcısıyla bağlanır; bu yüzden kimlik her çağrıda
// parametre olarak geçer (IEmailSender/SmtpAyar deseni) ve adaptör DURUM TUTMAZ (singleton kaydedilebilir).
//
// BU SÜRÜMDE GERÇEK ADAPTÖR YOK (bilinçli): hiçbir entegratörün API'si ve test kimliği elimizde değil.
// Uydurulmuş bir "örnek entegratör" sınıfı, gerçeğini bilmediğimiz bir protokolü taklit eder ve yanlış
// güven verir. Kimlik bilgisi gelince adaptör bu arayüzü uygulayıp DI'a IEInvoiceIntegrator olarak
// kaydedilir; ayar ekranı seçeneği ve gönderim işi kendiliğinden onu bulur (EInvoiceIntegratorCatalog).
// Testler arayüzü bir test çiftiyle (fake) sınar.

/// <summary>
/// Bir tenant'ın entegratör kimliği — sır DÜZ METİNDİR (çağıran <c>ISecretProtector</c> ile çözer). Kayıt,
/// log ya da hata mesajına YAZILMAZ.
/// </summary>
/// <param name="Kullanici">Entegratör kullanıcı adı.</param>
/// <param name="Sifre">Şifre ya da API anahtarı (entegratöre göre).</param>
/// <param name="GondericiEtiket">Gönderici birim etiketi (GB, <c>urn:mail:…</c>); boşsa entegratör varsayılanı.</param>
/// <param name="PostaKutusuEtiket">Posta kutusu etiketi (PK, <c>urn:mail:…</c>); boşsa entegratör varsayılanı.</param>
/// <param name="CanliOrtam">true = canlı, false = test ortamı.</param>
public sealed record EInvoiceCredentials(
    string Kullanici, string Sifre, string? GondericiEtiket, string? PostaKutusuEtiket, bool CanliOrtam)
{
    // Kayıt yanlışlıkla loglanırsa sır görünmesin (record'un varsayılan ToString'i tüm alanları yazar).
    public override string ToString() => $"EInvoiceCredentials {{ Kullanici = {Kullanici}, CanliOrtam = {CanliOrtam} }}";
}

/// <summary>Entegratöre teslim edilecek imzasız UBL-TR belgesi.</summary>
/// <param name="Ettn">Belgenin UUID'si (XML'deki <c>cbc:UUID</c> ile AYNI). Entegratör aynı ETTN'i ikinci kez kabul
/// etmemelidir; bizim tarafta da idempotency anahtarıdır.</param>
/// <param name="FaturaNo">16 haneli GİB fatura numarası.</param>
/// <param name="Tur">e-Fatura mı e-Arşiv mi.</param>
/// <param name="Profil">UBL-TR senaryosu (TEMELFATURA / TICARIFATURA / EARSIVFATURA).</param>
/// <param name="AliciVknTckn">Alıcı VKN/TCKN — zarf/yönlendirme için. KİŞİSEL VERİ: loglanmaz.</param>
/// <param name="AliciEtiket">Alıcının posta kutusu etiketi (e-Fatura'da; bilinmiyorsa null).</param>
/// <param name="Xml">UTF-8 UBL-TR 2.1 belge (imza yer tutucusuyla; imzayı entegratör atar).</param>
public sealed record EInvoiceSubmission(
    Guid Ettn, string FaturaNo, EDocumentKind Tur, string Profil, string AliciVknTckn, string? AliciEtiket, byte[] Xml)
{
    public override string ToString() => $"EInvoiceSubmission {{ Ettn = {Ettn}, FaturaNo = {FaturaNo}, Tur = {Tur}, Profil = {Profil} }}";
}

/// <summary>Gönderim sonucu sınıfı. Geçici hata yeniden denenir; kalıcı hata operatöre düşer.</summary>
public enum EInvoiceOutcome
{
    /// <summary>Entegratör belgeyi KABUL ETTİ.</summary>
    Basarili = 0,
    /// <summary>Ağ/zaman aşımı/servis dışı — aynı ETTN ile sonra yeniden denenebilir.</summary>
    GeciciHata = 1,
    /// <summary>Belge reddedildi (şema/iş kuralı, yetki) — veri düzeltilmeden yeniden denemek anlamsız.</summary>
    KaliciHata = 2
}

/// <param name="Hata">Operatöre gösterilebilecek kısa açıklama (kişisel veri İÇERMEMELİ; yine de kayda yazılmadan
/// önce maskelenir).</param>
public sealed record EInvoiceSendResult(EInvoiceOutcome Sonuc, string? EntegratorRef, string? Durum, string? Hata);

/// <param name="Sorgulandi">false = sorgu yapılamadı (geçici hata) — <paramref name="Bulundu"/> anlamsızdır.</param>
/// <param name="Bulundu">Entegratör bu ETTN'i tanıyor mu (daha önce kabul etti mi).</param>
public sealed record EInvoiceStatusResult(bool Sorgulandi, bool Bulundu, string? Durum, string? EntegratorRef, string? Hata);

public sealed record EInvoiceCancelResult(bool Basarili, string? Hata);

/// <param name="Sorgulandi">false = sorgu yapılamadı (geçici hata).</param>
/// <param name="EFaturaMukellefi">Alıcı GİB'de kayıtlı e-Fatura kullanıcısı mı (değilse e-Arşiv kesilir).</param>
/// <param name="Etiketler">Alıcının posta kutusu etiketleri (PK).</param>
public sealed record EInvoiceRecipientResult(bool Sorgulandi, bool EFaturaMukellefi, IReadOnlyList<string> Etiketler, string? Hata);

/// <summary>
/// Özel entegratör adaptörü. Uygulama birden çok adaptör barındırabilir; tenant hangisini kullanacağını
/// ayarlardan seçer (<c>TenantSettings.EFaturaEntegrator</c> = <see cref="Key"/>).
///
/// <para><b>Sözleşme (dürüst adaptör):</b> hiçbir yöntem gerçekleşmemiş bir işi başarılı DÖNMEZ; emin olunamayan
/// her durum <see cref="EInvoiceOutcome.GeciciHata"/> ya da <c>Sorgulandi=false</c>'tur. Yöntemler ağ/servis
/// hatasında istisna fırlatabilir — çağıran bunu geçici hata sayar ve mesajı maskeleyerek kaydeder.</para>
/// </summary>
public interface IEInvoiceIntegrator
{
    /// <summary>Kalıcı adaptör anahtarı (küçük harf, <c>^[a-z0-9-]{1,32}$</c>) — ayarlara yazılır, değiştirilmez.</summary>
    string Key { get; }

    /// <summary>Ayar ekranında görünen ad.</summary>
    string Ad { get; }

    /// <summary>Belgeyi imzalatıp GİB'e iletir (e-Fatura: alıcıya; e-Arşiv: raporlama).</summary>
    Task<EInvoiceSendResult> SendAsync(EInvoiceCredentials credentials, EInvoiceSubmission document, CancellationToken ct = default);

    /// <summary>ETTN ile belge durumunu sorar — çöküş sonrası "gitti mi?" sorusunun tek güvenilir cevabı.</summary>
    Task<EInvoiceStatusResult> QueryStatusAsync(EInvoiceCredentials credentials, Guid ettn, EDocumentKind kind, CancellationToken ct = default);

    /// <summary>Belgeyi iptal eder (yalnız e-Arşiv; e-Fatura iptali alıcı onayı/GİB portalı ister).</summary>
    Task<EInvoiceCancelResult> CancelAsync(EInvoiceCredentials credentials, Guid ettn, EDocumentKind kind, string reason, CancellationToken ct = default);

    /// <summary>Alıcı e-Fatura mükellefi mi (GİB kayıtlı kullanıcı listesi sorgusu).</summary>
    Task<EInvoiceRecipientResult> QueryRecipientAsync(EInvoiceCredentials credentials, string vknTckn, CancellationToken ct = default);

    /// <summary>Gelen kutusundaki faturaları [from, to] aralığında çeker.</summary>
    Task<IReadOnlyList<EInvoiceInboxItem>> FetchInboxAsync(
        EInvoiceCredentials credentials, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
