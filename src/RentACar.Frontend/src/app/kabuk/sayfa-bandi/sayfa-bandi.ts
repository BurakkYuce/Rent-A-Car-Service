import { DOCUMENT } from '@angular/common';
import {
  afterEveryRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

let sayac = 0;

/**
 * Sayfa bandı (Yol v2 §5.4): 52 px lacivert şerit — ekran ikonu + başlık (`<h1>`, sayfanın TEK başlığı) + bağlam
 * pill'i + ikincil metin; sağda eylemler. Kullanım:
 *
 * ```html
 * <rc-sayfa-bandi baslik="Kira listesi" ikon="key" pill="Kirada · 23 kayıt" altMetin="Merkez · 18.09–25.09">
 *   <ng-container eylemler>
 *     <button type="button" class="rc-dugme">Excel</button>
 *   </ng-container>
 *   <a birincil class="rc-dugme rc-dugme--birincil" routerLink="/kiralar/yeni">Yeni kira</a>
 * </rc-sayfa-bandi>
 * ```
 *
 * - `[eylemler]`: ikincil eylemler — bantta çerçeveli beyaz görünür (`.rc-dugme` burada yeniden boyanır).
 * - `[birincil]`: sayfanın TEK dolu birincil eylemi (dolu beyaz). §5.4'ün "tek dolu birincil" kuralı ayrı yuvayla
 *   yapısal: mobilde (≤ 900 px) band yalnız başlığı + birincili gösterir, ikinciller "…" menüsüne iner.
 *
 * Projeksiyonla gelen düğmelere ulaşmak için stil kapsüllenmez; seçiciler `rc-sayfa-bandi` altında sınırlı.
 */
@Component({
  selector: 'rc-sayfa-bandi',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  imports: [Ikon, TranslocoPipe],
  host: { '(document:click)': 'disariTik($event)', '(keydown.escape)': 'kapat(true)' },
  templateUrl: './sayfa-bandi.html',
  styleUrl: './sayfa-bandi.scss',
})
export class SayfaBandi {
  private readonly kok = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly belge = inject(DOCUMENT);

  readonly baslik = input.required<string>();
  readonly ikon = input<IkonAdi | null>(null);
  /** Bağlam pill'i (ör. "Kirada · 23 kayıt"). */
  readonly pill = input<string | null>(null);
  /** Başlığın yanındaki ikincil metin (ör. tarih aralığı, şube). */
  readonly altMetin = input<string | null>(null);

  protected readonly menuKimligi = `rc-bant-eylemler-${++sayac}`;
  protected readonly menuAcik = signal(false);
  /** İkincil eylem yuvası dolu mu ("…" düğmesi yalnız o zaman). */
  protected readonly ikincilVar = signal(false);

  private readonly eylemler = viewChild.required<ElementRef<HTMLElement>>('eylemler');
  private readonly menuDugmesi = viewChild<ElementRef<HTMLButtonElement>>('menuDugmesi');

  constructor() {
    // Projeksiyon içeriği ebeveynin `@if`'iyle değişebilir: her çizimden sonra bakılır (yalnız değişince yazılır).
    afterEveryRender(() => {
      const dolu = this.eylemler().nativeElement.childElementCount > 0;
      if (dolu !== this.ikincilVar()) this.ikincilVar.set(dolu);
    });
  }

  protected menuyuDegistir(): void {
    this.menuAcik.update((a) => !a);
  }

  protected kapat(odakGeriVer: boolean): void {
    if (!this.menuAcik()) return;
    this.menuAcik.set(false);
    const aktif = this.belge.activeElement;
    if (odakGeriVer && aktif && this.eylemler().nativeElement.contains(aktif)) {
      this.menuDugmesi()?.nativeElement.focus();
    }
  }

  protected disariTik(olay: MouseEvent): void {
    if (!this.menuAcik()) return;
    const hedef = olay.target as Node | null;
    if (hedef && this.kok.nativeElement.querySelector('.bant__menu-dugmesi')?.contains(hedef)) {
      return;
    }
    // Menü içinde bir eyleme tıklanınca da kapanır (işlem başladı).
    this.menuAcik.set(false);
  }
}
