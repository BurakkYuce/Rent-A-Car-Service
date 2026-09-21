import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import { TemaServisi, type TemaModu } from '@core/tema/tema-servisi';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

interface TemaSecenegi {
  mod: TemaModu;
  etiket: CeviriAnahtari;
  ikon: IkonAdi;
}

/**
 * Yer tutucu ana sayfa (kabuk F3.2'de). Oturumdaki kullanıcıyı, çıkışı ve tema seçimini gösterir;
 * e2e ve axe açık/koyu temayı burada denetler.
 */
@Component({
  selector: 'rc-yer-tutucu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Ikon, RouterLink],
  templateUrl: './yer-tutucu.html',
  styleUrl: './yer-tutucu.scss',
})
export class YerTutucu {
  protected readonly tema = inject(TemaServisi);
  protected readonly oturum = inject(OturumServisi);
  protected readonly temaSecenekleri: readonly TemaSecenegi[] = [
    { mod: 'sistem', etiket: 'tema.sistem', ikon: 'device-desktop' },
    { mod: 'acik', etiket: 'tema.acik', ikon: 'sun' },
    { mod: 'koyu', etiket: 'tema.koyu', ikon: 'moon' },
  ];
}
