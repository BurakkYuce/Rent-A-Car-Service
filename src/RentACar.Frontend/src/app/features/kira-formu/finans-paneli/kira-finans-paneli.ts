import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ViewEncapsulation,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { RentalDetailResponse } from '../kira-tipleri';
import { FinanceDeposit } from './finance-deposit';
import { FinanceOutsourcedService } from './finance-outsourced-service';
import { FinancePeriod } from './finance-period';
import { FinanceInvoices } from './finance-invoices';
import { FinancePenalties, FinanceRates } from './finans-listeler';
import { FinancePayment } from './finance-payment';
import { FinanceCollection } from './finance-collection';
import { displayMoney } from './finans-modeli';
import { RentalFinanceState } from './rental-finance-state';

/** Alt sekmeler — Blazor sabit paneli: Nakit, Kredi Kart/Havale, Faturalar, Fatura Dönemleri, Dış Hizmet, Kur, Ceza/HGS. */
export const FINANCE_TABS = [
  'nakit',
  'kart',
  'faturalar',
  'donem',
  'dishizmet',
  'kurlar',
  'ceza',
] as const;
export type FinanceTab = (typeof FINANCE_TABS)[number];

/**
 * Sabit yan paneldeki FİNANS işlemleri (F4.4; Blazor `StickyPanel.razor` paritesi). Kira formunun tembel
 * parçası (`@defer`) — ilk pakete ve kira formunun ilk çizimine girmez.
 *
 * Sözleşme (F4.3'te sabitlendi, değişmedi):
 * - Girdi `detay`: kayıtlı kiranın `GET /kiralar/{id}` yanıtı (`yetkiler.finans` düğme durumları; asıl kapı
 *   sunucuda; `tahsilat` deterministik anahtar satırı). Yeni kirada `null` → "önce kaydedin".
 * - Çıktı `degisti`: finans işlemi 2xx döndü (ya da `mukerrer` → kayıt değişmiş) → sayfa kaydı yeniden
 *   yükler. Ana formun alanlarına dokunulmaz. ZORUNLU: para işlemleri kiranın sürümünü (`kira.surum`,
 *   Tahsilat/Bakiye…) değiştirir; tazelenmezse ana formun sonraki Kaydet'i bayat sürümle 409 `cakisma` alırdı
 *   (e2e `kira-finans.spec.ts` "panel işlemi sonrası kira sürümü tazelenir" kilitler).
 * - Panel ana kira formunun İÇİNDE çizilir ama `<form>` AÇMAZ (`[formGroup]` + `type="button"`).
 * - Durum ve eylemler `KiraFinansDurumu`'nda (panelin `providers`'ı): sekme değişince yazılanlar kaybolmaz.
 */
@Component({
  selector: 'rc-kira-finans-paneli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  providers: [RentalFinanceState],
  imports: [
    TranslocoPipe,
    FinanceCollection,
    FinancePayment,
    FinanceDeposit,
    FinanceInvoices,
    FinancePeriod,
    FinanceOutsourcedService,
    FinanceRates,
    FinancePenalties,
  ],
  host: { class: 'kf-finans' },
  styles: `
    .kf-finans .kf-alt-sekmeler {
      flex-wrap: wrap;
      margin-bottom: var(--rc-bosluk-2);
    }
    .kf-finans .kf-finans__panel {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-3);
      min-width: 0;
    }
    .kf-finans .kf-finans__islem {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-2);
      min-width: 0;
      padding-top: var(--rc-bosluk-2);
      border-top: 1px solid var(--rc-kenar);
    }
    .kf-finans .kf-finans__baslik {
      margin: 0;
      font-size: var(--rc-yazi-sm);
      font-weight: var(--rc-agirlik-kalin);
    }
    .kf-finans .kf-finans__ust {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
      align-items: center;
      justify-content: space-between;
    }
    .kf-finans .kf-finans__satir {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
      align-items: center;
    }
  `,
  template: `
    <section
      class="rc-bolum rc-bolum--katman kf-kart"
      aria-labelledby="kf-finans-baslik"
      data-testid="finans-paneli"
    >
      <div class="kf-finans__ust">
        <h2 class="kf-kart__baslik" id="kf-finans-baslik">{{ 'kiraFinans.baslik' | transloco }}</h2>
        @if (f.kira(); as k) {
          <span class="rc-rozet" data-testid="finans-kalan">{{
            'kiraFinans.kalan' | transloco: { tutar: money(k.bakiye, k.doviz) }
          }}</span>
        }
      </div>
      @if (f.kira()) {
        <div
          class="kf-alt-sekmeler"
          role="tablist"
          [attr.aria-label]="'kiraFinans.bolumler' | transloco"
        >
          @for (s of tabs; track s) {
            <button
              type="button"
              role="tab"
              class="kf-alt-sekme"
              [id]="'kf-fin-' + s"
              [attr.aria-selected]="aktif() === s"
              [attr.aria-controls]="aktif() === s ? 'kf-fin-panel-' + s : null"
              [tabindex]="aktif() === s ? 0 : -1"
              (click)="select(s)"
              (keydown)="tus($event)"
            >
              {{ label(s) | transloco }}
            </button>
          }
        </div>
        <div
          class="kf-finans__panel"
          role="tabpanel"
          [id]="'kf-fin-panel-' + aktif()"
          [attr.aria-labelledby]="'kf-fin-' + aktif()"
        >
          @switch (aktif()) {
            @case ('nakit') {
              <rc-kf-finans-tahsilat [tf]="f.nakit" />
              <rc-kf-finans-depozito />
            }
            @case ('kart') {
              <rc-kf-finans-tahsilat [tf]="f.kart" />
              <rc-kf-finans-odeme />
            }
            @case ('faturalar') {
              <rc-kf-finans-faturalar />
            }
            @case ('donem') {
              <rc-kf-finans-donem />
            }
            @case ('dishizmet') {
              <rc-kf-finans-dis-hizmet />
            }
            @case ('kurlar') {
              <rc-kf-finans-kurlar />
            }
            @case ('ceza') {
              <rc-kf-finans-cezalar />
            }
          }
        </div>
      } @else {
        <p class="kf-not">{{ 'kiraFinans.onceKaydet' | transloco }}</p>
      }
    </section>
  `,
})
export class KiraFinansPaneli {
  readonly detay = input<RentalDetailResponse | null>(null);
  /** Sayfanın kira tazelemesi belirsiz hatayla (5xx/ağ) bitti; `detay` son iyi okumadır (#318 L1). */
  readonly tazelemeHatasi = input(false);
  /** Finans işlemi sonuçlandı → sayfa kaydı yeniden yükler. */
  readonly degisti = output<void>();

  protected readonly f = inject(RentalFinanceState);
  private readonly belge = inject(DOCUMENT);
  protected readonly tabs = FINANCE_TABS;
  protected readonly aktif = signal<FinanceTab>('nakit');
  protected readonly money = displayMoney;

  constructor() {
    this.f.changed = () => this.degisti.emit();
    effect(() => {
      const d = this.detay();
      untracked(() => {
        this.f.setDetail(d);
        this.f.tabOpened(this.aktif());
      });
    });
    effect(() => {
      const error = this.tazelemeHatasi();
      untracked(() => this.f.setDetailError(error));
    });
  }

  /** Sayfa terk koruması için (yuva üzerinden sayfaya). */
  isDirty(): boolean {
    return this.f.isDirty();
  }

  hasUnknownOutcome(): boolean {
    return this.f.hasUnknownOutcome();
  }

  protected label(s: FinanceTab): CeviriAnahtari {
    return `kiraFinans.sekme.${s}`;
  }

  protected select(s: FinanceTab): void {
    this.aktif.set(s);
    this.f.tabOpened(s);
  }

  /** APG sekme klavyesi: ←/→ dolaşır, Home/End uçlar. */
  protected tus(evt: KeyboardEvent): void {
    const n = FINANCE_TABS.length;
    const order = FINANCE_TABS.indexOf(this.aktif());
    const target =
      evt.key === 'ArrowRight'
        ? (order + 1) % n
        : evt.key === 'ArrowLeft'
          ? (order - 1 + n) % n
          : evt.key === 'Home'
            ? 0
            : evt.key === 'End'
              ? n - 1
              : null;
    const s = target === null ? undefined : FINANCE_TABS[target];
    if (s === undefined) return;
    evt.preventDefault();
    this.select(s);
    this.belge.getElementById(`kf-fin-${s}`)?.focus();
  }
}
