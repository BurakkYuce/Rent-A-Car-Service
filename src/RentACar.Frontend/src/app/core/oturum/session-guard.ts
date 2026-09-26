import { inject } from '@angular/core';
import { CanMatchFn, Router, UrlTree } from '@angular/router';

import { FULL_PAGE_NAVIGATION } from '@core/form/kaydedilmemis-degisiklik';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { translationFunction } from '@core/i18n/ceviri';

import { postLoginTarget, safeReturnUrl } from './giris-hatasi';
import { SessionService } from './session-service';
import type { Permission } from './oturum-tipleri';

/** Girilmek istenen adres (router yolu, `/app` önekisiz): giriş sonrası dönüş için. */
function targetUrl(router: Router): string {
  const navigation = router.currentNavigation();
  return navigation ? router.serializeUrl(navigation.extractedUrl) : '/';
}

/**
 * `returnUrl` SİTE yolu olarak yazılır (`/app/…`): sunucunun `/login` → `/app/giris` yönlendirmesi Blazor
 * adreslerini aynı parametreyle taşır; önek, girişten sonra hangisinin yeni arayüz olduğunu ayırır (F4.6).
 */
function redirectToLogin(router: Router): UrlTree {
  const returnInfo = safeReturnUrl(targetUrl(router));
  return router.createUrlTree(
    ['/giris'],
    returnInfo === '/' ? {} : { queryParams: { returnUrl: `/app${returnInfo}` } },
  );
}

/**
 * Oturum şart (canMatch): `ben` yüklenene kadar bekler; oturum yoksa `/giris?returnUrl=…`.
 * Kabuk anonim yüklenir (roadmap: `/app` oturum isterse `/login` döngüsü), veri `/api/ui`'da korunur;
 * bu guard yalnız kullanıcıyı doğru sayfaya yönlendirir, güvenlik sınırı sunucudadır.
 */
export const sessionGuard: CanMatchFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);
  await session.initialLoad();
  return session.loggedIn() || redirectToLogin(router);
};

/**
 * İzin şart (canMatch): oturum yoksa girişe; izinlerden biri bile eksikse uyarı bandı + ana sayfa.
 * İzin adları sunucunun etkin izin listesiyle (`ben.izinler`) karşılaştırılır — rol değil izin.
 */
export function permissionGuard(...permissions: readonly Permission[]): CanMatchFn {
  return async () => {
    const session = inject(SessionService);
    const router = inject(Router);
    const banner = inject(WarningBannerService);
    const t = translationFunction();
    await session.initialLoad();
    if (!session.loggedIn()) return redirectToLogin(router);
    if (permissions.every((permission) => session.izinVar(permission))) return true;
    banner.show({ tur: 'uyari', mesaj: t('oturum.yetkisizSayfa'), kod: 'yetki_yok' });
    return router.createUrlTree(['/']);
  };
}

/**
 * Giriş sayfası (canMatch): zaten oturum varsa giriş sonrası hedefe (`girisSonrasiHedef`): pilotsa SPA
 * rotası, değilse ya da dönüş Blazor ekranıysa tam sayfa geçiş (sunucu `/login` kapısı hedefi çözer).
 */
export const guestGuard: CanMatchFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);
  const navigate = inject(FULL_PAGE_NAVIGATION);
  await session.initialLoad();
  if (!session.loggedIn()) return true;
  const navigation = router.currentNavigation();
  const returnInfo = navigation?.extractedUrl.queryParamMap.get('returnUrl');
  const target = postLoginTarget(returnInfo);
  if (target.tur === 'spa') return router.parseUrl(target.yol);
  // Tam sayfa geçiş başladı; `false` dönülseydi router sonraki rotayı (kabuk `**`) dener ve sayfa
  // kapanana kadar boşuna kabuk + menü yüklerdi. Geçiş bitene dek giriş sayfası görünür kalır.
  navigate(target.adres);
  return true;
};
