import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import type { KiraDetayYaniti } from '../kira-tipleri';

/**
 * Sabit yan paneldeki FİNANS YUVASI — F4.3'te yalnız iskelet. F4.4 (ayrı PR) buraya tahsilat, ödeme,
 * fatura, dönem faturası, dış hizmet (+iptal) ve depozito (al / irat) işlemlerini koyar.
 *
 * Sözleşme (F4.4 bunu korur; sayfa tarafı değişmez):
 * - Girdi `detay`: kayıtlı kiranın `GET /kiralar/{id}` yanıtı (`yetkiler.finans` düğme durumları için;
 *   asıl kapı sunucuda). Yeni kirada `null` → "önce kaydedin".
 * - Çıktı `degisti`: finans işlemi 2xx döndü → sayfa kaydı yeniden yükler (Genel Toplam / Tahsilat /
 *   Bakiye özeti tazelenir). Form alanlarına dokunulmaz.
 * - Panel ana kira formunun İÇİNDE çizilir ama kendi `<form>` öğesini AÇMAZ (sayfada form öğesi yok;
 *   `[formGroup]` + `type="button"` düğme); ana formun kirli durumuna karışmaz.
 * - Deterministik tahsilat anahtarı (`TahsilatAnahtar`) varsa istemci anahtarından önce gelir; istemci
 *   anahtarı her 2xx'ten sonra yenilenir (`formGonderimi` / `GonderimKilidi`).
 */
@Component({
  selector: 'rc-kira-finans-paneli',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe],
  template: `
    <section class="kf-kart kf-finans" [attr.aria-label]="'kiraFormu.finans.baslik' | transloco">
      <h2 class="kf-kart__baslik">{{ 'kiraFormu.finans.baslik' | transloco }}</h2>
      @if (detay(); as d) {
        <p class="kf-not">{{ 'kiraFormu.finans.yakinda' | transloco }}</p>
        <a class="rc-dugme rc-dugme--kucuk" [href]="'/kiralar/' + d.kira.id + '#sekme=fiyat'">{{
          'kiraFormu.finans.mevcutEkran' | transloco
        }}</a>
      } @else {
        <p class="kf-not">{{ 'kiraFormu.finans.onceKaydet' | transloco }}</p>
      }
    </section>
  `,
})
export class KiraFinansPaneli {
  readonly detay = input<KiraDetayYaniti | null>(null);
  /** Finans işlemi başarılı → sayfa kaydı yeniden yükler (F4.4 kullanır). */
  readonly degisti = output<void>();
}
