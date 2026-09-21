import { Dialog, DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { Overlay } from '@angular/cdk/overlay';
import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TranslocoPipe } from '@jsverse/transloco';

import { Ikon } from '@shared/ikon/ikon';

import { menuAra, type MenuKaydi } from '../menu/menu-modeli';

export interface KomutPaletiVerisi {
  readonly kayitlar: readonly MenuKaydi[];
}

/**
 * Ctrl+K / ⌘K komut paleti (tembel parça, CDK diyaloğu: odak kilidi, Esc = kapat, odak geri döner).
 * Menü öğelerinde Türkçe-gevşek arama (`İş` = `is`); ↑/↓ gezinme, Enter açar. Seçilen öğe diyalog
 * sonucu olarak kabuğa döner — açma kararı (router / Blazor tam sayfa + kirli form sorusu) kabukta.
 * Combobox + listbox deseni (`aria-activedescendant`).
 */
@Component({
  selector: 'rc-komut-paleti',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, TranslocoPipe],
  template: `
    <div class="palet">
      <h2 id="rc-palet-baslik" class="rc-gorunmez">{{ 'kabuk.palet.baslik' | transloco }}</h2>
      <div class="palet__arama">
        <rc-ikon ad="search" [boyut]="16" />
        <input
          class="palet__girdi"
          type="text"
          role="combobox"
          autocomplete="off"
          spellcheck="false"
          aria-autocomplete="list"
          [attr.aria-label]="'kabuk.palet.ara' | transloco"
          [placeholder]="'kabuk.palet.yerTutucu' | transloco"
          [attr.aria-expanded]="sonuclar().length > 0"
          [attr.aria-controls]="sonuclar().length ? 'rc-palet-liste' : null"
          [attr.aria-activedescendant]="etkinKimlik()"
          [value]="sorgu()"
          (input)="yaz($event)"
          (keydown)="tus($event)"
        />
      </div>
      @if (sonuclar().length) {
        <ul
          id="rc-palet-liste"
          class="palet__liste"
          role="listbox"
          [attr.aria-label]="'kabuk.palet.sonuclar' | transloco"
        >
          @for (kayit of sonuclar(); track kayit.kimlik; let i = $index) {
            <!-- Klavye combobox'ta (aria-activedescendant): seçenekler odak almaz, fare tıklaması ek yol. -->
            <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
            <li
              class="palet__secenek"
              role="option"
              [id]="'rc-palet-' + i"
              [attr.aria-selected]="i === secili()"
              (click)="ac(kayit)"
              (mousemove)="secili.set(i)"
            >
              <span class="palet__etiket">{{ kayit.etiket }}</span>
              @if (kayit.grup) {
                <span class="palet__grup">{{ kayit.grup }}</span>
              }
              @if (kayit.hedef.tur === 'blazor') {
                <rc-ikon ad="external-link" [boyut]="12" />
              }
            </li>
          }
        </ul>
      } @else {
        <p class="palet__bos" role="status">{{ 'kabuk.palet.sonucYok' | transloco }}</p>
      }
      <p class="palet__ipucu" aria-hidden="true">{{ 'kabuk.palet.ipucu' | transloco }}</p>
    </div>
  `,
  styleUrl: './komut-paleti.scss',
})
export class KomutPaleti {
  private readonly ref = inject<DialogRef<MenuKaydi, KomutPaleti>>(DialogRef);
  private readonly veri = inject<KomutPaletiVerisi>(DIALOG_DATA);
  private readonly belge = inject(DOCUMENT);
  private readonly enjektor = inject(Injector);

  protected readonly sorgu = signal('');
  protected readonly secili = signal(0);
  protected readonly sonuclar = computed(() => menuAra(this.veri.kayitlar, this.sorgu()));
  protected readonly etkinKimlik = computed(() =>
    this.sonuclar().length ? `rc-palet-${this.secili()}` : null,
  );

  protected yaz(olay: Event): void {
    this.sorgu.set((olay.target as HTMLInputElement).value);
    this.secili.set(0);
  }

  protected tus(olay: KeyboardEvent): void {
    const adet = this.sonuclar().length;
    switch (olay.key) {
      case 'ArrowDown':
        olay.preventDefault();
        if (adet) this.sec((this.secili() + 1) % adet);
        break;
      case 'ArrowUp':
        olay.preventDefault();
        if (adet) this.sec((this.secili() - 1 + adet) % adet);
        break;
      case 'Home':
        if (adet && olay.ctrlKey) this.sec(0);
        break;
      case 'End':
        if (adet && olay.ctrlKey) this.sec(adet - 1);
        break;
      case 'Enter': {
        olay.preventDefault();
        const kayit = this.sonuclar()[this.secili()];
        if (kayit) this.ac(kayit);
        break;
      }
    }
  }

  /** Klavyeyle seçilen öğe görünür alanda kalsın (çizimden sonra kaydırılır). */
  private sec(sira: number): void {
    this.secili.set(sira);
    afterNextRender(
      () => this.belge.getElementById(`rc-palet-${sira}`)?.scrollIntoView?.({ block: 'nearest' }),
      { injector: this.enjektor },
    );
  }

  protected ac(kayit: MenuKaydi): void {
    this.ref.close(kayit);
  }
}

/**
 * Paleti açar, seçilen öğeyi (ya da vazgeçilirse `undefined`) döndürür. Bu modül tembel yüklenir;
 * CDK dialog/overlay burada STATİK içe aktarılır (dinamik `import('@angular/cdk/overlay')` tüm
 * dışa aktarımları canlı tutup ilk paketi büyütüyordu).
 */
export async function komutPaletiniAc(
  enjektor: Injector,
  kayitlar: readonly MenuKaydi[],
): Promise<MenuKaydi | undefined> {
  const konum = enjektor.get(Overlay).position().global().centerHorizontally().top('12vh');
  const ref = enjektor.get(Dialog).open<MenuKaydi, KomutPaletiVerisi>(KomutPaleti, {
    data: { kayitlar },
    ariaLabelledBy: 'rc-palet-baslik',
    ariaModal: true,
    panelClass: 'rc-diyalog-paneli',
    backdropClass: 'rc-diyalog-perdesi',
    width: '36rem',
    maxWidth: 'calc(100vw - 2rem)',
    positionStrategy: konum,
    autoFocus: '.palet__girdi',
    restoreFocus: true,
  });
  return firstValueFrom(ref.closed);
}
