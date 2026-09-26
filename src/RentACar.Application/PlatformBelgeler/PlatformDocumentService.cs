using RentACar.Application.Common;
using RentACar.Domain.Common;
using RentACar.Domain.Enums;

namespace RentACar.Application.PlatformBelgeler;

/// <summary>
/// PR-B — tenant tarafı "Firma Belgeleri". <b>SALT-OKUR</b>: bu servisin yazma metodu YOKTUR;
/// belgeleri yalnız platform operatörü yönetir (<c>PlatformAdminService</c>).
///
/// <para><b>Yetki:</b> yeni bir <see cref="Permission"/> değeri EKLENMEDİ — belgeleri (KVKW metni,
/// kılavuz) oturum açmış TÜM personel görmeli. Kapı "oturum açık mı" + belge başına
/// <c>YalnizYoneticiler</c> bayrağı. Matris testlerini dalgalandırmamak için `Permission` sabit kaldı.</para>
///
/// <para><b>Rol adı tuzağı:</b> yönetici rolleri <see cref="UserRole.Admin"/> ve
/// <see cref="UserRole.Yonetici"/> — <c>Yonetici</c> ASCII'dir (`ö` YOK). "Yönetici" yazan bir
/// karşılaştırma sessizce HİÇ KİMSEYİ geçirmezdi.</para>
/// </summary>
public sealed class PlatformDocumentService(IPlatformDocumentRepository repository, ICurrentUser currentUser, ITenantContext tenant)
{
    /// <summary>Oturum açmış kullanıcının tenant'ı — yoksa hiçbir belge görünmez (güvenli varsayılan).</summary>
    private Guid TenantIdOrThrow => tenant.TenantId
        ?? throw new ValidationException("Tenant bağlamı yok — belgeler listelenemez.");

    private bool IsManager => currentUser.Role is UserRole.Admin or UserRole.Yonetici;

    public Task<IReadOnlyList<FirmaBelgeSatiri>> ListAsync(CancellationToken ct = default)
        => repository.ListAsync(TenantIdOrThrow, IsManager, ct);

    /// <summary>İçeriği getirir; belge bu tenant'a AÇIK DEĞİLSE <c>null</c> (uç 404 döner).
    /// "Yok" ile "yetkisiz" ayırt edilmez — belgenin varlığı da bilgidir.</summary>
    public Task<BelgeIcerik?> DownloadAsync(Guid documentId, CancellationToken ct = default)
        => repository.DownloadAsync(documentId, TenantIdOrThrow, IsManager, ct);
}
