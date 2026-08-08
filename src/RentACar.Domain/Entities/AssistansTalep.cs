using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Assistans (yol yardım) talebi — FAZ-44; canlı <c>musterigelenmesajlar.aspx</c> karşılığı.
/// Kiradaki bir araçtan/müşteriden gelen yardım mesajı: yedek lastik gerekiyor mu, araç hareket
/// edebiliyor mu, mesaj ve sebep.
///
/// <para><b>SNAPSHOT ALANLARI (Plaka/AdSoyad/CepTel) BİLİNÇLİ KOPYADIR.</b> Şikayet dikeyinde
/// (FAZ-43) plaka sözleşmeden CANLI çözülüyor; burada TERSİ yapılıyor ve bu bilinçlidir:
/// assistans kaydı bir OLAY TUTANAĞIDIR — "o gece hangi araç, hangi numara arandı" sorusunun
/// cevabı sonradan araç değişse/müşteri telefonunu güncellese de DEĞİŞMEMELİDİR. Şikayet ise
/// sözleşmenin güncel hâline bakan bir değerlendirmedir. İki farklı soru, iki farklı doğru cevap.</para>
///
/// <para>Sözleşme bağı OPSİYONELDİR: çağrı merkezine sözleşmesi bulunamayan bir arama da düşebilir;
/// kaydın tutulamaması bilgi kaybı olurdu.</para>
///
/// <para>Para/defter TAŞIMAZ.</para>
/// </summary>
public class AssistansTalep : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>İlgili kira sözleşmesi (opsiyonel — sözleşmesiz çağrı da kaydedilebilir).</summary>
    public Guid? RentalId { get; set; }

    /// <summary>Plaka — kayıt anında normalize edilerek KOPYALANIR (olay tutanağı).</summary>
    public string? Plaka { get; set; }
    public string? AdSoyad { get; set; }
    public string? CepTel { get; set; }

    /// <summary>Talebin geldiği an.</summary>
    public DateTimeOffset Zaman { get; set; } = DateTimeOffset.UtcNow;

    public string Mesaj { get; set; } = string.Empty;
    public string? Sebep { get; set; }

    /// <summary>Yedek lastik gerekiyor mu.</summary>
    public bool YedekLastikMi { get; set; }
    /// <summary>Araç hareket edebiliyor mu (false = çekici gerekebilir).</summary>
    public bool AracHareketMi { get; set; }

    /// <summary>Talep kapatıldı mı (operasyonel takip; para taşımaz).</summary>
    public bool Kapandi { get; set; }
    public string? Cozum { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
