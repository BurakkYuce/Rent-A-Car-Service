import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

let sonrakiNo = 0;

/**
 * Katlanır filtre kabı (F3.4). Filtre alanları içerik olarak verilir; kap yalnız aç/kapa ve
 * erişilebilirliği taşır: `aria-expanded` + `aria-controls` düğmesi, düğmeyle etiketlenmiş bölge,
 * kapalıyken `hidden` (ekran okuyucu ve sekme sırası da atlar). Kapalıyken etkin filtre sayısı düğme
 * metninde kalır — filtrelenmiş liste "tüm kayıtlar" sanılmaz.
 *
 * Bilinçli olarak STİLSİZ: görünüm F3.1 token'larıyla eklenir.
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
  templateUrl: './katlanir-filtre.html',
})
export class KatlanirFiltre {
  readonly baslik = input('Filtreler');
  readonly etkinSayisi = input(0);
  readonly acik = model(false);

  private readonly no = ++sonrakiNo;
  protected readonly dugmeId = `rc-katlanir-filtre-${this.no}-dugme`;
  protected readonly panelId = `rc-katlanir-filtre-${this.no}-panel`;

  protected degistir(): void {
    this.acik.update((acik) => !acik);
  }
}
