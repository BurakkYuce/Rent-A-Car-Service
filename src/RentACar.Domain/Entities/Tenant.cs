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

    /// <summary>KAPALI durumu damgası. Set ise firma kapalıdır (CloseAsync IsActive=false ile BİRLİKTE set eder
    /// → tek yaptırım yolu değişmez); yalnız ReopenAsync temizler. Pasif (IsActive=false, Kapanis=null)
    /// geçici askıya almadan AYRIDIR. Veri hiçbir durumda silinmez.</summary>
    public DateTimeOffset? KapanisTarihiUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
