import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { KiraFormuDurumu } from '../kira-formu-durumu';
import { aracSecenegi, sayiya } from '../kira-formu-modeli';
import { AracKarti } from './arac-karti';
import { PlateChipComponent } from '@shared/plaka/plaka';
import { KF_ORTAK } from './ortak';

/**
 * ARAÇ — kanonik araç seçimi + müsaitlik penceresi (`GET /kiralar/musait-arac`; sayfa YENİLENMEZ,
 * girilen veri korunur) + bilgi kartı. Pencere getirilince araç araması o listeyle sınırlanır.
 */
@Component({
  selector: 'rc-kf-arac',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [...KF_ORTAK, AracKarti, PlateChipComponent],
  template: `
    @if (d.yeni) {
      <section class="rc-bolum kf-kart">
        <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.musaitlik' | transloco }}</h3>
        <div class="rc-form-izgara" [formGroup]="d.musaitFormu">
          <rc-alan [etiket]="'kiraFormu.alan.vfrom' | transloco">
            <rc-tarih-secici formControlName="vfrom" [hazirlar]="false" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.vto' | transloco">
            <rc-tarih-secici formControlName="vto" [hazirlar]="false" />
          </rc-alan>
          <rc-alan [etiket]="'kiraFormu.alan.vgrup' | transloco">
            <rc-metin-girdisi formControlName="vgrup" [azamiUzunluk]="64" />
          </rc-alan>
          <div class="kf-eylemler">
            <button
              type="button"
              class="rc-dugme"
              [disabled]="d.musait.yukleniyor() || !d.operasyon()"
              (click)="d.musaitGetir()"
            >
              {{ 'kiraFormu.eylem.musaitGetir' | transloco }}
            </button>
            @if (d.musait.veri()) {
              <button type="button" class="rc-dugme rc-dugme--hayalet" (click)="d.musaitTemizle()">
                {{ 'kiraFormu.eylem.tumAraclar' | transloco }}
              </button>
            }
          </div>
        </div>
        @if (d.musait.tur() === 'hata') {
          <p class="kf-not kf-not--uyari" role="alert">{{ d.musait.hata()?.detay }}</p>
        }
        @if (d.musaitNotu(); as not) {
          <p class="kf-not" role="status" data-testid="musait-notu">{{ not }}</p>
        }
        @if (d.musait.veri(); as liste) {
          @if (liste.length > 0) {
            <div
              class="rc-tablo-kap"
              role="region"
              tabindex="0"
              [attr.aria-label]="'kiraFormu.bolum.musaitAraclar' | transloco"
            >
              <table
                class="rc-duz-tablo"
                [attr.aria-label]="'kiraFormu.bolum.musaitAraclar' | transloco"
              >
                <thead>
                  <tr>
                    <th scope="col">{{ 'kiraFormu.arac.plaka' | transloco }}</th>
                    <th scope="col">{{ 'kiraFormu.arac.markaTip' | transloco }}</th>
                    <th scope="col">{{ 'kiraFormu.arac.grup' | transloco }}</th>
                    <th scope="col" class="rc-num">{{ 'kiraFormu.arac.km' | transloco }}</th>
                    <th scope="col">{{ 'kiraFormu.arac.sube' | transloco }}</th>
                    <th scope="col">
                      <span class="rc-gorunmez">{{ 'kiraFormu.eylem.sec' | transloco }}</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (a of liste; track a.id) {
                    <tr [class.rc-satir-secili]="d.secilenArac()?.id === a.id">
                      <td><rc-plaka boyut="sm" [plaka]="a.plaka" /></td>
                      <td>{{ a.marka }} {{ a.tip }}</td>
                      <td>{{ a.grup || '—' }}</td>
                      <td class="rc-num">{{ km(a.km) | sayi }}</td>
                      <td>{{ a.sube || '—' }}</td>
                      <td>
                        <button
                          type="button"
                          class="rc-dugme rc-dugme--kucuk"
                          [attr.aria-pressed]="d.secilenArac()?.id === a.id"
                          [attr.aria-label]="('kiraFormu.eylem.sec' | transloco) + ' ' + etiket(a)"
                          [disabled]="!d.operasyon()"
                          (click)="d.aracSec(a)"
                        >
                          {{ 'kiraFormu.eylem.sec' | transloco }}
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </section>
    }

    <section class="rc-bolum kf-kart" [formGroup]="d.form">
      <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.aracSecimi' | transloco }}</h3>
      <div class="rc-form-izgara">
        <rc-alan
          [etiket]="'kiraFormu.alan.arac' | transloco"
          class="rc-form-izgara__genis"
          [ipucu]="d.yeni ? '' : ('kiraFormu.ipucu.aracDegismez' | transloco)"
        >
          <rc-arama-secim
            formControlName="arac"
            [kaynak]="d.aracKaynagi"
            [yerTutucu]="'kiraFormu.alan.plakaAra' | transloco"
          />
        </rc-alan>
      </div>
      <rc-kf-arac-karti />
    </section>

    <section class="rc-bolum kf-kart">
      <h3 class="kf-kart__baslik">{{ 'kiraFormu.bolum.cikis' | transloco }}</h3>
      <dl class="kf-bilgiler">
        <div>
          <dt>{{ 'kiraFormu.alan.cikisKm' | transloco }}</dt>
          <dd>{{ d.kira()?.cikisKm ?? ('kiraFormu.not.teslimdeGirilir' | transloco) }}</dd>
        </div>
        <div>
          <dt>{{ 'kiraFormu.alan.cikisYakit' | transloco }}</dt>
          <dd>{{ d.kira()?.cikisYakit ?? ('kiraFormu.not.teslimdeGirilir' | transloco) }}</dd>
        </div>
      </dl>
    </section>
  `,
})
export class Arac {
  protected readonly d = inject(KiraFormuDurumu);
  protected readonly km = sayiya;

  protected etiket(a: Parameters<typeof aracSecenegi>[0]): string {
    return aracSecenegi(a).etiket;
  }
}
