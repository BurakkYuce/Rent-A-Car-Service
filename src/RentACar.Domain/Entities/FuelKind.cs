using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Yakıt türü tanımı (master sözlük): araç yakıt tiplerinin adlandırılmış listesi (ör. "Benzin",
/// "Dizel", "LPG", "Elektrik", "Hibrit"). Tenant-owned + auditable. Tanım/raporlama sözlüğü.
/// NOT: <see cref="RentACar.Domain.Enums.FuelType"/> enum'ı ile çakışmasın diye sınıf adı FuelKind.
/// </summary>
public class FuelKind : ITenantOwned, IAuditable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>Kısa kod (tenant içinde benzersiz; servis büyük harfe normalize eder).</summary>
    public string Kod { get; set; } = string.Empty;
    public string Ad { get; set; } = string.Empty;
    public bool Aktif { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
