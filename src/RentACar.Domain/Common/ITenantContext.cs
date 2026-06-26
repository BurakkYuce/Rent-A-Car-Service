namespace RentACar.Domain.Common;

/// <summary>
/// Geçerli isteğin/oturumun kiracısını taşır. Web'de kimlik doğrulama claim'inden,
/// testlerde test double'ından beslenir. DbContext ve RLS connection interceptor'ı
/// bunu okur.
/// </summary>
public interface ITenantContext
{
    /// <summary>Çözümlenmiş tenant; aşama-1 login öncesi null olabilir.</summary>
    Guid? TenantId { get; }

    bool HasTenant => TenantId.HasValue && TenantId.Value != Guid.Empty;

    /// <summary>Yazma yollarında guard: tenant yoksa fırlatır.</summary>
    Guid TenantIdOrThrow() => HasTenant
        ? TenantId!.Value
        : throw new InvalidOperationException("Aktif tenant yok (ITenantContext çözümlenmedi).");
}
