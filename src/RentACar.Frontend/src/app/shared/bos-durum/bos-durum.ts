import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { Ikon } from '../ikon/ikon';
import type { IkonAdi } from '../ikon/ikon-kaydi';

/**
 * Boş durum: liste/tablo kaydı yoksa. Başlık ve açıklama verilmezse ortak Türkçe metin.
 * Eylem düğmeleri içerik olarak verilir: `<rc-bos-durum><button class="rc-dugme">…</button></rc-bos-durum>`.
 */
@Component({
  selector: 'rc-bos-durum',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, TranslocoPipe],
  template: `
    <rc-ikon class="ikon" [ad]="ikon()" [boyut]="32" />
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
      margin-bottom: var(--rc-bosluk-2);
      color: var(--rc-metin-soluk);
    }
    .baslik {
      font-size: var(--rc-yazi-md);
      font-weight: var(--rc-agirlik-kalin);
    }
    .aciklama {
      color: var(--rc-metin-ikincil);
    }
    .eylemler:not(:empty) {
      display: flex;
      gap: var(--rc-bosluk-2);
      margin-top: var(--rc-bosluk-3);
    }
  `,
})
export class BosDurum {
  readonly ikon = input<IkonAdi>('inbox');
  readonly baslik = input('');
  readonly aciklama = input('');
}
