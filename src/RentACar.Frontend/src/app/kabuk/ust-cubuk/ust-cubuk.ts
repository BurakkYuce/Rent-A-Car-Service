import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { Router } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { TemaServisi, type TemaModu } from '@core/tema/tema-servisi';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';
import { PlateSearchComponent } from '@shared/plaka/plaka-arama';
import type { NormalizedPlate } from '@shared/plaka/plaka-normalize';

interface TemaSecenegi {
  readonly mod: TemaModu;
  readonly etiket: CeviriAnahtari;
  readonly ikon: IkonAdi;
}

/**
 * Üst çubuk (Yol v2 §5.2, 56 px): menü daralt (mobilde çekmece aç), komut paleti (Ctrl+K / ⌘K), plaka arama,
 * bildirim zili (menüde bildirim öğesi varsa; sayaç sunucu rozetinden), tema üçlüsü. Kullanıcı bloğu ve çıkış
 * kenar çubuğunda — burada tekrar YOK.
 *
 * Plaka arama: `rc-plaka-arama` (`shared/plaka/`, TR şeritli). Enter → araç listesi plaka süzgeciyle
 * (`/araclar?q=…`, mevcut liste sorgusu; yeni uç yok); boş aramada gezinme yok. Erişilebilir adı "Hızlı araç
 * arama" — "Plaka" GEÇMEZ (e2e `getByLabel('Plaka')` form alanlarını hedefler).
 */
@Component({
  selector: 'rc-ust-cubuk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, PlateSearchComponent, TranslocoPipe],
  templateUrl: './ust-cubuk.html',
  styleUrl: './ust-cubuk.scss',
})
export class UstCubuk {
  protected readonly tema = inject(TemaServisi);
  private readonly router = inject(Router);
  private readonly enjektor = inject(Injector);

  readonly cekmeceAcik = input(false);
  /** Kenar çubuğu 56 px şeritte mi (daralt düğmesinin durumu). */
  readonly dar = input(false);
  /** Menüde bildirim öğesi var mı (zil yalnız o zaman). */
  readonly bildirimVar = input(false);
  /** Okunmamış bildirim sayısı; `null`/0 → rozet yok. */
  readonly bildirimSayisi = input<number | null>(null);

  readonly menuAc = output<void>();
  readonly daralt = output<void>();
  readonly paletAc = output<void>();
  readonly bildirimAc = output<void>();

  private readonly menuDugmesi = viewChild<ElementRef<HTMLButtonElement>>('menuDugmesi');
  private readonly plakaArama = viewChild(PlateSearchComponent, { read: ElementRef });
  private plakaGirdisi(): HTMLInputElement | null {
    return (
      (this.plakaArama()?.nativeElement as HTMLElement | undefined)?.querySelector('input') ?? null
    );
  }

  /** Mobilde plaka arama ikon düğmesiyle tam genişlik açılır. */
  protected readonly plakaAcik = signal(false);

  protected readonly kisayol = /Mac|iPhone|iPad/.test(
    inject(DOCUMENT).defaultView?.navigator.userAgent ?? '',
  )
    ? '⌘K'
    : 'Ctrl K';

  protected readonly temaSecenekleri: readonly TemaSecenegi[] = [
    { mod: 'sistem', etiket: 'kabuk.tema.sistem', ikon: 'device-desktop' },
    { mod: 'acik', etiket: 'kabuk.tema.acik', ikon: 'sun' },
    { mod: 'koyu', etiket: 'kabuk.tema.koyu', ikon: 'moon' },
  ];

  /** Çekmece kapanınca odak menü düğmesine döner. */
  menuDugmesineOdaklan(): void {
    this.menuDugmesi()?.nativeElement.focus();
  }

  protected plakaAramasiniAc(): void {
    this.plakaAcik.set(true);
    afterNextRender(() => this.plakaGirdisi()?.focus(), {
      injector: this.enjektor,
    });
  }

  protected plakaAra(sonuc: NormalizedPlate): void {
    const plaka = sonuc.ham.trim().replace(/\s+/g, ' ');
    if (!plaka) return;
    const girdi = this.plakaGirdisi();
    if (girdi) girdi.value = '';
    this.plakaAcik.set(false);
    void this.router.navigate(['/araclar'], { queryParams: { q: plaka } });
  }

  protected plakaKapat(olay: Event): void {
    if (!this.plakaAcik()) return;
    olay.stopPropagation();
    this.plakaAcik.set(false);
  }

  protected rozetMetni(sayi: number | null): string | null {
    if (!sayi || sayi <= 0) return null;
    return sayi > 99 ? '99+' : String(sayi);
  }
}
