import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Icon } from '../ikon/icon';
import type { IkonAdi } from '../ikon/ikon-kaydi';

/**
 * Boş durum: liste/tablo kaydı yoksa. Başlık ve açıklama verilmezse ortak Türkçe metin.
 * Eylem düğmeleri içerik olarak verilir: `<rc-bos-durum><button class="rc-dugme">…</button></rc-bos-durum>`.
 */
@Component({
  selector: 'rc-bos-durum',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, TranslocoPipe],
  template: `
    <span class="ikon"><rc-ikon [ad]="ikon()" [boyut]="24" /></span>
    <p class="baslik">{{ baslik() || ('ortak.bosDurum.baslik' | transloco) }}</p>
    <p class="aciklama">{{ aciklama() || ('ortak.bosDurum.aciklama' | transloco) }}</p>
    <div class="eylemler"><ng-content /></div>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--rc-bosluk-1);
      padding: var(--rc-bosluk-8) var(--rc-bosluk-4);
      text-align: center;
    }
    .ikon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: var(--rc-bosluk-12);
      height: var(--rc-bosluk-12);
      margin-bottom: var(--rc-bosluk-2);
      border-radius: var(--rc-yaricap-tam);
      background-color: var(--rc-yuzey-alt);
      color: var(--rc-metin-soluk);
    }
    .baslik {
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-kalin);
    }
    .aciklama {
      max-width: 32rem;
      color: var(--rc-metin-soluk);
    }
    .eylemler:not(:empty) {
      display: flex;
      flex-wrap: wrap;
      justify-content: center;
      gap: var(--rc-bosluk-2);
      margin-top: var(--rc-bosluk-3);
    }
  `,
})
export class EmptyState {
  readonly ikon = input<IkonAdi>('inbox');
  readonly baslik = input('');
  readonly aciklama = input('');
}
