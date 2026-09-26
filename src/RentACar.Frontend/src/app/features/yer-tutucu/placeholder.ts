import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { SessionService } from '@core/oturum/session-service';

import { PageBand } from '../../kabuk/sayfa-bandi/page-band';

/**
 * Yer tutucu ana sayfa (kabuk içinde; tema, çıkış ve menü F3.2'den beri kabukta). Oturumdaki kullanıcıyı
 * ve vitrin bağlantılarını gösterir; e2e ve axe kabuğu burada denetler.
 */
@Component({
  selector: 'rc-yer-tutucu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, RouterLink, PageBand],
  templateUrl: './placeholder.html',
  styleUrl: './placeholder.scss',
})
export class Placeholder {
  protected readonly oturum = inject(SessionService);
}
