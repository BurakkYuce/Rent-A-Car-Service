import { inject } from '@angular/core';
import type { CanActivateChildFn } from '@angular/router';

import { tabKey } from '@core/sekme/tab-key';

import { TabService } from './tab-service';

/**
 * Kabuğun `canActivateChild`'ı: yeni sekme açılacaksa yer var mı (en fazla 10). Doluysa en eski temiz
 * sekme kapatılır; hepsinde kaydedilmemiş değişiklik varsa gezinme durur (uyarı toast'u).
 */
export const tabLimitGuard: CanActivateChildFn = (child) => {
  const key = tabKey(child);
  return key === null || inject(TabService).makeRoom(key);
};
