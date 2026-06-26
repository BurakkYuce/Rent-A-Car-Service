namespace RentACar.Domain.Entities;

/// <summary>
/// Kiracı (firma). PLATFORM tablosu — kendisi tenant-owned DEĞİLDİR, RLS uygulanmaz.
/// Aşama-1 login'de <see cref="Code"/> ile çözümlenir (= tenant seçimi).
/// </summary>
public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Login'de girilen firma kodu (benzersiz). Örn: "yucerent".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
