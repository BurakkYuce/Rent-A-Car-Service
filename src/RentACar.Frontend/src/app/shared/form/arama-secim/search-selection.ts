import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  numberAttribute,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CdkListbox, CdkOption, type ListboxValueChangeEvent } from '@angular/cdk/listbox';
import { CdkConnectedOverlay, CdkOverlayOrigin } from '@angular/cdk/overlay';
import { TranslocoPipe } from '@jsverse/transloco';
import { Subject, catchError, debounce, map, of, startWith, switchMap, timer } from 'rxjs';
import { Icon } from '../../ikon/icon';
import { uniqueId } from '../alan/alan-baglami';
import { BaseControl, controlProviders } from '../kontroller/base-control';
import { SELECTION_MAX_LIMIT, type SelectionSource, type SecimSecenegi } from './selection-source';

type SearchState = 'bos' | 'kisa' | 'yukleniyor' | 'hazir' | 'hata';

interface AramaIstegi {
  readonly metin: string;
  readonly anlik: boolean;
}

/**
 * Aranabilir tekli seçim (combobox + CDK overlay + CDK listbox). Kaynak F1.6 `/secim/*` uçları
 * (`sunucuSecimKaynagi`): `q` + `limit ≤ 20`, yazarken gecikmeli (varsayılan 250 ms), önceki istek
 * iptal (switchMap). DOM odağı girdide kalır, etkin seçenek `aria-activedescendant` ile:
 * ↓/↑ gezin, Enter seç, Esc kapat (metin seçili öğeye döner), Tab kapatır.
 *
 * Değer seçilen öğenin kendisi (`{ id, etiket, … }`) — ön doldurmada etiket için ek istek gerekmez; gönderirken
 * `.id` alınır.
 */
@Component({
  selector: 'rc-arama-secim',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, CdkListbox, CdkOption, TranslocoPipe, Icon],
  providers: controlProviders(() => SearchSelection),
  styleUrl: './search-selection.scss',
  templateUrl: './search-selection.html',
})
export class SearchSelection extends BaseControl<SecimSecenegi> {
  readonly kaynak = input.required<SelectionSource>();
  readonly yerTutucu = input('');
  readonly limit = input(SELECTION_MAX_LIMIT, { transform: numberAttribute });
  readonly enAzHarf = input(0, { transform: numberAttribute });
  readonly gecikme = input(250, { transform: numberAttribute });

  private readonly girdi = viewChild.required<ElementRef<HTMLInputElement>>('girdi');
  private readonly kutu = viewChild.required<ElementRef<HTMLElement>>('kutu');

  protected readonly listId = uniqueId('rc-liste');
  protected readonly metin = signal('');
  protected readonly acik = signal(false);
  protected readonly durum = signal<SearchState>('bos');
  protected readonly results = signal<readonly SecimSecenegi[]>([]);
  protected readonly activeIndex = signal(-1);
  protected readonly panelWidth = signal(0);

  protected readonly activeId = computed(() =>
    this.acik() && this.activeIndex() >= 0 && this.activeIndex() < this.results().length
      ? this.optionId(this.activeIndex())
      : null,
  );

  private readonly searches = new Subject<AramaIstegi>();

  constructor() {
    super();
    this.searches
      .pipe(
        debounce((request) => timer(request.anlik ? 0 : this.gecikme())),
        switchMap((request) => this.fetch(request.metin)),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(({ durum: status, sonuclar: results }) => {
        this.durum.set(status);
        this.results.set(results);
        this.activeIndex.set(results.length > 0 ? 0 : -1);
      });
  }

  protected override writtenExternally(value: SecimSecenegi | null): void {
    this.metin.set(value?.etiket ?? '');
  }

  protected optionId(order: number): string {
    return `${this.listId}-${order}`;
  }

  protected openItem(instant = true): void {
    if (this.pasif()) return;
    if (!this.acik()) {
      this.panelWidth.set(this.kutu().nativeElement.getBoundingClientRect().width);
      this.acik.set(true);
      // Açılışta metin seçili öğenin etiketiyse filtre değil — ilk sayfa gelir.
      const selected = this.deger();
      const search = selected && this.metin() === selected.etiket ? '' : this.metin();
      this.searches.next({ metin: search, anlik: instant });
    }
  }

  protected kapat(): void {
    this.acik.set(false);
    this.activeIndex.set(-1);
  }

  protected written(evt: Event): void {
    const text = (evt.target as HTMLInputElement).value;
    this.metin.set(text);
    if (!this.acik()) {
      this.panelWidth.set(this.kutu().nativeElement.getBoundingClientRect().width);
      this.acik.set(true);
    }
    this.searches.next({ metin: text, anlik: false });
  }

  protected tus(evt: KeyboardEvent): void {
    const count = this.results().length;
    switch (evt.key) {
      case 'ArrowDown':
        evt.preventDefault();
        if (!this.acik()) this.openItem();
        else if (count > 0) this.activate(Math.min(this.activeIndex() + 1, count - 1));
        break;
      case 'ArrowUp':
        evt.preventDefault();
        if (count > 0) this.activate(Math.max(this.activeIndex() - 1, 0));
        break;
      case 'Enter': {
        const oge = this.acik() ? this.results()[this.activeIndex()] : undefined;
        if (oge) {
          evt.preventDefault();
          this.select(oge);
        }
        break;
      }
      case 'Escape':
        if (this.acik()) {
          evt.preventDefault();
          evt.stopPropagation();
          this.restoreText();
          this.kapat();
        }
        break;
      case 'Tab':
        this.kapat();
        break;
    }
  }

  protected released(): void {
    this.kapat();
    if (this.metin().trim() === '') {
      if (this.deger() !== null) this.notify(null);
      this.metin.set('');
    } else {
      // Seçilmeden bırakılan arama metni değer değildir; seçili öğeye dön.
      this.restoreText();
    }
    this.touch();
  }

  protected selectedFromList(evt: ListboxValueChangeEvent<unknown>): void {
    // Seçenek değerleri bu bileşenin `sonuclar()` öğeleri; kimliğiyle eşlenir.
    const oge = this.results().find((o) => o === evt.option?.value);
    if (oge) this.select(oge);
  }

  protected clear(): void {
    this.notify(null);
    this.metin.set('');
    this.touch();
    this.girdi().nativeElement.focus();
  }

  protected outsideClick(evt: MouseEvent): void {
    if (!this.kutu().nativeElement.contains(evt.target as Node)) this.kapat();
  }

  /** Uca özgü kısa kod (lokasyon/ek hizmet `kod`, araç `plaka`) varsa ikinci sütunda. */
  protected kodu(oge: SecimSecenegi): string | null {
    const extra = oge as Partial<Record<'kod' | 'plaka', unknown>>;
    const code = extra.kod ?? extra.plaka;
    return typeof code === 'string' && code !== '' && code !== oge.etiket ? code : null;
  }

  protected secili(oge: SecimSecenegi): boolean {
    return this.deger()?.id === oge.id;
  }

  private select(oge: SecimSecenegi): void {
    this.notify(oge);
    this.metin.set(oge.etiket);
    this.kapat();
    this.girdi().nativeElement.focus();
  }

  /**
   * Metni seçili öğenin etiketine döndürür. DOM da doğrudan yazılır: yazma ile bırakma arasında
   * çizim olmadıysa bağlamanın son değeri etiketle aynı kalır ve `[value]` güncellenmez.
   */
  private restoreText(): void {
    const label = this.deger()?.etiket ?? '';
    this.metin.set(label);
    this.girdi().nativeElement.value = label;
  }

  private activate(order: number): void {
    this.activeIndex.set(order);
    const oge = this.girdi().nativeElement.ownerDocument.getElementById(this.optionId(order));
    oge?.scrollIntoView?.({ block: 'nearest' });
  }

  private fetch(text: string) {
    const search = text.trim();
    if (search.length < this.enAzHarf()) {
      return of({ durum: 'kisa' as SearchState, sonuclar: [] as readonly SecimSecenegi[] });
    }
    const limit = Math.min(Math.max(1, this.limit()), SELECTION_MAX_LIMIT);
    return this.kaynak()(search, limit).pipe(
      map((results) => ({ durum: 'hazir' as SearchState, sonuclar: results.slice(0, limit) })),
      catchError(() =>
        of({ durum: 'hata' as SearchState, sonuclar: [] as readonly SecimSecenegi[] }),
      ),
      startWith({ durum: 'yukleniyor' as SearchState, sonuclar: [] as readonly SecimSecenegi[] }),
    );
  }
}
