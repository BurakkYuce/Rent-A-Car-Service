import { inject } from '@angular/core';
import { CanMatchFn, Router, UrlTree } from '@angular/router';

import { TAM_SAYFA_GEZINMESI } from '@core/form/kaydedilmemis-degisiklik';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { ceviriFonksiyonu } from '@core/i18n/ceviri';

import { girisSonrasiHedef, guvenliDonusAdresi } from './giris-hatasi';
import { OturumServisi } from './oturum-servisi';
import type { Izin } from './oturum-tipleri';

/** Girilmek istenen adres (router yolu, `/app` önekisiz): giriş sonrası dönüş için. */
function hedefAdres(router: Router): string {
  const gezinme = router.currentNavigation();
  return gezinme ? router.serializeUrl(gezinme.extractedUrl) : '/';
}

/**
 * `returnUrl` SİTE yolu olarak yazılır (`/app/…`): sunucunun `/login` → `/app/giris` yönlendirmesi Blazor
 * adreslerini aynı parametreyle taşır; önek, girişten sonra hangisinin yeni arayüz olduğunu ayırır (F4.6).
 */
function giriseYonlendir(router: Router): UrlTree {
  const donus = guvenliDonusAdresi(hedefAdres(router));
  return router.createUrlTree(
    ['/giris'],
    donus === '/' ? {} : { queryParams: { returnUrl: `/app${donus}` } },
  );
}

/**
 * Oturum şart (canMatch): `ben` yüklenene kadar bekler; oturum yoksa `/giris?returnUrl=…`.
 * Kabuk anonim yüklenir (roadmap: `/app` oturum isterse `/login` döngüsü), veri `/api/ui`'da korunur;
 * bu guard yalnız kullanıcıyı doğru sayfaya yönlendirir, güvenlik sınırı sunucudadır.
 */
export const oturumGuard: CanMatchFn = async () => {
  const oturum = inject(OturumServisi);
  const router = inject(Router);
  await oturum.ilkYukleme();
  return oturum.girisYapildi() || giriseYonlendir(router);
};

/**
 * İzin şart (canMatch): oturum yoksa girişe; izinlerden biri bile eksikse uyarı bandı + ana sayfa.
 * İzin adları sunucunun etkin izin listesiyle (`ben.izinler`) karşılaştırılır — rol değil izin.
 */
export function izinGuard(...izinler: readonly Izin[]): CanMatchFn {
  return async () => {
    const oturum = inject(OturumServisi);
    const router = inject(Router);
    const bant = inject(UyariBandiServisi);
    const t = ceviriFonksiyonu();
    await oturum.ilkYukleme();
    if (!oturum.girisYapildi()) return giriseYonlendir(router);
    if (izinler.every((izin) => oturum.izinVar(izin))) return true;
    bant.goster({ tur: 'uyari', mesaj: t('oturum.yetkisizSayfa'), kod: 'yetki_yok' });
    return router.createUrlTree(['/']);
  };
}

/**
 * Giriş sayfası (canMatch): zaten oturum varsa giriş sonrası hedefe (`girisSonrasiHedef`): pilotsa SPA
 * rotası, değilse ya da dönüş Blazor ekranıysa tam sayfa geçiş (sunucu `/login` kapısı hedefi çözer).
 */
export const misafirGuard: CanMatchFn = async () => {
  const oturum = inject(OturumServisi);
  const router = inject(Router);
  const gezin = inject(TAM_SAYFA_GEZINMESI);
  await oturum.ilkYukleme();
  if (!oturum.girisYapildi()) return true;
  const gezinme = router.currentNavigation();
  const donus = gezinme?.extractedUrl.queryParamMap.get('returnUrl');
  const hedef = girisSonrasiHedef(oturum.ben()?.pilot === true, donus);
  if (hedef.tur === 'spa') return router.parseUrl(hedef.yol);
  // Tam sayfa geçiş başladı; `false` dönülseydi router sonraki rotayı (kabuk `**`) dener ve sayfa
  // kapanana kadar boşuna kabuk + menü yüklerdi. Geçiş bitene dek giriş sayfası görünür kalır.
  gezin(hedef.adres);
  return true;
};
