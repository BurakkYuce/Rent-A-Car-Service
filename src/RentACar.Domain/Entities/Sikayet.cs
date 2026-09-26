using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteri şikayeti (roadmap C3; CRM). Tenant-owned kayıt; opsiyonel cari ilişkisi. Durum takipli (çözüm notu).
/// </summary>
public class Sikayet : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid? CariId { get; set; }
    public string Konu { get; set; } = string.Empty;
    public string? Detay { get; set; }
    public ComplaintStatus Durum { get; set; } = ComplaintStatus.Acik;
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? Cozum { get; set; }

    // ---- FAZ-43: teslim/dönüş sürecine bağlama ----
    /// <summary>Şikayetin bağlı olduğu kira sözleşmesi. Boş → sözleşmeden bağımsız genel şikayet
    /// (eski davranış korunur). Plaka/sözleşme no BU BAĞDAN çözülür — snapshot alan tutulmaz,
    /// yoksa araç değişince liste yalan söylerdi.</summary>
    public Guid? RentalId { get; set; }

    /// <summary>Aracı teslim ALAN personel (dönüşte). <c>RentalContract.TeslimAlanPersonelId</c> ile
    /// AYNI kavram ve AYNI isim — ama bu ŞİKAYETİN kendi alanıdır, sözleşmeden kopya değildir:
    /// form sözleşme seçilince ÖNERİ olarak doldurur, kullanıcı değiştirebilir.</summary>
    public Guid? TeslimAlanPersonelId { get; set; }
    /// <summary>Aracı teslim EDEN personel (çıkışta). Aynı kural.</summary>
    public Guid? TeslimEdenPersonelId { get; set; }

    /// <summary>Müşteri memnuniyet puanı (1-5). Boş → puanlanmamış.</summary>
    public int? Puan { get; set; }
    /// <summary>Şikayetin geldiği kanal (Telefon/Web/Yüz Yüze… serbest metin + öneri listesi).</summary>
    public string? SikayetKanali { get; set; }
    /// <summary>Şikayet hangi sürece ait. Boş → belirtilmemiş (eski kayıtlar).</summary>
    public ComplaintLocation? SikayetYeri { get; set; }
    /// <summary>Çıkış ofisi — sözleşmeden ANLIK GÖRÜNTÜ olarak kopyalanır. Sözleşme bağı olmayan
    /// şikayette elle girilebilir.</summary>
    public string? CikisOfisi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
