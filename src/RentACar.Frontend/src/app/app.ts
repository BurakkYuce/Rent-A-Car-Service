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

import { QueryMessages } from '@core/geri-bildirim/query-messages';
import { WarningBannerService } from '@core/geri-bildirim/warning-banner-service';
import { VersionService } from '@core/surum/version-service';
import { ToastAlani } from '@shared/toast/toast-alani';
import { WarningBanner } from '@shared/uyari-bandi/warning-banner';

/**
 * Uygulama kökü: uyarı bandı + sayfa + toast yığını. Gezinme olaylarını banda bildirir, `?bilgi=`/`?hata=`
 * mesajlarını işler ve yeni sürüm denetimini başlatır. Kabuk (menü, üst çubuk, sekmeler) bir rota
 * bileşenidir (`kabuk/kabuk.routes.ts`, tembel): giriş sayfası onun dışında çizilir.
 */
@Component({
  selector: 'rc-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, ToastAlani, WarningBanner],
  template: `
    <rc-uyari-bandi />
    <router-outlet />
    <rc-toast-alani />
  `,
})
export class App {
  constructor() {
    const banner = inject(WarningBannerService);
    const subscription = inject(Router).events.subscribe((evt) => {
      if (evt instanceof NavigationStart) banner.navigationStarted();
      else if (evt instanceof NavigationEnd) banner.navigationEnded('tamamlandi');
      else if (evt instanceof NavigationCancel) {
        banner.navigationEnded(
          evt.code === NavigationCancellationCode.Redirect ? 'yonlendirme' : 'iptal',
        );
      } else if (evt instanceof NavigationError || evt instanceof NavigationSkipped) {
        banner.navigationEnded('iptal');
      }
    });
    inject(DestroyRef).onDestroy(() => subscription.unsubscribe());
    inject(QueryMessages).start();
    inject(VersionService).start();
  }
}
