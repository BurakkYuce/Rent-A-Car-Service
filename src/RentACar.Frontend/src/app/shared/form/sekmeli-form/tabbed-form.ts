import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { uniqueId } from '../alan/alan-baglami';

export interface SekmeTanimi {
  readonly kimlik: string;
  readonly etiket: string;
}

/** URL parçası: `#sekme=odeme` (Blazor mega-formuyla aynı derin bağlantı). */
const TAB_FRAGMENT = /(?:^#|&)sekme=([^&]+)/;

/**
 * Tam sayfa sekmeli form yerleşimi + sabit yan panel (F4 kira formu bunu kullanır).
 *
 * - Sekmeler `role="tablist"`; ←/→/Home/End ile gezilir. Etkin sekme URL'de `#sekme=<kimlik>`
 *   (derin bağlantı, yenilemede korunur). Adres `pathname + search + '#…'` ile yazılır — çıplak `#`
 *   `<base href>` altında köke çözülür (Blazor dersi).
 * - Paneller (`rcSekmePaneli`) gizlenir ama DOM'da kalır: kontroller ve değerleri kaybolmaz.
 * - `ilkGecersizeGit()`: gönderimde geçersiz alan gizli sekmedeyse o sekmeye geçer ve alana odaklanır;
 *   hatalı sekmeler işaretlenir.
 * - Yan panel `rcYanPanel` özniteliğiyle verilir; geniş ekranda sabit (sticky), dar ekranda alta iner.
 */
@Component({
  selector: 'rc-sekmeli-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  styleUrl: './tabbed-form.scss',
  host: {
    class: 'rc-sekmeli-form',
    '(window:hashchange)': 'readFromUrl()',
    '(focusout)': 'scanErrorsLater()',
  },
  template: `
    <div class="ana">
      <div class="sekmeler" role="tablist" [attr.aria-label]="etiket() || null">
        @for (s of sekmeler(); track s.kimlik) {
          <button
            type="button"
            role="tab"
            class="sekme"
            [id]="tabId(s.kimlik)"
            [attr.aria-selected]="aktif() === s.kimlik"
            [attr.aria-controls]="panelId(s.kimlik)"
            [tabindex]="aktif() === s.kimlik ? 0 : -1"
            (click)="select(s.kimlik)"
            (keydown)="tabKey($event)"
          >
            {{ s.etiket }}
            @if (invalidTabs().has(s.kimlik)) {
              <span class="sekme__hata" aria-hidden="true"></span>
              <span class="rc-gorunmez">({{ 'form.sekme.hataVar' | transloco }})</span>
            }
          </button>
        }
      </div>
      <ng-content />
    </div>
    <aside class="rc-sekmeli-form__yan yan">
      <ng-content select="[rcYanPanel]" />
    </aside>
  `,
})
export class TabbedForm {
  readonly sekmeler = input.required<readonly SekmeTanimi[]>();
  /** Sekme listesinin erişilebilir adı. */
  readonly etiket = input('');

  private readonly belge = inject(DOCUMENT);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly onek = uniqueId('rc-sekme');

  private readonly selected = signal<string | null>(this.tabInUrl());
  readonly aktif = computed(() => {
    const selected = this.selected();
    const list = this.sekmeler();
    return list.some((s) => s.kimlik === selected) ? selected : (list[0]?.kimlik ?? null);
  });
  readonly invalidTabs = signal<ReadonlySet<string>>(new Set());

  tabId(identity: string): string {
    return `${this.onek}-${identity}`;
  }

  panelId(identity: string): string {
    return `${this.onek}-${identity}-panel`;
  }

  select(identity: string, { adreseYaz: writeToUrl = true }: { adreseYaz?: boolean } = {}): void {
    this.selected.set(identity);
    if (writeToUrl) this.writeToUrl(identity);
  }

  /**
   * Gönderim geçersizse çağrılır (ya da sunucu alan hatası geldiğinde): çizimden sonra ilk
   * `aria-invalid="true"` öğeyi bulur, gizli sekmedeyse o sekmeye geçer, odaklar.
   */
  goToFirstInvalid(): void {
    afterNextRender(
      () => {
        this.scanErrors();
        const first =
          this.element.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        if (!first) return;
        const panel = first.closest<HTMLElement>('[data-rc-sekme]');
        const identity = panel?.dataset['rcSekme'];
        if (identity && identity !== this.aktif()) this.select(identity);
        afterNextRender(() => focusable(first)?.focus(), { injector: this.injector });
      },
      { injector: this.injector },
    );
  }

  protected scanErrorsLater(): void {
    afterNextRender(() => this.scanErrors(), { injector: this.injector });
  }

  protected readFromUrl(): void {
    const identity = this.tabInUrl();
    if (identity) this.select(identity, { adreseYaz: false });
  }

  protected tabKey(evt: KeyboardEvent): void {
    const list = this.sekmeler();
    const order = list.findIndex((s) => s.kimlik === this.aktif());
    const target = ((): number | null => {
      switch (evt.key) {
        case 'ArrowRight':
          return (order + 1) % list.length;
        case 'ArrowLeft':
          return (order - 1 + list.length) % list.length;
        case 'Home':
          return 0;
        case 'End':
          return list.length - 1;
        default:
          return null;
      }
    })();
    const identity = target === null ? undefined : list[target]?.kimlik;
    if (identity === undefined) return;
    evt.preventDefault();
    this.select(identity);
    this.belge.getElementById(this.tabId(identity))?.focus();
  }

  private scanErrors(): void {
    const invalid = new Set<string>();
    for (const el of this.element.nativeElement.querySelectorAll<HTMLElement>(
      '[aria-invalid="true"]',
    )) {
      const identity = el.closest<HTMLElement>('[data-rc-sekme]')?.dataset['rcSekme'];
      if (identity) invalid.add(identity);
    }
    const previous = this.invalidTabs();
    if (previous.size !== invalid.size || [...invalid].some((k) => !previous.has(k))) {
      this.invalidTabs.set(invalid);
    }
  }

  private tabInUrl(): string | null {
    const match = TAB_FRAGMENT.exec(this.belge.location?.hash ?? '');
    return match?.[1] ? decodeURIComponent(match[1]) : null;
  }

  private writeToUrl(identity: string): void {
    const location = this.belge.location;
    const window = this.belge.defaultView;
    if (!location || !window) return;
    window.history.replaceState(
      window.history.state,
      '',
      `${location.pathname}${location.search}#sekme=${encodeURIComponent(identity)}`,
    );
  }
}

/**
 * Sekme paneli: `<section rcSekmePaneli="odeme" baslik="Ödeme">`. Gizliyken `hidden` (DOM'da kalır),
 * yazdırmada başlığıyla basılır.
 */
@Directive({
  selector: '[rcSekmePaneli]',
  host: {
    class: 'rc-sekme-paneli',
    role: 'tabpanel',
    '[id]': 'form.panelId(rcSekmePaneli())',
    '[attr.aria-labelledby]': 'form.tabId(rcSekmePaneli())',
    '[attr.data-rc-sekme]': 'rcSekmePaneli()',
    '[attr.data-baslik]': 'baslik()',
    '[hidden]': 'form.aktif() !== rcSekmePaneli()',
  },
})
export class TabPanel {
  readonly rcSekmePaneli = input.required<string>();
  readonly baslik = input('');
  protected readonly form = inject(TabbedForm);
}

function focusable(el: HTMLElement): HTMLElement | null {
  const picker =
    'input:not([disabled]),select:not([disabled]),textarea:not([disabled]),button:not([disabled]),[tabindex]';
  return el.matches(picker) ? el : el.querySelector<HTMLElement>(picker);
}
