using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// WhatsApp günlük özet gönderim log'u + idempotency (tenant-owned + RLS). Unique (TenantId, Gun, Tur) → günde tek
/// gönderim. Başarısız (Basarili=false) satır ertesi denemede TEKRAR denenir (sessiz-düşme yok); HataMesaji
/// görünürlük içindir ("neden özet gelmiyor" cevaplanabilir). Mali belge DEĞİL → immutability trigger yok (update
/// serbest: başarısız→başarılı geçiş).
/// </summary>
public class WhatsAppGonderim : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>İstanbul takvim günü — idempotency anahtarı (DateOnly → postgres date, TZ-agnostik).</summary>
    public DateOnly Gun { get; set; }

    /// <summary>Gönderim türü ("OpOzet" = günlük operasyon özeti).</summary>
    public string Tur { get; set; } = string.Empty;

    public string Alici { get; set; } = string.Empty;
    public string? Ozet { get; set; }
    public bool Basarili { get; set; }
    public string? HataMesaji { get; set; }
    public DateTimeOffset OlusturmaTarihi { get; set; } = DateTimeOffset.UtcNow;
}
