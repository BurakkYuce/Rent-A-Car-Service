import {
  ChangeDetectionStrategy,
  Component,
  type OnInit,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Icon } from '@shared/ikon/icon';

let nextNo = 0;

/** Yalnız görünüm tercihi (açık/kapalı): kişisel veri değil; erişilemezse (gizli pencere, kota) sessizce varsayılan. */
function readState(key: string): boolean | null {
  try {
    const v = globalThis.localStorage?.getItem(key);
    return v === '1' ? true : v === '0' ? false : null;
  } catch {
    return null;
  }
}

function writeState(key: string, open: boolean): void {
  try {
    globalThis.localStorage?.setItem(key, open ? '1' : '0');
  } catch {
    // Depolama kapalı/dolu: tercih bu oturumda bellekte kalır.
  }
}

/**
 * Filtre paneli (Yol v2 §6/§8): alanlar içerik olarak verilir ve 6 sütunlu `.rc-filtre-izgara`'ya dizilir
 * (≤ 900 px 2, ≤ 600 px 1). Altta `Temizle` (çerçeveli) + `Filtrele` (TEK dolu düğme). Panel bir `<form>`:
 * alanda Enter = Filtrele. Bu yüzden başka bir `<form>` içine konmaz. Katlanır; varsayılan açık, durum
 * `depoAnahtari` verilirse localStorage'da saklanır (`rc.` önekiyle verin → çıkışta temizlenir).
 */
@Component({
  selector: 'rc-filtre-paneli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, TranslocoPipe],
  template: `
    <div class="ust">
      <button
        type="button"
        class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk ac-kapa"
        [id]="buttonId"
        [attr.aria-expanded]="acik()"
        [attr.aria-controls]="panelId"
        (click)="degistir()"
      >
        <rc-ikon [ad]="acik() ? 'chevron-down' : 'chevron-right'" [boyut]="14" />
        {{ baslik() || ('ortak.filtre.baslik' | transloco) }}
        @if (etkinSayisi() > 0) {
          <span class="rc-rozet rc-rozet--vurgu">{{
            'ortak.filtre.etkin' | transloco: { adet: etkinSayisi() }
          }}</span>
        }
      </button>
    </div>
    <form
      class="govde"
      role="region"
      [id]="panelId"
      [attr.aria-labelledby]="buttonId"
      [hidden]="!acik()"
      (submit)="gonder($event)"
    >
      <div class="rc-filtre-izgara"><ng-content /></div>
      <div class="eylemler">
        <button type="button" class="rc-dugme" (click)="temizle.emit()">
          {{ 'ortak.filtre.temizle' | transloco }}
        </button>
        <button type="submit" class="rc-dugme rc-dugme--birincil">
          <rc-ikon ad="filter" [boyut]="14" />
          {{ 'ortak.filtre.filtrele' | transloco }}
        </button>
      </div>
    </form>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      min-width: 0;
      padding: var(--rc-bosluk-2) var(--rc-bosluk-4) var(--rc-bosluk-4);
      border: 1px solid var(--rc-kenar);
      border-radius: var(--rc-yaricap-lg);
      background-color: var(--rc-yuzey);
    }
    .ust {
      display: flex;
      align-items: center;
    }
    .ac-kapa {
      margin-inline-start: calc(-1 * var(--rc-bosluk-2));
      font-weight: var(--rc-agirlik-kalin);
    }
    .govde {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
      margin-top: var(--rc-bosluk-2);
    }
    .govde[hidden] {
      display: none;
    }
    .eylemler {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
      justify-content: flex-end;
    }
  `,
})
export class FilterPanelComponent implements OnInit {
  readonly baslik = input('');
  readonly etkinSayisi = input(0);
  /** localStorage anahtarı (ör. `rc.filtre.kiralar`); verilmezse durum saklanmaz. */
  readonly depoAnahtari = input<string | null>(null);
  readonly temizle = output<void>();
  readonly filtrele = output<void>();

  protected readonly acik = signal(true);
  private readonly no = ++nextNo;
  protected readonly buttonId = `rc-filtre-paneli-${this.no}-dugme`;
  protected readonly panelId = `rc-filtre-paneli-${this.no}-panel`;

  ngOnInit(): void {
    const key = this.depoAnahtari();
    if (key) {
      const saved = readState(key);
      if (saved !== null) this.acik.set(saved);
    }
  }

  protected degistir(): void {
    const open = !this.acik();
    this.acik.set(open);
    const key = this.depoAnahtari();
    if (key) writeState(key, open);
  }

  protected gonder(event: Event): void {
    event.preventDefault();
    this.filtrele.emit();
  }
}
