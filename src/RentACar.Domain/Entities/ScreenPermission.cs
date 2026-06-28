using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Ekran-bazlı yetki override (roadmap E3; review #8). Rol-izin matrisi (floor) ÜSTÜNE opt-in sıkılaştırma:
/// bir <see cref="EkranKodu"/> için override TANIMLIYSA yalnız <see cref="AllowedRolesCsv"/>'deki roller
/// erişir (deny-by-default), AMA matris izni (floor) yine şarttır (override floor'u GENİŞLETEMEZ). Override
/// YOKSA matris aynen geçerli (mevcut davranış). Tenant başına EkranKodu benzersiz.
/// </summary>
public class ScreenPermission : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public string EkranKodu { get; set; } = string.Empty;
    public string AllowedRolesCsv { get; set; } = string.Empty; // "Admin,Yonetici"
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
