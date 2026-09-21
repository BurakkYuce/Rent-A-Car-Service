import { ChangeDetectionStrategy, Component, DestroyRef, inject } from '@angular/core';
import {
  NavigationCancel,
  NavigationCancellationCode,
  NavigationEnd,
  NavigationError,
  NavigationSkipped,
  NavigationStart,
  Router,
  RouterOutlet,
} from '@angular/router';

import { SorguMesajlari } from '@core/geri-bildirim/sorgu-mesajlari';
import { UyariBandiServisi } from '@core/geri-bildirim/uyari-bandi-servisi';
import { SurumServisi } from '@core/surum/surum-servisi';
import { ToastAlani } from '@shared/toast/toast-alani';
import { UyariBandi } from '@shared/uyari-bandi/uyari-bandi';

/**
 * Uygulama kökü: uyarı bandı + sayfa + toast yığını. Gezinme olaylarını banda bildirir, `?bilgi=`/`?hata=`
 * mesajlarını işler ve yeni sürüm denetimini başlatır. Kabuk (menü, üst çubuk) F3.2'de buraya eklenir.
 */
@Component({
  selector: 'rc-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, ToastAlani, UyariBandi],
  template: `
    <rc-uyari-bandi />
    <router-outlet />
    <rc-toast-alani />
  `,
})
export class App {
  constructor() {
    const bant = inject(UyariBandiServisi);
    const abonelik = inject(Router).events.subscribe((olay) => {
      if (olay instanceof NavigationStart) bant.gezinmeBasladi();
      else if (olay instanceof NavigationEnd) bant.gezinmeBitti('tamamlandi');
      else if (olay instanceof NavigationCancel) {
        bant.gezinmeBitti(
          olay.code === NavigationCancellationCode.Redirect ? 'yonlendirme' : 'iptal',
        );
      } else if (olay instanceof NavigationError || olay instanceof NavigationSkipped) {
        bant.gezinmeBitti('iptal');
      }
    });
    inject(DestroyRef).onDestroy(() => abonelik.unsubscribe());
    inject(SorguMesajlari).baslat();
    inject(SurumServisi).baslat();
  }
}
