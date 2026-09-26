import { inject } from '@angular/core';
import { type CanMatchFn, Router } from '@angular/router';

import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import type { Permission } from '@core/oturum/oturum-tipleri';

/**
 * İzinlerden BİRİ yeter (canMatch). Çekirdek `izinGuard` HEPSİNİ ister; araç okuma uçları ise
 * `RequireAnyPermission(OperationsWrite, ViewReports)` — Muhasebe (yalnız ViewReports) de listeyi görür.
 * Oturum kabuk rotasının `oturumGuard`'ında zaten doğrulandı; burada yalnız izin.
 */
export function anyPermissionGuard(...permissions: readonly Permission[]): CanMatchFn {
  return async () => {
    const session = inject(SessionService);
    const router = inject(Router);
    const banner = inject(WarningBannerService);
    const t = translationFunction();
    await session.initialLoad();
    if (permissions.some((p) => session.izinVar(p))) return true;
    banner.show({ tur: 'uyari', mesaj: t('oturum.yetkisizSayfa'), kod: 'yetki_yok' });
    return router.createUrlTree(['/']);
  };
}

/** Araç karnesi (Blazor `/raporlar/arac-karne/{id}`) finans rollerine açık — bağlantı da rol kapılı. */
export const SCORECARD_ROLES: readonly string[] = ['Admin', 'Yonetici', 'Muhasebe'];
