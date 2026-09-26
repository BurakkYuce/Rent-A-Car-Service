import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { Permission } from '@core/oturum/oturum-tipleri';
import { Icon } from '@shared/ikon/icon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

export interface HizliIslem {
  readonly etiket: string;
  readonly ikon: IkonAdi;
  readonly rota: string;
}

/**
 * Hızlı işlem bağlantıları. `izin` hedef rotanın `izinGuard`'ıyla AYNI olmalı: kullanıcı açamayacağı ekranın
 * bağlantısını görmez (rota kapısı yine de son sözü söyler).
 */
export const QUICK_ACTIONS: readonly {
  readonly etiket: CeviriAnahtari;
  readonly ikon: IkonAdi;
  readonly rota: string;
  readonly izin: Permission;
}[] = [
  { etiket: 'panel.hizli.yeniKira', ikon: 'key', rota: '/kiralar/yeni', izin: 'OperationsWrite' },
  {
    etiket: 'panel.hizli.musaitlik',
    ikon: 'calendar-event',
    rota: '/musaitlik',
    izin: 'OperationsWrite',
  },
  { etiket: 'panel.hizli.cari', ikon: 'users', rota: '/cariler/yeni', izin: 'OperationsWrite' },
  { etiket: 'panel.hizli.kasa', ikon: 'cash', rota: '/kasa', izin: 'FinanceWrite' },
];

/** Hızlı işlemler (Yol v2 §6 `rc-hizli-islemler`): ikonlu bağlantı listesi. */
@Component({
  selector: 'rc-hizli-islemler',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterLink, TranslocoPipe],
  template: `
    <nav class="kap" aria-labelledby="panel-hizli-islemler">
      <h2 id="panel-hizli-islemler">{{ 'panel.eylemler' | transloco }}</h2>
      <ul>
        @for (b of baglantilar(); track b.rota) {
          <li>
            <a class="baglanti" [routerLink]="b.rota">
              <rc-ikon [ad]="b.ikon" [boyut]="16" />
              <span>{{ b.etiket }}</span>
            </a>
          </li>
        }
      </ul>
    </nav>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .kap {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-2);
    }
    h2 {
      margin: 0;
      color: var(--rc-metin-ikincil);
      font-size: var(--rc-yazi-xs);
      font-weight: var(--rc-agirlik-orta);
    }
    ul {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .baglanti {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: center;
      min-height: 2.5rem;
      padding: 0 var(--rc-bosluk-3);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-lg);
      background-color: var(--rc-yuzey);
      color: var(--rc-metin);
      font-size: var(--rc-yazi-sm);
      font-weight: var(--rc-agirlik-orta);
      text-decoration: none;
    }
    .baglanti:hover {
      background-color: var(--rc-yuzey-alt);
    }
    rc-ikon {
      color: var(--rc-vurgu-metin);
    }
  `,
})
export class QuickActions {
  readonly baglantilar = input.required<readonly HizliIslem[]>();
}
