using RentACar.Domain.Common;

namespace RentACar.Domain.Entities;

/// <summary>
/// Gider türü/kategori tanımı (master sözlük): giderlerin kategorilerinin adlandırılmış listesi
/// (ör. "Yakıt", "Bakım", "Sigorta", "Kira", "Personel"). Tenant-owned + auditable. Gider formundaki
/// Kategori açılır listesini besler (additive — Expense kategori alanı serbest metin kalır).
/// NOT: <see cref="RentACar.Domain.Enums.ExpenseType"/> enum'ı ile çakışmasın diye sınıf adı Category.
/// </summary>
public class ExpenseCategory : ITenantOwned, IAuditable
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
