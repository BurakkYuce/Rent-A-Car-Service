import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { sayiBicimle } from '@core/bicim/bicim';
import { Ikon } from '@shared/ikon/ikon';

import type { TabloSayfasi } from './tablo-modeli';

/** Sunucu sayfalaması çubuğu: aralık, sayfa başına, ilk/önceki/sonraki/son. */
@Component({
  selector: 'rc-tablo-sayfalama',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Ikon],
  template: `
    <nav class="sayfalama" [attr.aria-label]="'tablo.sayfalama.etiket' | transloco">
      <span class="aralik">
        @if (sayfa().toplam === 0) {
          {{ 'tablo.sayfalama.kayitYok' | transloco }}
        } @else {
          {{ 'tablo.sayfalama.aralik' | transloco: aralik() }}
        }
      </span>
      <label class="boyut">
        {{ 'tablo.sayfalama.boyut' | transloco }}
        <select #boyutSecimi class="secim" (change)="boyutSec(boyutSecimi.value)">
          @for (b of boyutlar(); track b) {
            <option [value]="b" [selected]="b === sayfa().boyut">{{ b }}</option>
          }
        </select>
      </label>
      <span class="konum">{{ 'tablo.sayfalama.sayfa' | transloco: konum() }}</span>
      <span class="rc-dugme-grubu">
        @for (d of dugmeler(); track d.etiket) {
          <button
            type="button"
            class="rc-dugme rc-dugme--kucuk rc-dugme--ikon"
            [disabled]="d.pasif"
            [attr.aria-label]="d.etiket | transloco"
            [title]="d.etiket | transloco"
            (click)="sayfaDegisti.emit(d.hedef)"
          >
            <rc-ikon [ad]="d.ikon" [boyut]="14" />
          </button>
        }
      </span>
    </nav>
  `,
  styles: `
    .sayfalama {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: var(--rc-bosluk-2) var(--rc-bosluk-4);
      min-height: calc(var(--rc-kontrol-yukseklik) + var(--rc-bosluk-3));
      padding: var(--rc-bosluk-1-5) var(--rc-bosluk-3);
      border-top: 1px solid var(--rc-kenar);
      color: var(--rc-metin-ikincil);
      font-size: var(--rc-yazi-sm);
      font-variant-numeric: tabular-nums;
    }
    .aralik {
      margin-inline-end: auto;
      color: var(--rc-metin);
    }
    .boyut {
      display: inline-flex;
      align-items: center;
      gap: var(--rc-bosluk-1-5);
    }
    .secim {
      height: var(--rc-kontrol-yukseklik-kucuk);
      padding: 0 var(--rc-bosluk-1);
      border: 1px solid var(--rc-kenar-kontrol);
      border-radius: var(--rc-yaricap-md);
      background: var(--rc-yuzey);
    }
  `,
})
export class TabloSayfalama {
  readonly sayfa = input.required<TabloSayfasi>();
  readonly boyutlar = input<readonly number[]>([25, 50, 100, 200]);
  readonly sayfaDegisti = output<number>();
  readonly boyutDegisti = output<number>();

  protected readonly toplamSayfa = computed(() => {
    const s = this.sayfa();
    return s.boyut <= 0 ? 1 : Math.max(1, Math.ceil(s.toplam / s.boyut));
  });

  protected readonly aralik = computed(() => {
    const s = this.sayfa();
    const bas = (s.sayfa - 1) * s.boyut + 1;
    return {
      bas: sayiBicimle(bas, '1.0-0'),
      bit: sayiBicimle(Math.min(s.toplam, s.sayfa * s.boyut), '1.0-0'),
      toplam: sayiBicimle(s.toplam, '1.0-0'),
    };
  });

  protected readonly konum = computed(() => ({
    sayfa: sayiBicimle(this.sayfa().sayfa, '1.0-0'),
    toplamSayfa: sayiBicimle(this.toplamSayfa(), '1.0-0'),
  }));

  protected readonly dugmeler = computed(() => {
    const s = this.sayfa().sayfa;
    const son = this.toplamSayfa();
    return [
      { etiket: 'tablo.sayfalama.ilk', ikon: 'chevrons-left', hedef: 1, pasif: s <= 1 },
      { etiket: 'tablo.sayfalama.onceki', ikon: 'chevron-left', hedef: s - 1, pasif: s <= 1 },
      { etiket: 'tablo.sayfalama.sonraki', ikon: 'chevron-right', hedef: s + 1, pasif: s >= son },
      { etiket: 'tablo.sayfalama.son', ikon: 'chevrons-right', hedef: son, pasif: s >= son },
    ] as const;
  });

  protected boyutSec(deger: string): void {
    const boyut = Number(deger);
    if (Number.isInteger(boyut) && boyut > 0) this.boyutDegisti.emit(boyut);
  }
}
