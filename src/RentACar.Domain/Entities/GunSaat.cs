namespace RentACar.Domain.Entities;

/// <summary>
/// Bir günün açılış/kapanış saati (FAZ-22; <see cref="Location.HaftalikCalismaSaatleri"/> JSONB
/// içeriği). <c>Kapali=true</c> ise saatler yok sayılır — "00:00-00:00" ile "kapalı"yı ayırt
/// edebilmek için ayrı bayrak var (00:00-00:00 yazımı 24 saat açık ile karışırdı).
/// </summary>
public sealed class GunSaat
{
    /// <summary>1=Pazartesi … 7=Pazar (ISO). Gün ADI saklanmaz — dil değişince veri bozulmasın.</summary>
    public int Gun { get; set; }
    public string? Acilis { get; set; }
    public string? Kapanis { get; set; }
    public bool Kapali { get; set; }
}
