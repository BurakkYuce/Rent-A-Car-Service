/**
 * İzinlerden BİRİ yeter (canMatch) — çekirdeğe taşındı (`@core/oturum/session-guard`); red davranışı (bant + Panel)
 * `permissionGuard` ile tek yerde. Özellik rotaları eski içe aktarma yolunu kullanmaya devam edebilir.
 */
export { anyPermissionGuard } from '@core/oturum/session-guard';

/** Araç karnesi (Blazor `/raporlar/arac-karne/{id}`) finans rollerine açık — bağlantı da rol kapılı. */
export const SCORECARD_ROLES: readonly string[] = ['Admin', 'Yonetici', 'Muhasebe'];
