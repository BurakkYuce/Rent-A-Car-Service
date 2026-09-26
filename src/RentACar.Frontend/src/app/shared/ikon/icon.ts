import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  PendingTasks,
  signal,
  ViewEncapsulation,
} from '@angular/core';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import type { IkonAdi } from './ikon-kaydi';

/**
 * SVG kaydı TEMBEL parça (F3.7 paket bütçesi): ~10 kB'lık `ikon-kaydi.ts` ilk pakete girmez, ilk
 * `<rc-ikon>` çizilince bir kez yüklenir. O ana kadar ikon kutusu boyutunu korur (kayma yok), boş kalır.
 * Yükleme `PendingTasks`'e kayıtlı: `whenStable()` (birim test) SVG'yi bekler. Parça yüklenemezse
 * (yayın arası) bir sonraki ikon yeniden dener.
 */
const record = signal<Readonly<Record<IkonAdi, string>> | null>(null);
let loading: Promise<void> | null = null;

function loadRegistry(): Promise<void> {
  loading ??= import('./ikon-kaydi').then(
    (m) => record.set(m.IKONLAR),
    () => {
      loading = null;
    },
  );
  return loading;
}

/** Kanonik ikon boyutları (px). Yoğun arayüzde varsayılan 16. */
export const ICON_SIZE = { yogun: 12, kucuk: 14, normal: 16, orta: 20, buyuk: 24 } as const;

/**
 * Tabler ikonu (Revlo `app-icon`'dan). SVG, derleme anında üretilen `ikon-kaydi.ts`'ten gelir;
 * kullanıcı girdisi değil, bu yüzden sanitizer atlaması güvenli. Renk `currentColor`.
 * `etiket` verilmezse süs sayılır ve ekran okuyucudan gizlenir.
 */
@Component({
  selector: 'rc-ikon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<span class="ikon" [innerHTML]="svg()"></span>',
  // SVG innerHTML ile geldiği için kapsüllenmiş stil ona ulaşmaz; seçiciler `rc-ikon` ile sınırlı.
  encapsulation: ViewEncapsulation.None,
  styles: `
    rc-ikon {
      display: inline-flex;
      flex-shrink: 0;
      line-height: 0;
      color: inherit;
    }
    rc-ikon.don {
      animation: rc-ikon-don 0.8s linear infinite;
    }
    rc-ikon > .ikon,
    rc-ikon > .ikon > svg {
      display: block;
      width: 100%;
      height: 100%;
    }
    @keyframes rc-ikon-don {
      to {
        transform: rotate(360deg);
      }
    }
    @media (prefers-reduced-motion: reduce) {
      rc-ikon.don {
        animation: none;
      }
    }
  `,
  host: {
    '[style.width]': 'sizeCss()',
    '[style.height]': 'sizeCss()',
    '[class.don]': 'don()',
    '[attr.role]': "etiket() ? 'img' : null",
    '[attr.aria-label]': 'etiket() || null',
    '[attr.aria-hidden]': "etiket() ? null : 'true'",
  },
})
export class Icon {
  private readonly sanitizer = inject(DomSanitizer);

  readonly ad = input.required<IkonAdi>();
  readonly boyut = input<number | string>(ICON_SIZE.normal);
  readonly etiket = input<string>('');
  readonly don = input(false);

  protected readonly sizeCss = computed(() => {
    const value = this.boyut();
    return typeof value === 'number' ? `${value}px` : value;
  });

  protected readonly svg = computed((): SafeHtml | '' => {
    const icons = record();
    return icons ? this.sanitizer.bypassSecurityTrustHtml(icons[this.ad()]) : '';
  });

  constructor() {
    if (record() === null) void loadRegistry().finally(inject(PendingTasks).add());
  }
}
