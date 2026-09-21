import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { subeEtiketi } from '@core/api/ui-tipleri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemaServisi, type TemaModu } from '@core/tema/tema-servisi';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

interface TemaSecenegi {
  readonly mod: TemaModu;
  readonly etiket: CeviriAnahtari;
  readonly ikon: IkonAdi;
}

/**
 * Üst çubuk: mobil menü düğmesi, komut paleti (Ctrl+K / ⌘K), tema, kullanıcı + firma + şube, çıkış.
 * Olaylar kabuğa gider (çıkış ve palet kaydedilmemiş değişiklik sorusundan geçer).
 */
@Component({
  selector: 'rc-ust-cubuk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Ikon, TranslocoPipe],
  templateUrl: './ust-cubuk.html',
  styleUrl: './ust-cubuk.scss',
})
export class UstCubuk {
  protected readonly tema = inject(TemaServisi);
  protected readonly oturum = inject(OturumServisi);

  readonly cekmeceAcik = input(false);
  readonly menuAc = output<void>();
  readonly paletAc = output<void>();
  readonly cikis = output<void>();

  private readonly menuDugmesi = viewChild<ElementRef<HTMLButtonElement>>('menuDugmesi');

  protected readonly kisayol = /Mac|iPhone|iPad/.test(
    inject(DOCUMENT).defaultView?.navigator.userAgent ?? '',
  )
    ? '⌘ K'
    : 'Ctrl K';

  protected readonly kimlik = computed(() => {
    const ben = this.oturum.ben();
    if (!ben) return null;
    return {
      ad: ben.kullanici.adSoyad || ben.kullanici.kullaniciAdi,
      firma: ben.kiraci.ad,
      sube: subeEtiketi(ben),
    };
  });

  protected readonly temaSecenekleri: readonly TemaSecenegi[] = [
    { mod: 'sistem', etiket: 'kabuk.tema.sistem', ikon: 'device-desktop' },
    { mod: 'acik', etiket: 'kabuk.tema.acik', ikon: 'sun' },
    { mod: 'koyu', etiket: 'kabuk.tema.koyu', ikon: 'moon' },
  ];

  /** Çekmece kapanınca odak menü düğmesine döner. */
  menuDugmesineOdaklan(): void {
    this.menuDugmesi()?.nativeElement.focus();
  }
}
