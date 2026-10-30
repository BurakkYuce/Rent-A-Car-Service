import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

let nextNo = 0;

/**
 * Katlanır filtre kabı (F3.4). Filtre alanları içerik olarak verilir; kap yalnız aç/kapa ve
 * erişilebilirliği taşır: `aria-expanded` + `aria-controls` düğmesi, düğmeyle etiketlenmiş bölge,
 * kapalıyken `hidden` (ekran okuyucu ve sekme sırası da atlar). Kapalıyken etkin filtre sayısı düğme
 * metninde kalır — filtrelenmiş liste "tüm kayıtlar" sanılmaz.
 *
 * Kap stilsiz; aç/kapa düğmesi F3.1 primitifi (`rc-dugme rc-dugme--kucuk`) — yerel düğme koyu temada
 * tarayıcı rengini alıp kontrast kapısını geçmiyordu (F4.2 axe).
 *
 * ```html
 * <rc-katlanir-filtre [etkinSayisi]="liste.etkinFiltreSayisi()" [(acik)]="filtreAcik">
 *   <label>Plaka <input ... /></label>
 * </rc-katlanir-filtre>
 * ```
 */
@Component({
  selector: 'rc-katlanir-filtre',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './collapsible-filter.html',
})
export class CollapsibleFilter {
  readonly baslik = input('Filtreler');
  readonly etkinSayisi = input(0);
  readonly acik = model(false);

  private readonly no = ++nextNo;
  protected readonly buttonId = `rc-katlanir-filtre-${this.no}-dugme`;
  protected readonly panelId = `rc-katlanir-filtre-${this.no}-panel`;

  protected degistir(): void {
    this.acik.update((open) => !open);
  }
}
