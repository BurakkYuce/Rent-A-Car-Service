import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

import { FilterPanelComponent } from '@shared/filtre-paneli/filtre-paneli';
import { SavedViewChipsComponent, type SavedView } from '@shared/gorunum-cipleri/gorunum-cipleri';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { PlateSearchComponent } from '@shared/plaka/plaka-arama';
import type { NormalizedPlate } from '@shared/plaka/plaka-normalize';
import { StatusSignCardComponent } from '@shared/tabela-karti/tabela-karti';

const GORUNUMLER: readonly SavedView[] = [
  { id: 'tumu', ad: 'Tüm sözleşmeler', aktif: false },
  { id: 'kirada', ad: 'Kirada', sayac: 23, aktif: true },
  { id: 'geciken', ad: 'Dönüşü gecikenler', sayac: 3, tur: 'hata', aktif: false },
  { id: 'bugun-cikan', ad: 'Bugün çıkanlar', sayac: 1, aktif: false },
  { id: 'bugun-donecek', ad: 'Bugün dönecekler', sayac: 1, aktif: false },
  { id: 'faturasiz', ad: 'Faturası kesilmeyenler', aktif: false },
  { id: 'kapali', ad: 'Kapalı sözleşmeler', aktif: false },
];

/**
 * Yol v2 imza bileşenleri vitrini (PR-B): plaka çipi + plaka arama, tabela kartları, kayıtlı görünüm çipleri,
 * filtre paneli ve düz tablo (asfalt başlık, iki satırlı hücre, bugün/seçili satır). Yalnız global sınıflar ve
 * shared bileşenler — sayfa stili yok (düzen `_duzen.scss`'ten).
 */
@Component({
  selector: 'rc-yol-bilesenleri',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FilterPanelComponent,
    PlateChipComponent,
    PlateSearchComponent,
    SavedViewChipsComponent,
    StatusSignCardComponent,
  ],
  template: `
    <section class="rc-bolum" aria-labelledby="yol-plaka">
      <h2 id="yol-plaka">Plaka çipi</h2>
      <div class="rc-arac-cubugu">
        <rc-plaka plaka="07 bfg 579" boyut="sm" />
        <rc-plaka plaka="34abc123" />
        <rc-plaka plaka="06 A 12345" boyut="lg" />
        <rc-plaka plaka="B 1234 XY" />
        <rc-plaka plaka="82 ABC 123" boyut="sm" />
      </div>
      <p class="rc-hucre-alt">
        sm 22 · md 28 · lg 40 px. Son ikisi geçersiz (yabancı / il kodu 82): şeritsiz, ham büyük
        harf.
      </p>
      <div class="rc-arac-cubugu">
        <rc-plaka-arama (ara)="aranan.set($event)" />
        <span role="status">
          @if (aranan(); as a) {
            {{ a.gecerli ? 'Aranacak: ' + a.gosterim : 'Geçersiz plaka: "' + a.gosterim + '"' }}
          }
        </span>
      </div>
    </section>

    <section class="rc-bolum" aria-labelledby="yol-tabela">
      <h2 id="yol-tabela">Tabela kartları</h2>
      <div class="rc-izgara-4">
        <rc-tabela-karti
          durum="kirada"
          etiket="Kiradaki araçlar"
          ikon="car"
          [deger]="23"
          [oran]="0.88"
          altMetin="Toplamdan %88"
        />
        <rc-tabela-karti
          durum="bosta"
          etiket="Boştaki araçlar"
          ikon="car"
          [deger]="3"
          [oran]="0.12"
          altMetin="Toplamdan %12 · müsait"
        />
        <rc-tabela-karti
          durum="serviste"
          etiket="Servisteki araçlar"
          ikon="settings"
          [deger]="0"
          [oran]="0"
          altMetin="Toplamdan %0"
        />
        <rc-tabela-karti
          durum="rezerve"
          etiket="Açık rezervasyonlar"
          ikon="calendar"
          [deger]="0"
          altMetin="Bekleyen rezervasyon yok"
        />
        <rc-tabela-karti
          durum="gecikmis"
          etiket="Dönüşü gecikenler"
          ikon="alert-triangle"
          [deger]="3"
          altMetin="Eylem gerekiyor"
        />
      </div>
    </section>

    <section class="rc-bolum" aria-labelledby="yol-liste">
      <h2 id="yol-liste">Liste kalıbı</h2>
      <rc-gorunum-cipleri [gorunumler]="gorunumler()" (secildi)="gorunumSec($event)" />
      <rc-filtre-paneli
        depoAnahtari="rc.vitrin.filtre"
        (temizle)="olay.set('Temizlendi')"
        (filtrele)="olay.set('Filtrelendi')"
      >
        <label class="rc-alan">
          <span class="rc-alan__etiket">Ad / soyad</span>
          <input class="rc-girdi" name="ad" placeholder="Birkaç karakter yazın" />
        </label>
        <label class="rc-alan">
          <span class="rc-alan__etiket">Sözleşme no</span>
          <input class="rc-girdi" name="no" placeholder="KS-…" />
        </label>
        <label class="rc-alan">
          <span class="rc-alan__etiket">Plaka</span>
          <input class="rc-girdi" name="plaka" />
        </label>
        <label class="rc-alan">
          <span class="rc-alan__etiket">Başlangıç</span>
          <input class="rc-girdi" name="bas" type="date" />
        </label>
        <label class="rc-alan">
          <span class="rc-alan__etiket">Bitiş</span>
          <input class="rc-girdi" name="bit" type="date" />
        </label>
        <label class="rc-alan">
          <span class="rc-alan__etiket">Kira durumu</span>
          <select class="rc-girdi" name="durum">
            <option>Kirada</option>
            <option>Kapalı</option>
          </select>
        </label>
      </rc-filtre-paneli>
      <p class="rc-hucre-alt" role="status">{{ olay() }}</p>

      <div class="rc-tablo-kap">
        <table class="rc-duz-tablo" aria-label="Kiradaki araçlar (örnek)">
          <thead>
            <tr>
              <th scope="col">Plaka</th>
              <th scope="col">Araç</th>
              <th scope="col">Müşteri</th>
              <th scope="col">Bitiş</th>
              <th scope="col" class="rc-num">Gün</th>
              <th scope="col">Durum</th>
            </tr>
          </thead>
          <tbody>
            <tr class="rc-satir-secili">
              <td><rc-plaka plaka="07 BKL 496" boyut="sm" /></td>
              <td>Fiat Fiorino<span class="rc-hucre-alt">Dizel · Düz · 2022</span></td>
              <td>Genco Karakaya<span class="rc-hucre-alt">Bireysel</span></td>
              <td>17.09.2026 09:34</td>
              <td class="rc-num">274</td>
              <td><span class="rc-rozet rc-rozet--hata">8 gün gecikti</span></td>
            </tr>
            <tr class="rc-satir-bugun">
              <td><rc-plaka plaka="07 BFG 579" boyut="sm" /></td>
              <td>Fiat Egea<span class="rc-hucre-alt">Dizel · Düz · 2023</span></td>
              <td>Igor Podushin<span class="rc-hucre-alt">Bireysel</span></td>
              <td>Bugün 14:36</td>
              <td class="rc-num">12</td>
              <td><span class="rc-rozet rc-rozet--uyari">Bugün dönüyor</span></td>
            </tr>
            <tr>
              <td><rc-plaka plaka="07 CIV 153" boyut="sm" /></td>
              <td>Hyundai i20<span class="rc-hucre-alt">Benzin · Otomatik · 2026</span></td>
              <td>Tülin Karaca<span class="rc-hucre-alt">Bireysel</span></td>
              <td>26.09.2026 12:41</td>
              <td class="rc-num">183</td>
              <td><span class="rc-rozet rc-rozet--basari">Kirada</span></td>
            </tr>
            <tr>
              <td colspan="6" class="rc-bos">Başka kayıt yok.</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class YolBilesenleri {
  protected readonly aranan = signal<NormalizedPlate | null>(null);
  protected readonly olay = signal('');
  protected readonly gorunumler = signal<readonly SavedView[]>(GORUNUMLER);

  protected gorunumSec(secilen: SavedView): void {
    this.gorunumler.update((liste) => liste.map((g) => ({ ...g, aktif: g.id === secilen.id })));
  }
}
