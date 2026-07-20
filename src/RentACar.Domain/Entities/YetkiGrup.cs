using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Yetki grubu / şablon (PR-D, "güvenli genişletme"): admin'in ekran-izni yapılandırmasını (ScreenPermission
/// override'ları) isimli bir PROFİL olarak saklar; tek tıkla yeniden UYGULANIR (bulk ScreenPermission set).
/// GÜVENLİK: uygulamak yalnız ekran-override'larını yazar — rol-izin matrisi (floor) DEĞİŞMEZ; PermissionResolver
/// hâlâ "floor şart, override yalnız daraltır" kuralını uygular (grant floor'u AŞAMAZ). Bu, tighten-only modeli
/// bozmadan çok-ekran yönetimini ölçekler. Tenant başına <see cref="Ad"/> benzersiz.
/// </summary>
public class YetkiGrup : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string Ad { get; set; } = string.Empty;

    /// <summary>Şablon kalemleri (JSON): [{"ekran":"kasa","roller":["Admin","Yonetici"]}] — kaydetme anındaki
    /// ScreenPermission anlık görüntüsü. Uygulanınca her kalem ScreenPermission override'ına yazılır.</summary>
    public string KalemlerJson { get; set; } = "[]";

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
