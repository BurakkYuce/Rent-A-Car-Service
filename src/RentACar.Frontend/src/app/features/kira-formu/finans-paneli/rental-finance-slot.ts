import {
  ChangeDetectionStrategy,
  Component,
  type ComponentRef,
  DestroyRef,
  ViewContainerRef,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import type { RentalDetailResponse } from '../kira-tipleri';
import type { KiraFinansPaneli } from './kira-finans-paneli';

/**
 * Finans panelinin TEMBEL yuvası: panel (`kira-finans-paneli`, ~44 kB) ayrı parça olarak dinamik
 * `import()` ile yüklenir; kira formunun ilk çizimine girmez. `@defer` bilinçli olarak kullanılmadı:
 * defer çalışma zamanı `@angular/core` ile ilk pakete ~7 kB ekliyordu (ölçüldü), bu yuva eklemiyor.
 * Girdi/çıktı panelle birebir (`detay` → `setInput`, `degisti` → yeniden yayılır).
 */
@Component({
  selector: 'rc-kira-finans-yuvasi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    @if (!loaded()) {
      <section class="rc-bolum kf-kart" aria-busy="true">
        <h2 class="kf-kart__baslik">{{ 'kiraFinans.baslik' | transloco }}</h2>
      </section>
    }
  `,
})
export class RentalFinanceSlot {
  readonly detay = input<RentalDetailResponse | null>(null);
  readonly tazelemeHatasi = input(false);
  readonly degisti = output<void>();

  private readonly vcr = inject(ViewContainerRef);
  protected readonly loaded = signal(false);
  private panel: ComponentRef<KiraFinansPaneli> | null = null;

  /** Sayfa terk koruması: paneldeki yazılmış değer ya da sonuçlanmamış (donmuş anahtarlı) gönderim. */
  isDirty(): boolean {
    return this.panel?.instance.isDirty() ?? false;
  }

  /** Sonucu bilinmeyen para gönderimi (terk sorusu özel metinle — 3. tur). */
  hasUnknownOutcome(): boolean {
    return this.panel?.instance.hasUnknownOutcome() ?? false;
  }

  constructor() {
    let cancel = false;
    inject(DestroyRef).onDestroy(() => (cancel = true));
    void import('./kira-finans-paneli').then(({ KiraFinansPaneli: RentalFinancePanel }) => {
      if (cancel) return;
      const ref = this.vcr.createComponent(RentalFinancePanel);
      ref.setInput('detay', this.detay());
      ref.setInput('tazelemeHatasi', this.tazelemeHatasi());
      ref.instance.degisti.subscribe(() => this.degisti.emit());
      this.panel = ref;
      this.loaded.set(true);
    });
    effect(() => {
      const d = this.detay();
      if (this.loaded()) this.panel?.setInput('detay', d);
    });
    effect(() => {
      const error = this.tazelemeHatasi();
      if (this.loaded()) this.panel?.setInput('tazelemeHatasi', error);
    });
  }
}
