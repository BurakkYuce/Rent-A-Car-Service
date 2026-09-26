import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Schema } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';
import { DatePipe } from '@shared/bicim/bicim-pipe';

import { PageBand } from '../../../kabuk/sayfa-bandi/page-band';
import { kilobytes } from './documents-page';

type PlatformDocument = Schema<'PlatformDocumentDto'>;

/**
 * F11.2a firma belgeleri (Blazor `FirmaBelgeleri`, oturum): platformun paylaştığı belgeler, salt okunur.
 * Görüntüle = aynı indirme bağlantısı (tarayıcıda açılır), İndir = `?indir=1` (diske kaydeder).
 */
@Component({
  selector: 'rc-platform-documents-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, DatePipe, PageBand],
  styleUrl: '../definitions.scss',
  template: `
    <rc-sayfa-bandi [baslik]="'tanimlar.platformDocument.baslik' | transloco" ikon="file-text" />
    <div class="rc-sayfa">
      <p class="not">{{ 'tanimlar.platformDocument.aciklama' | transloco }}</p>
      @switch (list.tur()) {
        @case ('hata') {
          <div class="rc-form-hatalari" role="alert">
            {{ 'tanimlar.document.yuklenemedi' | transloco }} {{ list.hata()?.detay }}
            <button type="button" class="rc-dugme rc-dugme--kucuk" (click)="list.yenile()">
              {{ 'tanimlar.document.yenidenDene' | transloco }}
            </button>
          </div>
        }
        @case ('hazir') {
          @if (rows().length === 0) {
            <p class="not">{{ 'tanimlar.platformDocument.bos' | transloco }}</p>
          } @else {
            <div class="rc-tablo-kap">
              <table class="rc-duz-tablo">
                <thead>
                  <tr>
                    <th scope="col">{{ 'tanimlar.document.belge' | transloco }}</th>
                    <th scope="col" class="rc-num">
                      {{ 'tanimlar.platformDocument.surum' | transloco }}
                    </th>
                    <th scope="col" class="rc-num">{{ 'tanimlar.document.boyut' | transloco }}</th>
                    <th scope="col">{{ 'tanimlar.platformDocument.guncelleme' | transloco }}</th>
                    <th scope="col" class="satir-eylemleri">
                      {{ 'form.tanim.islemler' | transloco }}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (b of rows(); track b.id) {
                    <tr>
                      <td>
                        {{ b.baslik }}
                        @if (b.yeni) {
                          <span class="rc-rozet rc-rozet--basari">{{
                            'tanimlar.platformDocument.yeni' | transloco
                          }}</span>
                        }
                        @if (b.aciklama) {
                          <span class="rc-hucre-alt">{{ b.aciklama }}</span>
                        }
                      </td>
                      <td class="rc-num">v{{ b.surum }}</td>
                      <td class="rc-num">
                        {{ 'tanimlar.document.kb' | transloco: { kb: kilobytes(b.boyut) } }}
                      </td>
                      <td>{{ b.guncelleme | tarih }}</td>
                      <td class="satir-eylemleri">
                        <a
                          class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
                          [href]="b.indirmeYolu"
                          target="_blank"
                          rel="noopener"
                          >{{ 'tanimlar.platformDocument.goruntule' | transloco }}</a
                        >
                        <a
                          class="rc-dugme rc-dugme--hayalet rc-dugme--kucuk"
                          [href]="b.indirmeYolu + '?indir=1'"
                          >{{ 'tanimlar.document.indir' | transloco }}</a
                        >
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
        @default {
          <p class="not" role="status">{{ 'form.tanim.yukleniyor' | transloco }}</p>
        }
      }
    </div>
  `,
})
export class PlatformDocumentsPage {
  private readonly api = inject(ApiIstemcisi);
  protected readonly list = new TemelStore<readonly PlatformDocument[]>(() =>
    this.api.get<readonly PlatformDocument[]>('/api/ui/v1/firma-belgeleri'),
  );
  protected readonly rows = computed(() => this.list.veri() ?? []);
  protected readonly kilobytes = kilobytes;

  constructor() {
    this.list.yukle();
  }
}
