namespace RentACar.Domain.Entities;

/// <summary>
/// Kiracı (firma). PLATFORM tablosu — kendisi tenant-owned DEĞİLDİR, RLS uygulanmaz.
/// Aşama-1 login'de <see cref="Code"/> ile çözümlenir (= tenant seçimi).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Login'de girilen firma kodu (benzersiz). Örn: "yucerent". DEĞİŞMEZ (login anahtarı).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // ---- Platform konsolu v2: iletişim/notlar/plan (bilgi amaçlı) ----
    public string? YetkiliAd { get; set; }
    public string? Eposta { get; set; }
    public string? Telefon { get; set; }
    /// <summary>Platform operatörü serbest notu (ödeme durumu, özel anlaşma…).</summary>
    public string? Notlar { get; set; }
    /// <summary>Abonelik plan etiketi (ör. Deneme/Standart/Pro) — şimdilik bilgi amaçlı, zorlama yok.</summary>
    public string? Plan { get; set; }

    /// <summary>
    /// PR-12: "Web Sitesi" modülü satın alındı mı. Bu alan PLATFORM kararıdır — yalnız <c>/platform</c>
    /// konsolundan yazılır; tenant kendi ERP'sinden GÖREMEZ/AÇAMAZ (bu yüzden tenant-owned
    /// <see cref="TenantSettings"/>'e değil buraya kondu; orası `ManageUsers` ile müşteriye açık).
    /// <c>TenantSettings.PublicSiteEnabled</c> ise tenant'ın KENDİ "Sitemi Aç" tercihidir — ikisi
    /// AYRI kademedir ve site ancak İKİSİ de açıkken görünür.
    ///
    /// BİLİNÇLİ BORÇ: tek bool. İkinci modülde <c>TenantModul(TenantId, ModulKod, Aktif)</c> platform
    /// tablosuna geçilmeli (kolon enflasyonu). <see cref="Plan"/>'ı zorlayıcı yapmak seçenek DEĞİL —
    /// serbest metin ("Deneme/Standart/Pro") ve mevcut veriyi kırar.
    /// </summary>
    public bool WebSitesiModulu { get; set; }

    /// <summary>KAPALI durumu damgası. Set ise firma kapalıdır (CloseAsync IsActive=false ile BİRLİKTE set eder
    /// → tek yaptırım yolu değişmez); yalnız ReopenAsync temizler. Pasif (IsActive=false, Kapanis=null)
    /// geçici askıya almadan AYRIDIR. Veri hiçbir durumda silinmez.</summary>
    public DateTimeOffset? KapanisTarihiUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
