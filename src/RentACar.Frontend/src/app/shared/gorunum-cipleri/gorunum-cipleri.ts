import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink, type Params } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { sayiBicimle } from '@core/bicim/bicim';

/** Kayıtlı görünüm (Yol v2 §5.1/§8): `link` verilirse gezinme bağlantısı, yoksa `id` ile düğme. */
export interface SavedView {
  readonly ad: string;
  /** Sayaç rozeti; `null`/verilmezse rozet yok (sayaç ucu yoksa görünüm sayaçsız çalışır). */
  readonly sayac?: number | null;
  /** 'hata' → sayaç kırmızı (ör. dönüşü gecikenler). */
  readonly tur?: 'hata';
  readonly aktif: boolean;
  readonly link?: string | readonly unknown[];
  readonly sorgu?: Params;
  readonly id?: string;
}

/**
 * Kayıtlı görünüm çipleri: hap biçimli, seçili dolu lacivert, sayaç rozetli. Bağlantı kipinde `<nav>` +
 * `aria-current="page"`; düğme kipinde `aria-pressed`. Dar ekranda satır yatay kayar (gövde taşmaz).
 * `secildi` her iki kipte yayılır (bağlantıda gezinmeye ek bilgi).
 */
@Component({
  selector: 'rc-gorunum-cipleri',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, RouterLink, TranslocoPipe],
  template: `
    <nav class="kap" [attr.aria-label]="etiket() || ('ortak.gorunumler.etiket' | transloco)">
      <span class="baslik" aria-hidden="true">{{
        etiket() || ('ortak.gorunumler.etiket' | transloco)
      }}</span>
      <ul class="liste">
        @for (g of gorunumler(); track g.id ?? g.ad) {
          <li>
            @if (g.link !== undefined) {
              <a
                class="cip"
                [class.cip--aktif]="g.aktif"
                [routerLink]="g.link"
                [queryParams]="g.sorgu"
                [attr.aria-current]="g.aktif ? 'page' : null"
                (click)="secildi.emit(g)"
              >
                <ng-container *ngTemplateOutlet="icerik; context: { $implicit: g }" />
              </a>
            } @else {
              <button
                type="button"
                class="cip"
                [class.cip--aktif]="g.aktif"
                [attr.aria-pressed]="g.aktif"
                (click)="secildi.emit(g)"
              >
                <ng-container *ngTemplateOutlet="icerik; context: { $implicit: g }" />
              </button>
            }
          </li>
        }
      </ul>
    </nav>

    <ng-template #icerik let-g>
      <span>{{ g.ad }}</span>
      @if (counterText(g); as s) {
        <span class="sayac" [class.sayac--hata]="g.tur === 'hata'">{{ s }}</span>
      }
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    .kap {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: center;
      min-width: 0;
    }
    .baslik {
      flex-shrink: 0;
      color: var(--rc-metin-soluk);
      font-size: var(--rc-yazi-xs);
    }
    .liste {
      display: flex;
      gap: var(--rc-bosluk-1-5);
      min-width: 0;
      margin: 0;
      padding: var(--rc-bosluk-0-5);
      overflow-x: auto;
      list-style: none;
      scrollbar-width: thin;
    }
    .cip {
      display: inline-flex;
      gap: var(--rc-bosluk-1-5);
      align-items: center;
      height: var(--rc-kontrol-yukseklik-kucuk);
      padding: 0 var(--rc-bosluk-3);
      border: 1px solid var(--rc-cip-kenar);
      border-radius: var(--rc-yaricap-tam);
      background-color: var(--rc-yuzey);
      color: var(--rc-cip-metin);
      font: inherit;
      font-size: var(--rc-yazi-sm);
      font-weight: var(--rc-agirlik-orta);
      text-decoration: none;
      white-space: nowrap;
      cursor: pointer;
    }
    .cip:hover {
      background-color: var(--rc-yuzey-alt);
    }
    .cip--aktif,
    .cip--aktif:hover {
      border-color: var(--rc-cip-secili-zemin);
      background-color: var(--rc-cip-secili-zemin);
      color: var(--rc-cip-secili-metin);
    }
    .sayac {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 1.25rem;
      height: 1.125rem;
      padding: 0 var(--rc-bosluk-1-5);
      border-radius: var(--rc-yaricap-tam);
      background-color: var(--rc-cip-sayac-zemin);
      color: var(--rc-cip-sayac-metin);
      font-size: var(--rc-yazi-2xs);
      font-weight: var(--rc-agirlik-kalin);
      font-variant-numeric: tabular-nums;
    }
    .cip--aktif .sayac {
      background-color: var(--rc-cip-secili-sayac-zemin);
      color: var(--rc-cip-secili-sayac-metin);
    }
    .sayac--hata,
    .cip--aktif .sayac--hata {
      background-color: var(--rc-cip-hata-sayac-zemin);
      color: var(--rc-cip-hata-sayac-metin);
    }
  `,
})
export class SavedViewChipsComponent {
  readonly gorunumler = input.required<readonly SavedView[]>();
  /** Görünür ön etiket ve gezinme bölgesinin adı (verilmezse "Kayıtlı görünümler"). */
  readonly etiket = input('');
  readonly secildi = output<SavedView>();

  protected counterText(g: SavedView): string | null {
    return g.sayac === null || g.sayac === undefined ? null : sayiBicimle(g.sayac, '1.0-0');
  }
}
