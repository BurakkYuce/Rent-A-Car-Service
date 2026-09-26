import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterEveryRender,
  afterNextRender,
  computed,
  contentChildren,
  effect,
  inject,
  input,
  linkedSignal,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import {
  createTable,
  functionalUpdate,
  getCoreRowModel,
  type ColumnDef,
  type ColumnSizingInfoState,
  type Header,
  type Updater,
} from '@tanstack/table-core';
import {
  Virtualizer,
  elementScroll,
  observeElementOffset,
  observeElementRect,
} from '@tanstack/virtual-core';
import { take } from 'rxjs';

import { translationFunction } from '@core/i18n/ceviri';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';
import { EmptyState } from '@shared/bos-durum/empty-state';
import { Icon } from '@shared/ikon/icon';

import { exportUrl, type DisaAktarma, type ExportFormat } from './disa-aktarma';
import {
  layoutKey,
  mergeLayout,
  minWidth,
  setWidth,
  isHideable,
  setVisibility,
  setSort,
  scrollColumn,
  placeColumn,
  tanstackState,
  defaultLayout,
} from './tablo-duzeni';
import { resolveTableLayoutStore } from './table-layout-store';
import { TableCell, type TabloHucreBaglami } from './table-cell';
import { resolveSource } from './tablo-kaynagi';
import { navigateCell, clampPosition, type HucreKonumu } from './tablo-klavye';
import {
  SELECTION_COLUMN,
  SELECTION_COLUMN_WIDTH,
  TABLE_LIMITS,
  type TabloDuzeni,
  type TableSource,
  type TabloSutunu,
} from './tablo-modeli';
import { TablePaging } from './table-paging';
import { TableColumnPicker, type SutunSeciciOgesi } from './table-column-picker';
import { ariaSort, parseSort, sortText, nextSort } from './tablo-siralama';

/** Şablonun çizdiği görünür sütun. */
interface GorunurSutun<T> {
  readonly kod: string;
  readonly tanim: TabloSutunu<T> | null; // null = seçim sütunu
  readonly baslik: Header<T, unknown>;
  readonly genislik: number;
  /** Sabitse sol konumu (px), değilse null. */
  readonly sol: number | null;
  readonly sonSabit: boolean;
  readonly hiza: 'bas' | 'son' | 'orta';
  readonly siralanabilir: boolean;
  readonly tasinabilir: boolean;
}

interface CizilenSatir<T> {
  readonly satir: T;
  /** Sayfadaki sıra (0 tabanlı). */
  readonly index: number;
  readonly kimlik: string;
}

const SAVE_DELAY_MS = 600;
const KEYBOARD_WIDTH_STEP = 16;
const SKELETON_ROW = 8;
const EMPTY_SIZING: ColumnSizingInfoState = {
  columnSizingStart: [],
  deltaOffset: null,
  deltaPercentage: null,
  isResizingColumn: false,
  startOffset: null,
  startSize: null,
};

/**
 * Tablo motoru (F3.5): yoğun satır (32 px), sabit başlık + sabit ilk sütun(lar), para sağa yaslı ve
 * tr biçimli, sütun göster/gizle/sırala/boyutla (kullanıcıya kayıtlı, `TabloDuzenleri`), sunucu
 * sayfalama/sıralama (F3.4 liste sorgusu), sanal satırlar, satır seçimi, klavye gezinmesi (APG
 * Data Grid) ve dört AYRI durum (boş-istek/yükleniyor/hazır/hata).
 *
 * TanStack `table-core` (sütun durumu + boyutlama) ve `virtual-core` (satır sanallaştırma) doğrudan,
 * çerçeve bağdaştırıcısız kullanılır (bağdaştırıcılar deneysel/zone'a bağlı). Revlo'dan farklar:
 * düzen otele değil kullanıcıya bağlı, zoneless, 48 → 32 px satır, klavye gezinmesi eklendi.
 */
@Component({
  selector: 'rc-tablo',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    NgTemplateOutlet,
    TranslocoPipe,
    Icon,
    EmptyState,
    TablePaging,
    TableColumnPicker,
    ...FORMAT_PIPES,
  ],
  templateUrl: './table.html',
  styleUrl: './table.scss',
  // Izgara olayları kökte dinlenir (hücreler `data-hucre` taşır; araç çubuğu olayları yok sayılır).
  host: {
    '(keydown)': 'keyboard($event)',
    '(focusin)': 'focusReceived($event)',
  },
})
export class Table<T> {
  // ------------------------------------------------------------------ girdiler / çıktılar

  /** Erişilebilir ad (`aria-label`). */
  readonly etiket = input.required<string>();
  readonly sutunlar = input.required<readonly TabloSutunu<T>[]>();
  /** F3.4 `TemelStore` durumu (`store.liste.durum()`). */
  readonly kaynak = input.required<TableSource<T>>();
  /** Satırın kalıcı kimliği (seçim ve DOM izleme). */
  readonly satirKimligi = input.required<(row: T) => string>();
  /** Kullanıcı düzeninin anahtarı (`kiralar.liste`); null → düzen kaydedilmez. */
  readonly tabloKodu = input<string | null>(null);
  /** Geçerli sunucu sıralaması (F3.4 `sorgu().sirala`). */
  readonly sirala = input<string | null>(null);
  /** Sayfanın varsayılan sıralaması (`ListeTanimi.varsayilanSirala`): kayıtlı tercih yalnız bu durumdayken uygulanır. */
  readonly varsayilanSirala = input<string | null>(null);
  readonly boyutSecenekleri = input<readonly number[]>([25, 50, 100, 200]);
  readonly secilebilir = input(false);
  /** Seçili satır kimlikleri (sayfalar arası korunur). `[(secim)]`. */
  readonly secim = model<readonly string[]>([]);
  readonly disaAktarma = input<DisaAktarma | null>(null);
  /** Bu satır sayısının üstünde satırlar sanallaştırılır. */
  readonly sanalEsik = input(60);
  /**
   * Satıra ek sınıf (Yol v2): ör. bugün dönen/çıkan satır → `'rc-satir-bugun'` (krem vurgu). Seçili satır vurgusu
   * motorun kendisinde (`secilebilir` + `secim`); bu yalnız görünüm, davranış değiştirmez.
   */
  readonly satirSinifi = input<((row: T) => string | null) | null>(null);

  readonly siralaDegisti = output<string | null>();
  readonly sayfaDegisti = output<number>();
  readonly boyutDegisti = output<number>();
  /** Enter ya da çift tıklama. */
  readonly satirAc = output<T>();
  readonly yenidenDene = output<void>();

  // ------------------------------------------------------------------ iç durum

  private readonly ceviri = translationFunction();
  private readonly store = resolveTableLayoutStore();
  private readonly teardown = inject(DestroyRef);
  private readonly kaydirici = viewChild<ElementRef<HTMLDivElement>>('kaydirici');
  private readonly hucreSablonlari = contentChildren<TableCell<T>>(TableCell);

  protected readonly SELECTION = SELECTION_COLUMN;
  protected readonly skeletonRows = Array.from({ length: SKELETON_ROW }, (_, i) => i);

  protected readonly hal = computed(() => resolveSource(this.kaynak()));
  protected readonly satirlar = computed(() => this.hal().satirlar);

  /** Kullanıcı düzeni; tanımlar değişince kayıtlıyla yeniden uzlaşır. */
  private readonly layout = linkedSignal<readonly TabloSutunu<T>[], TabloDuzeni>({
    source: this.sutunlar,
    computation: (columns, previous) => mergeLayout(columns, previous?.value ?? null),
  });
  private readonly sizing = signal<ColumnSizingInfoState>(EMPTY_SIZING);
  private savedKey: string | null = null;
  private saveTimer: ReturnType<typeof setTimeout> | null = null;
  protected readonly saveError = signal(false);
  protected readonly announcement = signal('');

  protected readonly sort = computed(() => parseSort(this.sutunlar(), this.sirala()));

  private readonly tanstackColumns = computed<ColumnDef<T, unknown>[]>(() => {
    const definitions: ColumnDef<T, unknown>[] = this.sutunlar().map((s) => ({
      id: s.kod,
      accessorFn: (row: T) => s.deger(row),
      header: s.baslik,
      minSize: minWidth(s),
      maxSize: TABLE_LIMITS.enFazlaGenislik,
      enableHiding: isHideable(s),
      enableResizing: true,
    }));
    if (!this.secilebilir()) return definitions;
    return [
      {
        id: SELECTION_COLUMN,
        size: SELECTION_COLUMN_WIDTH,
        minSize: SELECTION_COLUMN_WIDTH,
        maxSize: SELECTION_COLUMN_WIDTH,
        enableResizing: false,
        enableHiding: false,
      },
      ...definitions,
    ];
  });

  private readonly tanstack = createTable<T>({
    data: [],
    columns: [],
    state: {},
    onStateChange: () => undefined,
    renderFallbackValue: null,
    getCoreRowModel: getCoreRowModel(),
  });

  /** TanStack tablosu (her okumada güncel seçeneklerle; nesne aynı, bu yüzden `equal` yok). */
  private readonly tablo = computed(
    () => {
      const status = tanstackState(this.sutunlar(), this.layout(), this.secilebilir());
      this.tanstack.setOptions((previous) => ({
        ...previous,
        columns: this.tanstackColumns(),
        state: { ...this.tanstack.initialState, ...status, columnSizingInfo: this.sizing() },
        enableColumnResizing: true,
        columnResizeMode: 'onChange',
        manualSorting: true,
        manualPagination: true,
        manualFiltering: true,
        onColumnSizingChange: (u: Updater<Record<string, number>>) =>
          this.widthChanged(functionalUpdate(u, status.columnSizing)),
        onColumnSizingInfoChange: (u: Updater<ColumnSizingInfoState>) =>
          this.resizeChanged(functionalUpdate(u, this.sizing())),
      }));
      return this.tanstack;
    },
    { equal: () => false },
  );

  protected readonly visibleColumns = computed<readonly GorunurSutun<T>[]>(() => {
    const table = this.tablo();
    const definition = new Map(this.sutunlar().map((s) => [s.kod, s] as const));
    const headers = table.getHeaderGroups()[0]?.headers ?? [];
    return headers.map((title) => {
      const column = title.column;
      const s = definition.get(column.id) ?? null;
      const fixedValue = column.getIsPinned() === 'left';
      return {
        kod: column.id,
        tanim: s,
        baslik: title,
        genislik: column.getSize(),
        sol: fixedValue ? column.getStart('left') : null,
        sonSabit: fixedValue && column.getIsLastColumn('left'),
        hiza:
          s === null
            ? 'orta'
            : (s.hizala ?? (s.tur === 'para' || s.tur === 'sayi' ? 'son' : 'bas')),
        siralanabilir: s !== null && s.sirala !== undefined && s.sirala !== false,
        tasinabilir: s !== null && !s.sabit,
      };
    });
  });

  protected readonly totalWidth = computed(() =>
    this.visibleColumns().reduce((t, s) => t + s.genislik, 0),
  );
  protected readonly fixedWidth = computed(() =>
    this.visibleColumns()
      .filter((s) => s.sol !== null)
      .reduce((t, s) => t + s.genislik, 0),
  );

  protected readonly pickerItems = computed<readonly SutunSeciciOgesi[]>(() => {
    const definition = new Map(this.sutunlar().map((s) => [s.kod, s] as const));
    const list = this.layout().sutunlar;
    return list.map((d, i) => {
      const s = definition.get(d.kod);
      const movable = s !== undefined && !s.sabit;
      const previous = definition.get(list[i - 1]?.kod ?? '');
      const next = definition.get(list[i + 1]?.kod ?? '');
      return {
        kod: d.kod,
        baslik: s?.baslik ?? d.kod,
        gorunur: d.gorunur,
        gizlenebilir: s !== undefined && isHideable(s),
        oncekiyeTasinabilir: movable && previous !== undefined && !previous.sabit,
        sonrakiyeTasinabilir: movable && next !== undefined,
      };
    });
  });

  private readonly templates = computed(
    () => new Map(this.hucreSablonlari().map((h) => [h.kod(), h.sablon] as const)),
  );

  // ------------------------------------------------------------------ sanallaştırma

  private readonly rowPx = signal(32);
  private readonly headerPx = signal(32);
  private readonly virtualVersion = signal(0);
  private virtualized: Virtualizer<HTMLDivElement, HTMLTableRowElement> | null = null;
  protected readonly isVirtual = computed(() => this.satirlar().length > this.sanalEsik());

  protected readonly render = computed(() => {
    this.virtualVersion();
    const rows = this.satirlar();
    const identity = this.satirKimligi();
    const create = (index: number): CizilenSatir<T> => ({
      satir: rows[index],
      index,
      kimlik: identity(rows[index]),
    });
    const virtualized = this.virtualized;
    if (!this.isVirtual()) return { satirlar: rows.map((_, i) => create(i)), ust: 0, alt: 0 };
    if (virtualized === null || virtualized.options.count !== rows.length) {
      // Sanallaştırıcı henüz güncellenmedi (aynı tikteki effect'te güncellenir): tüm sayfayı değil
      // ilk ekranı çiz, geri kalanı boşlukla tut — 200 × 49 hücre tek karede çizilmesin.
      const ilk = Math.min(rows.length, 40);
      return {
        satirlar: rows.slice(0, ilk).map((_, i) => create(i)),
        ust: 0,
        alt: (rows.length - ilk) * this.rowPx(),
      };
    }
    const items = virtualized.getVirtualItems();
    if (items.length === 0) return { satirlar: [], ust: 0, alt: 0 };
    const edge = virtualized.options.scrollMargin;
    const first = items[0];
    const last = items[items.length - 1];
    return {
      satirlar: items.filter((o) => o.index < rows.length).map((o) => create(o.index)),
      ust: Math.max(0, first.start - edge),
      alt: Math.max(0, virtualized.getTotalSize() - (last.end - edge)),
    };
  });

  // ------------------------------------------------------------------ seçim

  protected readonly selectedSet = computed(() => new Set(this.secim()));
  protected readonly pageSelection = computed<'hepsi' | 'bazi' | 'yok'>(() => {
    const set = this.selectedSet();
    const identity = this.satirKimligi();
    const rows = this.satirlar();
    const selected = rows.filter((s) => set.has(identity(s))).length;
    if (selected === 0) return 'yok';
    return selected === rows.length ? 'hepsi' : 'bazi';
  });

  // ------------------------------------------------------------------ klavye

  /** Aktif hücre: satır 0 başlık, 1..n veri (sayfadaki sıra + 1). */
  protected readonly aktif = signal<HucreKonumu>({ satir: 0, sutun: 0 });
  private focusPending = false;
  /** Sekme durağı: aktif satır sanal pencerenin dışındaysa başlıktaki aynı sütun (Tab ızgarayı kaçırmasın). */
  protected readonly tabStop = computed<HucreKonumu>(() => {
    const k = clampPosition(this.aktif(), this.gridSize());
    if (k.satir === 0) return k;
    const strikethrough = this.render().satirlar.some((c) => c.index === k.satir - 1);
    return strikethrough ? k : { satir: 0, sutun: k.sutun };
  });
  private readonly gridSize = computed(() => ({
    satirSayisi: this.satirlar().length + 1,
    sutunSayisi: this.visibleColumns().length,
    sayfaAdimi: Math.max(
      1,
      Math.floor(
        ((this.kaydirici()?.nativeElement.clientHeight || 320) - this.headerPx()) / this.rowPx(),
      ),
    ),
  }));

  /** `aria-rowindex` tabanı: sunucu sayfalamasında önceki sayfaların satırları. */
  protected readonly rowOffset = computed(() => {
    const s = this.hal().sayfa;
    return s === null ? 0 : (s.sayfa - 1) * s.boyut;
  });
  protected readonly totalRows = computed(() => this.hal().sayfa?.toplam ?? this.satirlar().length);

  constructor() {
    // Kullanıcı düzeni: tablo kodu gelince bir kez okunur.
    effect(() => {
      const code = this.tabloKodu();
      untracked(() => this.loadLayout(code));
    });

    // Yeni sayfa/sıralama: dikey kaydırma başa döner, aktif hücre ızgarada kalır.
    let previousQuery = '';
    effect(() => {
      const query = `${this.hal().sayfa?.sayfa ?? ''}|${this.sirala() ?? ''}`;
      const size = this.gridSize();
      untracked(() => {
        this.aktif.update((k) => clampPosition(k, size));
        if (query === previousQuery) return;
        previousQuery = query;
        const el = this.kaydirici()?.nativeElement;
        if (el !== undefined && el.scrollTop > 0) el.scrollTop = 0;
      });
    });

    // Satır sayısı/yüksekliği değişince sanallaştırıcı güncellenir.
    effect(() => {
      const count = this.satirlar().length;
      const isVirtual = this.isVirtual();
      const px = this.rowPx();
      const title = this.headerPx();
      untracked(() => {
        if (this.virtualized === null) return;
        this.virtualized.setOptions(this.virtualOptions(count, isVirtual, px, title));
        this.virtualized._willUpdate();
        this.virtualVersion.update((n) => n + 1);
      });
    });

    afterNextRender(() => {
      const el = this.kaydirici()?.nativeElement;
      if (el === undefined) return;
      this.rowPx.set(this.cssPx(el, '--rc-tablo-satir', 32));
      this.headerPx.set(el.querySelector('thead')?.getBoundingClientRect().height || this.rowPx());
      this.virtualized = new Virtualizer(
        this.virtualOptions(
          this.satirlar().length,
          this.isVirtual(),
          this.rowPx(),
          this.headerPx(),
        ),
      );
      const clear = this.virtualized._didMount();
      this.virtualized._willUpdate();
      this.virtualVersion.update((n) => n + 1);
      this.teardown.onDestroy(clear);
    });

    afterEveryRender(() => {
      if (this.focusPending) this.focus();
    });

    this.teardown.onDestroy(() => this.submitPendingRecord());
  }

  // ------------------------------------------------------------------ şablon yardımcıları

  protected sablon(code: string) {
    return this.templates().get(code) ?? null;
  }

  protected context(row: T, column: TabloSutunu<T>): TabloHucreBaglami<T> {
    return { $implicit: row, deger: column.deger(row) };
  }

  protected numberValue(column: TabloSutunu<T>, row: T): number | null {
    const d = column.deger(row);
    return typeof d === 'number' && Number.isFinite(d) ? d : null;
  }

  protected dateValue(column: TabloSutunu<T>, row: T): Date | string | number | null {
    const d = column.deger(row);
    return d instanceof Date || typeof d === 'string' || typeof d === 'number' ? d : null;
  }

  protected textValue(column: TabloSutunu<T>, row: T): string {
    const d = column.deger(row);
    return d === null || d === undefined ? '' : String(d);
  }

  protected currency(column: TabloSutunu<T>, row: T): string {
    const b = column.paraBirimi;
    return typeof b === 'function' ? b(row) : (b ?? 'TRY');
  }

  protected ariaSort(code: string) {
    return ariaSort(this.sort(), code);
  }

  protected tabOrder(row: number, column: number): 0 | -1 {
    const d = this.tabStop();
    return d.satir === row && d.sutun === column ? 0 : -1;
  }

  protected exportUrl(format: ExportFormat): string {
    const d = this.disaAktarma();
    return d === null ? '' : exportUrl(d, format);
  }

  // ------------------------------------------------------------------ sıralama

  protected sortClick(code: string): void {
    const newItem = nextSort(this.sutunlar(), this.sort(), code);
    this.siralaDegisti.emit(sortText(this.sutunlar(), newItem));
    this.changeLayout(setSort(this.layout(), newItem));
    const s = this.sutunlar().find((x) => x.kod === code);
    this.announcement.set(
      newItem === null
        ? this.ceviri('tablo.duyuru.siralamaYok')
        : this.ceviri(newItem.azalan ? 'tablo.duyuru.azalan' : 'tablo.duyuru.artan', {
            baslik: s?.baslik ?? code,
          }),
    );
  }

  // ------------------------------------------------------------------ sütun düzeni

  protected toggleVisibility(code: string, visible: boolean): void {
    this.changeLayout(setVisibility(this.sutunlar(), this.layout(), code, visible));
  }

  protected scrollColumn(code: string, yon: -1 | 1): void {
    this.changeLayout(scrollColumn(this.sutunlar(), this.layout(), code, yon));
  }

  protected resetToDefault(): void {
    this.stopTimer();
    const defaultValue = defaultLayout(this.sutunlar());
    this.layout.set(defaultValue);
    this.savedKey = layoutKey(defaultValue);
    this.saveError.set(false);
    if (this.sirala() !== this.varsayilanSirala()) this.siralaDegisti.emit(null);
    const code = this.tabloKodu();
    if (code !== null) {
      this.store
        .reset(code)
        .pipe(take(1))
        .subscribe({ error: () => this.saveError.set(true) });
    }
  }

  private widthChanged(newItem: Record<string, number>): void {
    let layout = this.layout();
    // Yalnız GERÇEKTEN değişen sütun yazılır; diğerleri `null` (tanım varsayılanı) kalır.
    const existing = tanstackState(this.sutunlar(), layout, false).columnSizing;
    for (const [code, px] of Object.entries(newItem)) {
      if (code !== SELECTION_COLUMN && code in existing && existing[code] !== px) {
        layout = setWidth(this.sutunlar(), layout, code, px);
      }
    }
    this.changeLayout(layout);
    this.tablo();
  }

  private resizeChanged(info: ColumnSizingInfoState): void {
    this.sizing.set(info);
    this.tablo(); // TanStack durumu hemen eşitlensin: sonraki fare hareketi güncel başlangıcı görsün
  }

  protected startResize(evt: MouseEvent | TouchEvent, title: Header<T, unknown>): void {
    evt.stopPropagation();
    title.getResizeHandler()(evt);
  }

  protected resetWidth(code: string): void {
    const s = this.sutunlar().find((x) => x.kod === code);
    if (s === undefined) return;
    const layout = this.layout();
    this.changeLayout({
      ...layout,
      sutunlar: layout.sutunlar.map((d) => (d.kod === code ? { ...d, genislik: null } : d)),
    });
  }

  // Sürükle-bırak (başlık) --------------------------------------------------------------
  private dragging: string | null = null;

  protected dragStart(evt: DragEvent, code: string): void {
    if (this.sizing().isResizingColumn !== false) {
      evt.preventDefault();
      return;
    }
    this.dragging = code;
    evt.dataTransfer?.setData('text/plain', code);
    if (evt.dataTransfer) evt.dataTransfer.effectAllowed = 'move';
  }

  protected dragOver(evt: DragEvent, column: GorunurSutun<T>): void {
    if (this.dragging !== null && column.tasinabilir && column.kod !== this.dragging) {
      evt.preventDefault();
    }
  }

  protected release(evt: DragEvent, column: GorunurSutun<T>): void {
    const code = this.dragging;
    this.dragging = null;
    if (code === null || !column.tasinabilir) return;
    evt.preventDefault();
    const cell = evt.currentTarget as HTMLElement;
    const box = cell.getBoundingClientRect();
    const location = evt.clientX < box.left + box.width / 2 ? 'once' : 'sonra';
    this.changeLayout(placeColumn(this.sutunlar(), this.layout(), code, column.kod, location));
  }

  protected dragEnd(): void {
    this.dragging = null;
  }

  // ------------------------------------------------------------------ seçim

  protected rowSelection(identity: string, selected: boolean): void {
    const existing = this.secim();
    if (selected === existing.includes(identity)) return;
    this.secim.set(selected ? [...existing, identity] : existing.filter((k) => k !== identity));
  }

  protected selectPage(selected: boolean): void {
    const identity = this.satirKimligi();
    const onPageItems = this.satirlar().map((s) => identity(s));
    const set = new Set(this.secim());
    for (const k of onPageItems) {
      if (selected) set.add(k);
      else set.delete(k);
    }
    this.secim.set([...set]);
  }

  // ------------------------------------------------------------------ klavye / odak

  protected focusReceived(evt: FocusEvent): void {
    const location = this.cellPosition(evt.target);
    if (location !== null) this.aktif.set(location);
  }

  protected keyboard(evt: KeyboardEvent): void {
    const target = evt.target as HTMLElement;
    const location = this.cellPosition(target);
    if (location === null) return;
    const column = this.visibleColumns()[location.sutun];

    // Başlıkta Alt+←/→ genişlik, Alt+Shift+←/→ sıra.
    if (
      location.satir === 0 &&
      evt.altKey &&
      (evt.key === 'ArrowLeft' || evt.key === 'ArrowRight')
    ) {
      evt.preventDefault();
      const yon = evt.key === 'ArrowLeft' ? -1 : 1;
      if (column?.tanim) {
        if (evt.shiftKey) this.moveWithKeyboard(column, yon);
        else this.resizeWithKeyboard(column, yon);
      }
      return;
    }

    const newItem = navigateCell(location, evt, this.gridSize());
    if (newItem !== null) {
      evt.preventDefault();
      this.moveFocus(newItem);
      return;
    }

    if (location.satir === 0) return; // başlık: Enter/Space düğmenin kendi davranışı (sıralama)
    const row = this.satirlar()[location.satir - 1];
    if (row === undefined) return;
    const check = isInteractive(target);
    if (evt.key === 'Enter' && !check) {
      evt.preventDefault();
      this.satirAc.emit(row);
    } else if (evt.key === ' ' && this.secilebilir() && !check) {
      evt.preventDefault();
      const identity = this.satirKimligi()(row);
      this.rowSelection(identity, !this.selectedSet().has(identity));
    }
  }

  protected doubleClick(row: T, evt: MouseEvent): void {
    // Hücredeki bağlantı/düğmeye çift tıklama o denetimin işidir (ör. "Tahsil et"), satırı açmaz.
    if (evt.target instanceof Element && isInteractive(evt.target)) return;
    this.satirAc.emit(row);
  }

  private resizeWithKeyboard(column: GorunurSutun<T>, yon: -1 | 1): void {
    const definition = column.tanim;
    if (definition === null) return;
    const px = column.genislik + yon * KEYBOARD_WIDTH_STEP;
    this.changeLayout(setWidth(this.sutunlar(), this.layout(), definition.kod, px));
    const newItem = this.layout().sutunlar.find((d) => d.kod === definition.kod)?.genislik ?? px;
    this.announcement.set(
      this.ceviri('tablo.duyuru.genislik', { baslik: definition.baslik, px: newItem }),
    );
  }

  private moveWithKeyboard(column: GorunurSutun<T>, yon: -1 | 1): void {
    const definition = column.tanim;
    if (definition === null || definition.sabit) return;
    // Görünür komşunun ötesine geç (gizli sütunlar arada kalabilir).
    let layout = this.layout();
    const visibleCodes = () => layout.sutunlar.filter((d) => d.gorunur).map((d) => d.kod);
    const previous = visibleCodes().indexOf(definition.kod);
    for (let step = layout.sutunlar.length; step > 0; step--) {
      const next = scrollColumn(this.sutunlar(), layout, definition.kod, yon);
      if (next === layout) break;
      layout = next;
      if (visibleCodes().indexOf(definition.kod) !== previous) break;
    }
    this.changeLayout(layout);
    const newOrder = this.visibleColumns().findIndex((s) => s.kod === definition.kod);
    if (newOrder >= 0) this.moveFocus({ satir: 0, sutun: newOrder });
    this.announcement.set(
      this.ceviri('tablo.duyuru.tasindi', { baslik: definition.baslik, sira: newOrder + 1 }),
    );
  }

  private moveFocus(location: HucreKonumu): void {
    this.aktif.set(location);
    if (location.satir > 0 && this.virtualized !== null && this.isVirtual()) {
      const strikethrough = this.render().satirlar.some((c) => c.index === location.satir - 1);
      if (!strikethrough) this.virtualized.scrollToIndex(location.satir - 1, { align: 'auto' });
    }
    this.focusPending = true;
    this.focus();
  }

  private focus(): void {
    const { satir, sutun } = this.aktif();
    const cell = this.kaydirici()?.nativeElement.querySelector<HTMLElement>(
      `[data-hucre="${satir}:${sutun}"]`,
    );
    if (cell === null || cell === undefined) return; // sanal satır henüz çizilmedi; sonraki çizimde denenir
    this.focusPending = false;
    const target = cell.querySelector<HTMLElement>('[data-odak]') ?? cell;
    if (document.activeElement !== target) target.focus();
  }

  private cellPosition(target: EventTarget | null): HucreKonumu | null {
    if (!(target instanceof HTMLElement)) return null;
    const cell = target.closest<HTMLElement>('[data-hucre]');
    const [row, column] = (cell?.dataset['hucre'] ?? '').split(':').map(Number);
    return Number.isInteger(row) && Number.isInteger(column) ? { satir: row, sutun: column } : null;
  }

  // ------------------------------------------------------------------ kalıcılık

  private loadLayout(code: string | null): void {
    this.savedKey = layoutKey(this.layout());
    if (code === null) return;
    this.store
      .read(code)
      .pipe(take(1))
      .subscribe({
        next: (saved) => {
          if (saved === null) return;
          const layout = mergeLayout(this.sutunlar(), saved);
          this.layout.set(layout);
          this.savedKey = layoutKey(layout);
          // Kayıtlı sıralama tercihi yalnız sayfa varsayılandayken (URL açık sıralama taşımıyorsa).
          const preference = sortText(this.sutunlar(), layout.siralama[0] ?? null);
          if (
            preference !== null &&
            this.sirala() === this.varsayilanSirala() &&
            preference !== this.sirala()
          ) {
            this.siralaDegisti.emit(preference);
          }
        },
        // Okunamayan düzen tabloyu durdurmaz: varsayılanla devam.
        error: () => undefined,
      });
  }

  private changeLayout(newItem: TabloDuzeni): void {
    if (newItem === this.layout()) return;
    this.layout.set(newItem);
    if (this.tabloKodu() === null) return;
    this.stopTimer();
    this.saveTimer = setTimeout(() => this.submitPendingRecord(), SAVE_DELAY_MS);
  }

  private submitPendingRecord(): void {
    this.stopTimer();
    const code = this.tabloKodu();
    const layout = this.layout();
    const key = layoutKey(layout);
    if (code === null || key === this.savedKey) return;
    // Boyutlama sürerken yazma; bırakınca yazılır.
    if (this.sizing().isResizingColumn !== false) {
      this.saveTimer = setTimeout(() => this.submitPendingRecord(), SAVE_DELAY_MS);
      return;
    }
    this.savedKey = key;
    this.store
      .kaydet(code, layout)
      .pipe(take(1))
      .subscribe({
        next: () => this.saveError.set(false),
        error: () => {
          this.savedKey = null;
          this.saveError.set(true);
        },
      });
  }

  private stopTimer(): void {
    if (this.saveTimer !== null) clearTimeout(this.saveTimer);
    this.saveTimer = null;
  }

  // ------------------------------------------------------------------ iç yardımcılar

  private virtualOptions(count: number, active: boolean, rowPx: number, headerPx: number) {
    return {
      count: count,
      enabled: active,
      getScrollElement: () => this.kaydirici()?.nativeElement ?? null,
      estimateSize: () => rowPx,
      scrollMargin: headerPx,
      scrollPaddingStart: headerPx,
      overscan: 10,
      getItemKey: (i: number) => {
        const row = this.satirlar()[i];
        return row === undefined ? i : this.satirKimligi()(row);
      },
      scrollToFn: elementScroll,
      observeElementRect,
      observeElementOffset,
      onChange: () => this.virtualVersion.update((n) => n + 1),
    };
  }

  private cssPx(el: HTMLElement, name: string, defaultValue: number): number {
    const value = getComputedStyle(el).getPropertyValue(name).trim();
    const count = parseFloat(value);
    if (!Number.isFinite(count)) return defaultValue;
    if (value.endsWith('rem')) {
      return count * (parseFloat(getComputedStyle(document.documentElement).fontSize) || 16);
    }
    return value.endsWith('px') ? count : defaultValue;
  }
}

/**
 * Hücre içindeki etkileşimli öğe (bağlantı, düğme, form denetimi): Enter/Boşluk/çift tıklama onun kendi
 * davranışıdır — satırı açmaz/seçmez (bağlantıda Enter'ı yutmak yeni sekmede PDF'i açtırmazdı).
 */
function isInteractive(target: Element): boolean {
  return target.closest('a[href], button, input, select, textarea, label') !== null;
}
