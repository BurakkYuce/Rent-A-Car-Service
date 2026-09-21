import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { OturumServisi } from '@core/oturum/oturum-servisi';

/**
 * Yer tutucu ana sayfa (kabuk içinde; tema, çıkış ve menü F3.2'den beri kabukta). Oturumdaki kullanıcıyı
 * ve vitrin bağlantılarını gösterir; e2e ve axe kabuğu burada denetler.
 */
@Component({
  selector: 'rc-yer-tutucu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink],
  templateUrl: './yer-tutucu.html',
  styleUrl: './yer-tutucu.scss',
})
export class YerTutucu {
  protected readonly oturum = inject(OturumServisi);
}
