import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { translationFunction } from '@core/i18n/ceviri';
import { TemelStore } from '@core/veri/temel-store';
import { FORMAT_PIPES } from '@shared/bicim/bicim-pipe';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';

type ProblemRow = Schema<'KabisSorunSatiri'>;

const KNOWN_TYPES: readonly string[] = ['Kiralama', 'Iade', 'Iptal'];
const KNOWN_STATUSES: readonly string[] = ['Bekliyor', 'Hata'];

/**
 * KABİS bildirilemeyen kiralar (panel "KABİS bildirilemeyen" kutusunun dökümü): bekleyen ya da hatalı bildirimler,
 * şube kapsamlı (`GET /kabis/bildirilemeyenler`, OperationsWrite veya FinanceWrite). İşlem kira ekranındaki KABİS
 * kartından yapılır; satır kiraya bağlanır. Kimlik numarası bu uçta yoktur.
 */
@Component({
  selector: 'rc-kabis-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageBand, RouterLink, TranslocoPipe, ...FORMAT_PIPES],
  template: `
    <rc-sayfa-bandi [baslik]="'kiraListesi.kabis.baslik' | transloco" ikon="alert-triangle" />
    <div class="rc-sayfa">
      <p class="rc-form-mesaji">{{ 'kiraListesi.kabis.aciklama' | transloco }}</p>
      @switch (list.tur()) {
        @case ('hata') {
          <div class="rc-form-mesaji rc-form-mesaji--hata" role="alert">
            {{ 'kiraListesi.kabis.yuklenemedi' | transloco }} {{ list.hata()?.detay }}
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="list.yenile()">
              {{ 'kiraListesi.kabis.yenidenDene' | transloco }}
            </button>
          </div>
        }
        @case ('hazir') {
          @if ((list.veri() ?? []).length === 0) {
            <p role="status">{{ 'kiraListesi.kabis.bos' | transloco }}</p>
            <p class="rc-form-mesaji">{{ 'kiraListesi.kabis.kapali' | transloco }}</p>
          } @else {
            <div class="rc-tablo-kap">
              <table class="rc-duz-tablo" data-testid="kabis-listesi">
                <caption class="rc-gorunmez">
                  {{
                    'kiraListesi.kabis.tabloEtiketi' | transloco
                  }}
                </caption>
                <thead>
                  <tr>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.sozlesmeNo' | transloco }}</th>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.plaka' | transloco }}</th>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.tur' | transloco }}</th>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.durum' | transloco }}</th>
                    <th scope="col" class="rc-num">
                      {{ 'kiraListesi.kabis.sutun.deneme' | transloco }}
                    </th>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.olay' | transloco }}</th>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.sonDeneme' | transloco }}</th>
                    <th scope="col">{{ 'kiraListesi.kabis.sutun.sonHata' | transloco }}</th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of list.veri() ?? []; track r.kiraId + r.tur) {
                    <tr>
                      <td>
                        <a [routerLink]="['/kiralar', r.kiraId]">{{ r.sozlesmeNo }}</a>
                      </td>
                      <td>{{ r.plaka ?? '—' }}</td>
                      <td>{{ typeLabel(r.tur) }}</td>
                      <td>
                        <span
                          [class]="
                            r.durum === 'Hata'
                              ? 'rc-rozet rc-rozet--hata'
                              : 'rc-rozet rc-rozet--uyari'
                          "
                          >{{ statusLabel(r.durum) }}</span
                        >
                      </td>
                      <td class="rc-num">{{ attempts(r.denemeSayisi) | sayi: '1.0-0' }}</td>
                      <td>{{ r.olayUtc | tarihSaat }}</td>
                      <td>{{ r.sonDenemeUtc ? (r.sonDenemeUtc | tarihSaat) : '—' }}</td>
                      <td>{{ r.sonHata ?? '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
        @default {
          <p role="status">{{ 'kiraListesi.kabis.yukleniyor' | transloco }}</p>
        }
      }
    </div>
  `,
})
export class KabisList {
  private readonly api = inject(ApiIstemcisi);
  private readonly t = translationFunction();

  protected readonly list = new TemelStore<readonly ProblemRow[]>(() =>
    this.api.get<readonly ProblemRow[]>('/api/ui/v1/kabis/bildirilemeyenler'),
  );

  constructor() {
    this.list.yukle();
  }

  /** OpenAPI tamsayıyı `number | string` üretir. */
  protected attempts(value: number | string): number {
    const n = Number(value);
    return Number.isFinite(n) ? n : 0;
  }

  protected typeLabel(type: string): string {
    return KNOWN_TYPES.includes(type)
      ? this.t(`kiraListesi.kabis.tur.${type}` as CeviriAnahtari)
      : type;
  }

  protected statusLabel(status: string): string {
    return KNOWN_STATUSES.includes(status)
      ? this.t(`kiraListesi.kabis.durum.${status}` as CeviriAnahtari)
      : status;
  }
}
