import { inject } from '@angular/core';
import type { CanActivateChildFn } from '@angular/router';

import { sekmeAnahtari } from '@core/sekme/sekme-anahtari';

import { SekmeServisi } from './sekme-servisi';

/**
 * Kabuğun `canActivateChild`'ı: yeni sekme açılacaksa yer var mı (en fazla 10). Doluysa en eski temiz
 * sekme kapatılır; hepsinde kaydedilmemiş değişiklik varsa gezinme durur (uyarı toast'u).
 */
export const sekmeSiniriGuard: CanActivateChildFn = (cocuk) => {
  const anahtar = sekmeAnahtari(cocuk);
  return anahtar === null || inject(SekmeServisi).yerAc(anahtar);
};
