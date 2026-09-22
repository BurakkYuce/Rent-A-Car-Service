import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

interface RenkGrubu {
  readonly ad: string;
  readonly tokenlar: readonly string[];
}

const DURUMLAR = ['basari', 'uyari', 'hata', 'bilgi', 'notr'] as const;

/**
 * Token vitrini (F3.1 `_tokenlar.scss`): renkler etkin temada çizilir (üst çubuktaki tema düğmesi ya
 * da sistem teması); CI görsel regresyonu sayfayı iki temada da çeker. Değer yazılmaz, yalnız token
 * adı: tek doğruluk kaynağı SCSS (kontrast tablosu `npm run kontrast`).
 */
@Component({
  selector: 'rc-tokenlar-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  styleUrl: '../vitrin-ortak.scss',
  styles: `
    .renk {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: center;
      min-width: 0;
    }
    .ornek {
      flex-shrink: 0;
      width: var(--rc-bosluk-8);
      height: var(--rc-bosluk-8);
      border: 1px solid var(--rc-kenar-kontrol);
      border-radius: var(--rc-yaricap-md);
    }
    .yazi {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-1) var(--rc-bosluk-3);
      align-items: baseline;
      overflow-wrap: anywhere;
    }
    .cubuk {
      height: var(--rc-bosluk-3);
      border-radius: var(--rc-yaricap-sm);
      background-color: var(--rc-vurgu);
    }
    .kutu {
      display: grid;
      place-items: center;
      height: var(--rc-bosluk-12);
      border: 1px solid var(--rc-kenar-kontrol);
      background-color: var(--rc-yuzey-alt);
    }
  `,
  template: `
    <div class="sayfa-ust">
      <h1>Token’lar</h1>
      <a routerLink="/vitrin">Vitrin</a>
    </div>
    <p class="giris">
      Bileşen stilleri yalnız bu token’larla yazılır (<code>--rc-*</code>). Renkler etkin temada
      gösterilir; tema üst çubuktan değişir.
    </p>

    @for (grup of renkGruplari; track grup.ad) {
      <section class="bolum" [attr.aria-labelledby]="'renk-' + $index">
        <h2 [id]="'renk-' + $index">{{ grup.ad }}</h2>
        <ul class="izgara">
          @for (token of grup.tokenlar; track token) {
            <li class="renk">
              <span class="ornek" [style.background-color]="'var(' + token + ')'"></span>
              <code>{{ token }}</code>
            </li>
          }
        </ul>
      </section>
    }

    <section class="bolum" aria-labelledby="durum-renkleri">
      <h2 id="durum-renkleri">Durum renkleri (metin / zemin / kenar)</h2>
      <ul class="izgara">
        @for (durum of durumlar; track durum) {
          <li
            class="kutu"
            [style.color]="'var(--rc-' + durum + '-metin)'"
            [style.background-color]="'var(--rc-' + durum + '-zemin)'"
            [style.border-color]="'var(--rc-' + durum + '-kenar)'"
          >
            <code>--rc-{{ durum }}-*</code>
          </li>
        }
      </ul>
    </section>

    <section class="bolum" aria-labelledby="yazi-olcegi">
      <h2 id="yazi-olcegi">Yazı ölçeği (Inter, gövde 13 px)</h2>
      @for (adim of yaziOlcegi; track adim) {
        <p class="yazi" [style.font-size]="'var(--rc-yazi-' + adim + ')'">
          <code>--rc-yazi-{{ adim }}</code>
          <span>Işık Şoför İzmir’de ğüşöç 1.234,56 ₺</span>
        </p>
      }
      <p class="yazi">
        @for (agirlik of agirliklar; track agirlik) {
          <span [style.font-weight]="'var(--rc-agirlik-' + agirlik + ')'">{{ agirlik }}</span>
        }
      </p>
    </section>

    <section class="bolum" aria-labelledby="bosluk-olcegi">
      <h2 id="bosluk-olcegi">Boşluk (4 px ızgarası)</h2>
      <ul class="izgara">
        @for (adim of bosluklar; track adim) {
          <li>
            <code>--rc-bosluk-{{ adim }}</code>
            <div class="cubuk" [style.width]="'var(--rc-bosluk-' + adim + ')'"></div>
          </li>
        }
      </ul>
    </section>

    <section class="bolum" aria-labelledby="kose-golge">
      <h2 id="kose-golge">Köşe ve gölge</h2>
      <ul class="izgara">
        @for (y of yaricaplar; track y) {
          <li class="kutu" [style.border-radius]="'var(--rc-yaricap-' + y + ')'">
            <code>--rc-yaricap-{{ y }}</code>
          </li>
        }
        @for (g of golgeler; track g) {
          <li class="kutu" [style.box-shadow]="'var(--rc-golge-' + g + ')'">
            <code>--rc-golge-{{ g }}</code>
          </li>
        }
      </ul>
    </section>
  `,
})
export class TokenlarVitrini {
  protected readonly renkGruplari: readonly RenkGrubu[] = [
    {
      ad: 'Zemin ve yüzey',
      tokenlar: ['--rc-zemin', '--rc-yuzey', '--rc-yuzey-alt', '--rc-yuzey-vurgu', '--rc-secim'],
    },
    {
      ad: 'Metin ve kenar',
      tokenlar: [
        '--rc-metin',
        '--rc-metin-ikincil',
        '--rc-metin-soluk',
        '--rc-metin-pasif',
        '--rc-kenar',
        '--rc-kenar-kontrol',
        '--rc-odak',
      ],
    },
    {
      ad: 'Vurgu (kiracı rengi)',
      tokenlar: [
        '--rc-vurgu',
        '--rc-vurgu-hover',
        '--rc-vurgu-uzeri',
        '--rc-vurgu-metin',
        '--rc-vurgu-zemin',
      ],
    },
  ];
  protected readonly durumlar = DURUMLAR;
  protected readonly yaziOlcegi = ['2xs', 'xs', 'sm', 'md', 'lg', 'xl', '2xl'] as const;
  protected readonly agirliklar = ['normal', 'orta', 'kalin', 'cok-kalin'] as const;
  protected readonly bosluklar = ['0-5', '1', '1-5', '2', '3', '4', '5', '6', '8', '12'] as const;
  protected readonly yaricaplar = ['sm', 'md', 'lg', 'xl', 'tam'] as const;
  protected readonly golgeler = ['1', '2', '3'] as const;
}
