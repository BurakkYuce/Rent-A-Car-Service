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
import { tekilKimlik } from '../alan/alan-baglami';

export interface SekmeTanimi {
  readonly kimlik: string;
  readonly etiket: string;
}

/** URL parçası: `#sekme=odeme` (Blazor mega-formuyla aynı derin bağlantı). */
const SEKME_PARCASI = /(?:^#|&)sekme=([^&]+)/;

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
  styleUrl: './sekmeli-form.scss',
  host: {
    class: 'rc-sekmeli-form',
    '(window:hashchange)': 'adrestenOku()',
    '(focusout)': 'hatalariTaraSonra()',
  },
  template: `
    <div class="ana">
      <div class="sekmeler" role="tablist" [attr.aria-label]="etiket() || null">
        @for (s of sekmeler(); track s.kimlik) {
          <button
            type="button"
            role="tab"
            class="sekme"
            [id]="sekmeKimligi(s.kimlik)"
            [attr.aria-selected]="aktif() === s.kimlik"
            [attr.aria-controls]="panelKimligi(s.kimlik)"
            [tabindex]="aktif() === s.kimlik ? 0 : -1"
            (click)="sec(s.kimlik)"
            (keydown)="sekmeTusu($event)"
          >
            {{ s.etiket }}
            @if (hataliSekmeler().has(s.kimlik)) {
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
export class SekmeliForm {
  readonly sekmeler = input.required<readonly SekmeTanimi[]>();
  /** Sekme listesinin erişilebilir adı. */
  readonly etiket = input('');

  private readonly belge = inject(DOCUMENT);
  private readonly eleman = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly onek = tekilKimlik('rc-sekme');

  private readonly secilen = signal<string | null>(this.adrestekiSekme());
  readonly aktif = computed(() => {
    const secilen = this.secilen();
    const liste = this.sekmeler();
    return liste.some((s) => s.kimlik === secilen) ? secilen : (liste[0]?.kimlik ?? null);
  });
  readonly hataliSekmeler = signal<ReadonlySet<string>>(new Set());

  sekmeKimligi(kimlik: string): string {
    return `${this.onek}-${kimlik}`;
  }

  panelKimligi(kimlik: string): string {
    return `${this.onek}-${kimlik}-panel`;
  }

  sec(kimlik: string, { adreseYaz = true }: { adreseYaz?: boolean } = {}): void {
    this.secilen.set(kimlik);
    if (adreseYaz) this.adreseYaz(kimlik);
  }

  /**
   * Gönderim geçersizse çağrılır (ya da sunucu alan hatası geldiğinde): çizimden sonra ilk
   * `aria-invalid="true"` öğeyi bulur, gizli sekmedeyse o sekmeye geçer, odaklar.
   */
  ilkGecersizeGit(): void {
    afterNextRender(
      () => {
        this.hatalariTara();
        const ilk = this.eleman.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]');
        if (!ilk) return;
        const panel = ilk.closest<HTMLElement>('[data-rc-sekme]');
        const kimlik = panel?.dataset['rcSekme'];
        if (kimlik && kimlik !== this.aktif()) this.sec(kimlik);
        afterNextRender(() => odaklanilabilir(ilk)?.focus(), { injector: this.injector });
      },
      { injector: this.injector },
    );
  }

  protected hatalariTaraSonra(): void {
    afterNextRender(() => this.hatalariTara(), { injector: this.injector });
  }

  protected adrestenOku(): void {
    const kimlik = this.adrestekiSekme();
    if (kimlik) this.sec(kimlik, { adreseYaz: false });
  }

  protected sekmeTusu(olay: KeyboardEvent): void {
    const liste = this.sekmeler();
    const sira = liste.findIndex((s) => s.kimlik === this.aktif());
    const hedef = ((): number | null => {
      switch (olay.key) {
        case 'ArrowRight':
          return (sira + 1) % liste.length;
        case 'ArrowLeft':
          return (sira - 1 + liste.length) % liste.length;
        case 'Home':
          return 0;
        case 'End':
          return liste.length - 1;
        default:
          return null;
      }
    })();
    const kimlik = hedef === null ? undefined : liste[hedef]?.kimlik;
    if (kimlik === undefined) return;
    olay.preventDefault();
    this.sec(kimlik);
    this.belge.getElementById(this.sekmeKimligi(kimlik))?.focus();
  }

  private hatalariTara(): void {
    const hatali = new Set<string>();
    for (const el of this.eleman.nativeElement.querySelectorAll<HTMLElement>(
      '[aria-invalid="true"]',
    )) {
      const kimlik = el.closest<HTMLElement>('[data-rc-sekme]')?.dataset['rcSekme'];
      if (kimlik) hatali.add(kimlik);
    }
    const onceki = this.hataliSekmeler();
    if (onceki.size !== hatali.size || [...hatali].some((k) => !onceki.has(k))) {
      this.hataliSekmeler.set(hatali);
    }
  }

  private adrestekiSekme(): string | null {
    const eslesme = SEKME_PARCASI.exec(this.belge.location?.hash ?? '');
    return eslesme?.[1] ? decodeURIComponent(eslesme[1]) : null;
  }

  private adreseYaz(kimlik: string): void {
    const konum = this.belge.location;
    const pencere = this.belge.defaultView;
    if (!konum || !pencere) return;
    pencere.history.replaceState(
      pencere.history.state,
      '',
      `${konum.pathname}${konum.search}#sekme=${encodeURIComponent(kimlik)}`,
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
    '[id]': 'form.panelKimligi(rcSekmePaneli())',
    '[attr.aria-labelledby]': 'form.sekmeKimligi(rcSekmePaneli())',
    '[attr.data-rc-sekme]': 'rcSekmePaneli()',
    '[attr.data-baslik]': 'baslik()',
    '[hidden]': 'form.aktif() !== rcSekmePaneli()',
  },
})
export class SekmePaneli {
  readonly rcSekmePaneli = input.required<string>();
  readonly baslik = input('');
  protected readonly form = inject(SekmeliForm);
}

function odaklanilabilir(el: HTMLElement): HTMLElement | null {
  const secici =
    'input:not([disabled]),select:not([disabled]),textarea:not([disabled]),button:not([disabled]),[tabindex]';
  return el.matches(secici) ? el : el.querySelector<HTMLElement>(secici);
}
