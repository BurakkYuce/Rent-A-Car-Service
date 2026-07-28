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

    /// <summary>
    /// Varsayılan false: TenantId boşken RLS'in mevcut default-deny davranışı (GUC boş → 0 satır,
    /// bkz. TenantIsolationTests.Unset_tenant_sees_no_rows_default_deny) DEĞİŞMEZ. Yalnız istek-scoped
    /// bağlamlar (ör. PublicTenantContext) — middleware'in TenantId'yi HER ZAMAN dolu bırakması gerektiği,
    /// boş kalmasının yalnızca bir bug olabileceği yerlerde — true override eder: TenantConnectionInterceptor
    /// o zaman sessiz boş sonuç yerine gürültülü hata fırlatır.
    /// </summary>
    bool ThrowIfTenantMissing => false;
}
