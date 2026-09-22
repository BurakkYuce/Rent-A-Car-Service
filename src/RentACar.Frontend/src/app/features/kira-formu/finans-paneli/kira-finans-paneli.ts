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
import type { KiraDetayYaniti } from '../kira-tipleri';
import { FinansDepozito } from './finans-depozito';
import { FinansDisHizmet } from './finans-dis-hizmet';
import { FinansDonem } from './finans-donem';
import { FinansFaturalar } from './finans-faturalar';
import { FinansCezalar, FinansKurlar } from './finans-listeler';
import { FinansOdeme } from './finans-odeme';
import { FinansTahsilat } from './finans-tahsilat';
import { KiraFinansDurumu } from './kira-finans-durumu';

/** Alt sekmeler — Blazor sabit paneli: Nakit, Kredi Kart/Havale, Faturalar, Fatura Dönemleri, Dış Hizmet, Kur, Ceza/HGS. */
export const FINANS_SEKMELERI = [
  'nakit',
  'kart',
  'faturalar',
  'donem',
  'dishizmet',
  'kurlar',
  'ceza',
] as const;
export type FinansSekmesi = (typeof FINANS_SEKMELERI)[number];

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
  providers: [KiraFinansDurumu],
  imports: [
    TranslocoPipe,
    FinansTahsilat,
    FinansOdeme,
    FinansDepozito,
    FinansFaturalar,
    FinansDonem,
    FinansDisHizmet,
    FinansKurlar,
    FinansCezalar,
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
    .kf-finans .kf-finans__satir {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2);
      align-items: center;
    }
  `,
  template: `
    <section class="kf-kart" aria-labelledby="kf-finans-baslik" data-testid="finans-paneli">
      <h2 class="kf-kart__baslik" id="kf-finans-baslik">{{ 'kiraFinans.baslik' | transloco }}</h2>
      @if (f.kira()) {
        <div
          class="kf-alt-sekmeler"
          role="tablist"
          [attr.aria-label]="'kiraFinans.bolumler' | transloco"
        >
          @for (s of sekmeler; track s) {
            <button
              type="button"
              role="tab"
              class="kf-alt-sekme"
              [id]="'kf-fin-' + s"
              [attr.aria-selected]="aktif() === s"
              [attr.aria-controls]="aktif() === s ? 'kf-fin-panel-' + s : null"
              [tabindex]="aktif() === s ? 0 : -1"
              (click)="sec(s)"
              (keydown)="tus($event)"
            >
              {{ etiket(s) | transloco }}
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
  readonly detay = input<KiraDetayYaniti | null>(null);
  /** Finans işlemi sonuçlandı → sayfa kaydı yeniden yükler. */
  readonly degisti = output<void>();

  protected readonly f = inject(KiraFinansDurumu);
  private readonly belge = inject(DOCUMENT);
  protected readonly sekmeler = FINANS_SEKMELERI;
  protected readonly aktif = signal<FinansSekmesi>('nakit');

  constructor() {
    this.f.degisti = () => this.degisti.emit();
    effect(() => {
      const d = this.detay();
      untracked(() => {
        this.f.detayAyarla(d);
        this.f.sekmeAcildi(this.aktif());
      });
    });
  }

  protected etiket(s: FinansSekmesi): CeviriAnahtari {
    return `kiraFinans.sekme.${s}`;
  }

  protected sec(s: FinansSekmesi): void {
    this.aktif.set(s);
    this.f.sekmeAcildi(s);
  }

  /** APG sekme klavyesi: ←/→ dolaşır, Home/End uçlar. */
  protected tus(olay: KeyboardEvent): void {
    const n = FINANS_SEKMELERI.length;
    const sira = FINANS_SEKMELERI.indexOf(this.aktif());
    const hedef =
      olay.key === 'ArrowRight'
        ? (sira + 1) % n
        : olay.key === 'ArrowLeft'
          ? (sira - 1 + n) % n
          : olay.key === 'Home'
            ? 0
            : olay.key === 'End'
              ? n - 1
              : null;
    const s = hedef === null ? undefined : FINANS_SEKMELERI[hedef];
    if (s === undefined) return;
    olay.preventDefault();
    this.sec(s);
    this.belge.getElementById(`kf-fin-${s}`)?.focus();
  }
}
