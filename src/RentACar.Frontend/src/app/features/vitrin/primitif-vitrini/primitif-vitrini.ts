import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { BosDurum } from '@shared/bos-durum/bos-durum';
import { Ikon } from '@shared/ikon/ikon';
import { IKONLAR, type IkonAdi } from '@shared/ikon/ikon-kaydi';

import { YolBilesenleri } from './yol-bilesenleri';

const GORUNUMLER = ['Liste', 'Kart', 'Takvim'] as const;
type Gorunum = (typeof GORUNUMLER)[number];

/**
 * Primitif vitrini (F3.1, Yol v2 PR-B): sınıf tabanlı `rc-dugme*`, `rc-rozet*`, `rc-iskelet`, `<rc-bos-durum>`,
 * Yol imza bileşenleri (plaka, tabela, görünüm çipleri, filtre paneli, düz tablo) ve ikon kümesinin tamamı
 * (`ikon-listesi.json`). İkon kaydı bu sayfada statik içe aktarılır: sayfa zaten
 * tembel parça, ilk pakete girmez.
 */
@Component({
  selector: 'rc-primitif-vitrini',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, BosDurum, Ikon, YolBilesenleri],
  styleUrl: '../vitrin-ortak.scss',
  styles: `
    .ikon {
      display: flex;
      gap: var(--rc-bosluk-2);
      align-items: center;
      min-width: 0;
    }
    .iskelet {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-2);
      max-width: 24rem;
    }
    .iskelet .kisa {
      width: 60%;
    }
    .yol {
      display: flex;
      flex-direction: column;
      gap: var(--rc-bosluk-4);
    }
  `,
  template: `
    <div class="rc-sayfa-basligi">
      <h1>Primitifler</h1>
      <a routerLink="/vitrin">Vitrin</a>
    </div>

    <section class="rc-bolum" aria-labelledby="dugmeler">
      <h2 id="dugmeler">Düğmeler</h2>
      <div class="satir">
        <button type="button" class="rc-dugme rc-dugme--birincil">Birincil</button>
        <button type="button" class="rc-dugme">Çerçeveli (varsayılan)</button>
        <button type="button" class="rc-dugme rc-dugme--cerceveli">Çerçeveli vurgu</button>
        <button type="button" class="rc-dugme rc-dugme--hayalet">Hayalet</button>
        <button type="button" class="rc-dugme rc-dugme--tehlike">Tehlikeli</button>
        <button type="button" class="rc-dugme" disabled>Devre dışı</button>
        <a class="rc-dugme" routerLink="/vitrin">Bağlantı düğmesi</a>
      </div>
      <div class="satir">
        <button type="button" class="rc-dugme rc-dugme--kucuk">Küçük (30)</button>
        <button type="button" class="rc-dugme">Normal (34)</button>
        <button type="button" class="rc-dugme rc-dugme--buyuk">Büyük (40)</button>
        <button type="button" class="rc-dugme">
          <rc-ikon ad="plus" [boyut]="14" /> Yeni kayıt
        </button>
        <button type="button" class="rc-dugme rc-dugme--ikon" aria-label="Yazdır">
          <rc-ikon ad="printer" />
        </button>
        <button
          type="button"
          class="rc-dugme rc-dugme--ikon rc-dugme--hayalet"
          aria-label="Ayarlar"
        >
          <rc-ikon ad="settings" />
        </button>
      </div>
      <div class="rc-dugme-grubu" role="group" aria-label="Görünüm">
        @for (secenek of gorunumler; track secenek) {
          <button
            type="button"
            class="rc-dugme rc-dugme--kucuk"
            [attr.aria-pressed]="gorunum() === secenek"
            (click)="gorunum.set(secenek)"
          >
            {{ secenek }}
          </button>
        }
      </div>
    </section>

    <section class="rc-bolum" aria-labelledby="rozetler">
      <h2 id="rozetler">Durum rozeti</h2>
      <div class="satir">
        <span class="rc-rozet rc-rozet--basari">Kirada</span>
        <span class="rc-rozet rc-rozet--notr">Boşta</span>
        <span class="rc-rozet rc-rozet--uyari">Serviste</span>
        <span class="rc-rozet rc-rozet--vurgu">Rezerve</span>
        <span class="rc-rozet rc-rozet--hata">8 gün gecikti</span>
        <span class="rc-rozet rc-rozet--bilgi">Teslime hazır</span>
        <span class="rc-rozet rc-rozet--basari"><rc-ikon ad="check" [boyut]="12" /> Onaylı</span>
      </div>
    </section>

    <rc-yol-bilesenleri class="yol" />

    <section class="rc-bolum" aria-labelledby="iskelet">
      <h2 id="iskelet">İskelet</h2>
      <div class="iskelet" aria-hidden="true">
        <span class="rc-iskelet"></span>
        <span class="rc-iskelet"></span>
        <span class="rc-iskelet kisa"></span>
      </div>
    </section>

    <section class="rc-bolum" aria-labelledby="bos-durum">
      <h2 id="bos-durum">Boş durum</h2>
      <rc-bos-durum />
      <rc-bos-durum ikon="car" baslik="Henüz araç yok" aciklama="İlk aracınızı ekleyerek başlayın.">
        <button type="button" class="rc-dugme rc-dugme--birincil">
          <rc-ikon ad="plus" [boyut]="14" /> Araç ekle
        </button>
      </rc-bos-durum>
    </section>

    <section class="rc-bolum" aria-labelledby="ikonlar">
      <h2 id="ikonlar">İkonlar ({{ ikonlar.length }})</h2>
      <p class="not">
        Tabler alt kümesi; yeni ikon <code>ikon-listesi.json</code> + <code>npm run ikonlar</code>.
      </p>
      <ul class="izgara">
        @for (ad of ikonlar; track ad) {
          <li class="ikon">
            <rc-ikon [ad]="ad" [boyut]="20" /><code>{{ ad }}</code>
          </li>
        }
      </ul>
    </section>
  `,
})
export class PrimitifVitrini {
  protected readonly gorunumler = GORUNUMLER;
  protected readonly gorunum = signal<Gorunum>('Liste');
  protected readonly ikonlar = Object.keys(IKONLAR) as IkonAdi[];
}
