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
import type { KiraDetayYaniti } from '../kira-tipleri';
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
    @if (!yuklendi()) {
      <section class="rc-bolum kf-kart" aria-busy="true">
        <h2 class="kf-kart__baslik">{{ 'kiraFinans.baslik' | transloco }}</h2>
      </section>
    }
  `,
})
export class KiraFinansYuvasi {
  readonly detay = input<KiraDetayYaniti | null>(null);
  readonly tazelemeHatasi = input(false);
  readonly degisti = output<void>();

  private readonly vcr = inject(ViewContainerRef);
  protected readonly yuklendi = signal(false);
  private panel: ComponentRef<KiraFinansPaneli> | null = null;

  /** Sayfa terk koruması: paneldeki yazılmış değer ya da sonuçlanmamış (donmuş anahtarlı) gönderim. */
  kirliMi(): boolean {
    return this.panel?.instance.kirliMi() ?? false;
  }

  /** Sonucu bilinmeyen para gönderimi (terk sorusu özel metinle — 3. tur). */
  sonucuBilinmeyenVar(): boolean {
    return this.panel?.instance.sonucuBilinmeyenVar() ?? false;
  }

  constructor() {
    let iptal = false;
    inject(DestroyRef).onDestroy(() => (iptal = true));
    void import('./kira-finans-paneli').then(({ KiraFinansPaneli }) => {
      if (iptal) return;
      const ref = this.vcr.createComponent(KiraFinansPaneli);
      ref.setInput('detay', this.detay());
      ref.setInput('tazelemeHatasi', this.tazelemeHatasi());
      ref.instance.degisti.subscribe(() => this.degisti.emit());
      this.panel = ref;
      this.yuklendi.set(true);
    });
    effect(() => {
      const d = this.detay();
      if (this.yuklendi()) this.panel?.setInput('detay', d);
    });
    effect(() => {
      const hata = this.tazelemeHatasi();
      if (this.yuklendi()) this.panel?.setInput('tazelemeHatasi', hata);
    });
  }
}
