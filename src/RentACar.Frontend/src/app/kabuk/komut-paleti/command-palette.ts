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

import { Icon } from '@shared/ikon/icon';

import { searchMenu, type MenuKaydi } from '../menu/menu-modeli';

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
  imports: [Icon, TranslocoPipe],
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
          [attr.aria-expanded]="results().length > 0"
          [attr.aria-controls]="results().length ? 'rc-palet-liste' : null"
          [attr.aria-activedescendant]="activeId()"
          [value]="query()"
          (input)="write($event)"
          (keydown)="tus($event)"
        />
      </div>
      @if (results().length) {
        <ul
          id="rc-palet-liste"
          class="palet__liste"
          role="listbox"
          [attr.aria-label]="'kabuk.palet.sonuclar' | transloco"
        >
          @for (kayit of results(); track kayit.kimlik; let i = $index) {
            <!-- Klavye combobox'ta (aria-activedescendant): seçenekler odak almaz, fare tıklaması ek yol. -->
            <!-- eslint-disable-next-line @angular-eslint/template/click-events-have-key-events, @angular-eslint/template/interactive-supports-focus -->
            <li
              class="palet__secenek"
              role="option"
              [id]="'rc-palet-' + i"
              [attr.aria-selected]="i === secili()"
              (click)="open(kayit)"
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
  styleUrl: './command-palette.scss',
})
export class CommandPalette {
  private readonly ref = inject<DialogRef<MenuKaydi, CommandPalette>>(DialogRef);
  private readonly veri = inject<KomutPaletiVerisi>(DIALOG_DATA);
  private readonly belge = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  protected readonly query = signal('');
  protected readonly secili = signal(0);
  protected readonly results = computed(() => searchMenu(this.veri.kayitlar, this.query()));
  protected readonly activeId = computed(() =>
    this.results().length ? `rc-palet-${this.secili()}` : null,
  );

  protected write(evt: Event): void {
    this.query.set((evt.target as HTMLInputElement).value);
    this.secili.set(0);
  }

  protected tus(evt: KeyboardEvent): void {
    const count = this.results().length;
    switch (evt.key) {
      case 'ArrowDown':
        evt.preventDefault();
        if (count) this.select((this.secili() + 1) % count);
        break;
      case 'ArrowUp':
        evt.preventDefault();
        if (count) this.select((this.secili() - 1 + count) % count);
        break;
      case 'Home':
        if (count && evt.ctrlKey) this.select(0);
        break;
      case 'End':
        if (count && evt.ctrlKey) this.select(count - 1);
        break;
      case 'Enter': {
        evt.preventDefault();
        const record = this.results()[this.secili()];
        if (record) this.open(record);
        break;
      }
    }
  }

  /** Klavyeyle seçilen öğe görünür alanda kalsın (çizimden sonra kaydırılır). */
  private select(order: number): void {
    this.secili.set(order);
    afterNextRender(
      () => this.belge.getElementById(`rc-palet-${order}`)?.scrollIntoView?.({ block: 'nearest' }),
      { injector: this.injector },
    );
  }

  protected open(record: MenuKaydi): void {
    this.ref.close(record);
  }
}

/**
 * Paleti açar, seçilen öğeyi (ya da vazgeçilirse `undefined`) döndürür. Bu modül tembel yüklenir;
 * CDK dialog/overlay burada STATİK içe aktarılır (dinamik `import('@angular/cdk/overlay')` tüm
 * dışa aktarımları canlı tutup ilk paketi büyütüyordu).
 */
export async function komutPaletiniAc(
  injector: Injector,
  records: readonly MenuKaydi[],
): Promise<MenuKaydi | undefined> {
  const location = injector.get(Overlay).position().global().centerHorizontally().top('12vh');
  const ref = injector.get(Dialog).open<MenuKaydi, KomutPaletiVerisi>(CommandPalette, {
    data: { kayitlar: records },
    ariaLabelledBy: 'rc-palet-baslik',
    ariaModal: true,
    panelClass: 'rc-diyalog-paneli',
    backdropClass: 'rc-diyalog-perdesi',
    width: '36rem',
    maxWidth: 'calc(100vw - 2rem)',
    positionStrategy: location,
    autoFocus: '.palet__girdi',
    restoreFocus: true,
  });
  return firstValueFrom(ref.closed);
}
