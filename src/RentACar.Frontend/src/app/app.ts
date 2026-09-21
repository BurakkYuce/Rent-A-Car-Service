import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { TemaServisi, type TemaModu } from '@core/tema/tema-servisi';
import { Ikon } from '@shared/ikon/ikon';
import type { IkonAdi } from '@shared/ikon/ikon-kaydi';

interface TemaSecenegi {
  mod: TemaModu;
  etiket: CeviriAnahtari;
  ikon: IkonAdi;
}

/**
 * Yeni arayüzün kabuğu. Şimdilik yer tutucu (kabuk F3.2'de); tasarım token'larını ve tema
 * seçimini açık/koyu kullanır ki e2e ve axe ikisini de denetlesin.
 */
@Component({
  selector: 'rc-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, Ikon],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly tema = inject(TemaServisi);
  protected readonly temaSecenekleri: readonly TemaSecenegi[] = [
    { mod: 'sistem', etiket: 'tema.sistem', ikon: 'device-desktop' },
    { mod: 'acik', etiket: 'tema.acik', ikon: 'sun' },
    { mod: 'koyu', etiket: 'tema.koyu', ikon: 'moon' },
  ];
}
