using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Müşteri memnuniyet anketi (roadmap C3; CRM). Tenant-owned kayıt; opsiyonel cari ilişkisi. Puan 0-10.
/// </summary>
public class Anket : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid? CariId { get; set; }
    public int Puan { get; set; }
    public string? Yorum { get; set; }
    public DateTimeOffset Tarih { get; set; } = DateTimeOffset.UtcNow;
    public string? Kaynak { get; set; }

    // ---- FAZ-42: sözleşmeye bağlı çıkış/dönüş anketi (additive) ----

    /// <summary>İlgili kira sözleşmesi (opsiyonel — sözleşmesiz genel geri bildirim de kalır).</summary>
    public Guid? RentalId { get; set; }

    /// <summary>Çıkış mı dönüş anketi mi. Null = eski (sözleşmesiz) genel anket.</summary>
    public RentACar.Domain.Enums.SurveyType? AnketTuru { get; set; }

    /// <summary>Yapıldı/Yapılmadı. Varsayılan Yapildi — eski kayıtlar tamamlanmış sayılır.</summary>
    public RentACar.Domain.Enums.SurveyStatus Durum { get; set; } = RentACar.Domain.Enums.SurveyStatus.Yapildi;

    /// <summary>Çıkış ofisi — sözleşmeden SNAPSHOT (ofis adı sonradan değişse de anket sabit kalsın).</summary>
    public string? CikisOfisi { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
