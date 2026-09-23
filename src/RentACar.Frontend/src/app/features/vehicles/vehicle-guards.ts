import { inject } from '@angular/core';
import { type CanMatchFn, Router } from '@angular/router';

import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Izin } from '@core/oturum/oturum-tipleri';

/**
 * İzinlerden BİRİ yeter (canMatch). Çekirdek `izinGuard` HEPSİNİ ister; araç okuma uçları ise
 * `RequireAnyPermission(OperationsWrite, ViewReports)` — Muhasebe (yalnız ViewReports) de listeyi görür.
 * Oturum kabuk rotasının `oturumGuard`'ında zaten doğrulandı; burada yalnız izin.
 */
export function anyPermissionGuard(...permissions: readonly Izin[]): CanMatchFn {
  return async () => {
    const session = inject(OturumServisi);
    const router = inject(Router);
    const banner = inject(UyariBandiServisi);
    const t = ceviriFonksiyonu();
    await session.ilkYukleme();
    if (permissions.some((p) => session.izinVar(p))) return true;
    banner.goster({ tur: 'uyari', mesaj: t('oturum.yetkisizSayfa'), kod: 'yetki_yok' });
    return router.createUrlTree(['/']);
  };
}

/** Araç karnesi (Blazor `/raporlar/arac-karne/{id}`) finans rollerine açık — bağlantı da rol kapılı. */
export const SCORECARD_ROLES: readonly string[] = ['Admin', 'Yonetici', 'Muhasebe'];
