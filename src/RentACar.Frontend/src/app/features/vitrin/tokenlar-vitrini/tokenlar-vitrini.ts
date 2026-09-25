import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

interface RenkGrubu {
  readonly ad: string;
  readonly tokenlar: readonly string[];
}

interface DurumOrnegi {
  readonly ad: string;
  /** Anlamsal durum token'ı (rozet `-zemin` / `-metin`). */
  readonly rozet: string;
  /** `--rc-tabela-{tabela}-*`; bugün satırının tabelası yok. */
  readonly tabela: string | null;
  readonly kullanim: string;
}

const DURUMLAR = ['basari', 'uyari', 'hata', 'bilgi', 'notr'] as const;

/** Ham palet aileleri (`--rc-ham-{aile}-{ton}`); bileşen stilinde doğrudan okunmaz. */
const HAM_PALET: readonly RenkGrubu[] = [
  { ad: 'Kağıt', tokenlar: ['50', '100', '200', '300'].map((t) => `--rc-ham-kagit-${t}`) },
  {
    ad: 'Asfalt',
    tokenlar: ['950', '900', '800', '700', '600'].map((t) => `--rc-ham-asfalt-${t}`),
  },
  {
    ad: 'Mürekkep',
    tokenlar: ['900', '700', '600', '400', '300'].map((t) => `--rc-ham-murekkep-${t}`),
  },
  {
    ad: 'Lacivert',
    tokenlar: ['900', '800', '700', '600', '500', '400', '300', '200', '100', '50'].map(
      (t) => `--rc-ham-lacivert-${t}`,
    ),
  },
  { ad: 'Otoyol', tokenlar: ['900', '700', '300', '50'].map((t) => `--rc-ham-otoyol-${t}`) },
  {
    ad: 'İşaret',
    tokenlar: ['900', '800', '500', '300', '50'].map((t) => `--rc-ham-isaret-${t}`),
  },
  { ad: 'Dur', tokenlar: ['900', '700', '300', '50'].map((t) => `--rc-ham-dur-${t}`) },
  { ad: 'Petrol', tokenlar: ['900', '700', '300', '50'].map((t) => `--rc-ham-petrol-${t}`) },
  {
    ad: 'Plaka',
    tokenlar: ['--rc-ham-plaka-beyaz', '--rc-ham-plaka-siyah', '--rc-ham-plaka-kenar'],
  },
];

/** Filo durum sözlüğü (plan §1.2): rozet anlamsal token'la, tabela kartı sabit renkle. */
const DURUM_SOZLUGU: readonly DurumOrnegi[] = [
  { ad: 'Kirada', rozet: 'basari', tabela: 'kirada', kullanim: 'araç yolda, gelir' },
  { ad: 'Boşta', rozet: 'notr', tabela: 'bosta', kullanim: 'müsait' },
  { ad: 'Serviste', rozet: 'uyari', tabela: 'serviste', kullanim: 'bakım, dikkat' },
  { ad: 'Rezerve', rozet: 'vurgu', tabela: 'rezerve', kullanim: 'söz verilmiş' },
  { ad: 'Gecikmiş', rozet: 'hata', tabela: 'gecikmis', kullanim: 'eylem gerek' },
  { ad: 'Bugün dönüyor', rozet: 'uyari', tabela: null, kullanim: 'bugünün işi, satır vurgusu' },
];

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
    .durum {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rc-bosluk-2) var(--rc-bosluk-3);
      align-items: center;
      padding: var(--rc-bosluk-2);
      border-radius: var(--rc-yaricap-md);
    }
    .rozet {
      padding: var(--rc-bosluk-0-5) var(--rc-bosluk-2);
      border-radius: var(--rc-yaricap-tam);
      font-size: var(--rc-yazi-xs);
      font-weight: var(--rc-agirlik-orta);
    }
    .tabela {
      padding: var(--rc-bosluk-1) var(--rc-bosluk-3);
      border-radius: var(--rc-yaricap-lg);
      font-weight: var(--rc-agirlik-kalin);
    }
    .plaka {
      padding: var(--rc-bosluk-0-5) var(--rc-bosluk-2);
      border: 1px solid var(--rc-plaka-kenar);
      border-left: var(--rc-bosluk-3) solid var(--rc-plaka-serit);
      border-radius: var(--rc-yaricap-sm);
      background-color: var(--rc-plaka-zemin);
      color: var(--rc-plaka-metin);
      font-family: var(--rc-font-plaka);
      font-size: var(--rc-yazi-lg);
      font-weight: var(--rc-agirlik-kalin);
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

    <section class="bolum" aria-labelledby="durum-sozlugu">
      <h2 id="durum-sozlugu">Filo durum sözlüğü</h2>
      <p class="not">
        Renk = durum. Rozet anlamsal token'la (tema döner), tabela kartı iki temada aynı.
      </p>
      <ul class="izgara">
        @for (durum of durumSozlugu; track durum.ad) {
          <li
            class="durum"
            [style.background-color]="durum.tabela ? null : 'var(--rc-satir-vurgu)'"
          >
            <span
              class="rozet"
              [style.color]="'var(--rc-' + durum.rozet + '-metin)'"
              [style.background-color]="'var(--rc-' + durum.rozet + '-zemin)'"
              >{{ durum.ad }}</span
            >
            @if (durum.tabela; as tabela) {
              <span
                class="tabela"
                [style.color]="'var(--rc-tabela-' + tabela + '-metin)'"
                [style.background-color]="'var(--rc-tabela-' + tabela + '-zemin)'"
                >128</span
              >
            }
            <span class="not">{{ durum.kullanim }}</span>
          </li>
        }
      </ul>
      <p class="satir">
        <span class="plaka">34 ABC 123</span>
        <code>--rc-plaka-*</code>
        <code>--rc-font-plaka</code>
      </p>
    </section>

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

    @for (grup of hamPalet; track grup.ad) {
      <section class="bolum" [attr.aria-labelledby]="'ham-' + $index">
        <h2 [id]="'ham-' + $index">Ham palet: {{ grup.ad }}</h2>
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

    <section class="bolum" aria-labelledby="yazi-olcegi">
      <h2 id="yazi-olcegi">Yazı ölçeği (IBM Plex Sans, gövde 14 px)</h2>
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
    {
      ad: 'Satır vurgusu ve dolu sarı',
      tokenlar: ['--rc-satir-vurgu', '--rc-uyari-dolgu', '--rc-uyari-dolgu-metin'],
    },
    {
      ad: 'Kenar çubuğu',
      tokenlar: [
        '--rc-kenar-cubugu-zemin',
        '--rc-kenar-cubugu-metin',
        '--rc-kenar-cubugu-ikincil',
        '--rc-kenar-cubugu-grup',
        '--rc-kenar-cubugu-aktif-zemin',
        '--rc-kenar-cubugu-aktif-metin',
        '--rc-kenar-cubugu-ayrac',
        '--rc-kenar-cubugu-kontrol-kenar',
        '--rc-kenar-cubugu-rozet-zemin',
        '--rc-kenar-cubugu-rozet-metin',
        '--rc-kenar-cubugu-rozet-uyari-zemin',
        '--rc-kenar-cubugu-rozet-uyari-metin',
      ],
    },
    {
      ad: 'Sayfa bandı',
      tokenlar: [
        '--rc-bant-zemin',
        '--rc-bant-metin',
        '--rc-bant-ikincil',
        '--rc-bant-buton-kenar',
        '--rc-bant-dolu-buton-zemin',
        '--rc-bant-dolu-buton-metin',
      ],
    },
    {
      ad: 'Tablo başlığı ve görünüm çipi',
      tokenlar: [
        '--rc-tablo-baslik-zemin',
        '--rc-tablo-baslik-metin',
        '--rc-cip-kenar',
        '--rc-cip-metin',
        '--rc-cip-secili-zemin',
        '--rc-cip-secili-metin',
        '--rc-cip-sayac-zemin',
        '--rc-cip-sayac-metin',
        '--rc-cip-secili-sayac-zemin',
        '--rc-cip-secili-sayac-metin',
        '--rc-cip-hata-sayac-zemin',
        '--rc-cip-hata-sayac-metin',
      ],
    },
  ];
  protected readonly hamPalet = HAM_PALET;
  protected readonly durumSozlugu = DURUM_SOZLUGU;
  protected readonly durumlar = DURUMLAR;
  protected readonly yaziOlcegi = ['2xs', 'xs', 'sm', 'md', 'lg', 'xl', '2xl', '3xl'] as const;
  protected readonly agirliklar = ['normal', 'orta', 'kalin', 'cok-kalin'] as const;
  protected readonly bosluklar = ['0-5', '1', '1-5', '2', '3', '4', '5', '6', '8', '12'] as const;
  protected readonly yaricaplar = ['sm', 'md', 'lg', 'xl', 'tam'] as const;
  protected readonly golgeler = ['1', '2', '3', 'katman'] as const;
}
