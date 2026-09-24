import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { ApiIstemcisi } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { TemelStore } from '@core/veri/temel-store';
import { TarihPipe } from '@shared/bicim/bicim-pipe';

import { kilobytes } from './documents-page';

type PlatformDocument = Sema<'PlatformDocumentDto'>;

/**
 * F11.2a firma belgeleri (Blazor `FirmaBelgeleri`, oturum): platformun paylaştığı belgeler, salt okunur.
 * Görüntüle = aynı indirme bağlantısı (tarayıcıda açılır), İndir = `?indir=1` (diske kaydeder).
 */
@Component({
  selector: 'rc-platform-documents-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslocoPipe, TarihPipe],
  styleUrl: '../definitions.scss',
  template: `
    <div class="sayfa">
      <h1>{{ 'tanimlar.platformDocument.baslik' | transloco }}</h1>
      <p class="aciklama">{{ 'tanimlar.platformDocument.aciklama' | transloco }}</p>
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
            <p class="bos">{{ 'tanimlar.platformDocument.bos' | transloco }}</p>
          } @else {
            <div class="kaydirma">
              <table class="tablo">
                <thead>
                  <tr>
                    <th scope="col">{{ 'tanimlar.document.belge' | transloco }}</th>
                    <th scope="col" class="sayi">
                      {{ 'tanimlar.platformDocument.surum' | transloco }}
                    </th>
                    <th scope="col" class="sayi">{{ 'tanimlar.document.boyut' | transloco }}</th>
                    <th scope="col">{{ 'tanimlar.platformDocument.guncelleme' | transloco }}</th>
                    <th scope="col" class="islemler">{{ 'form.tanim.islemler' | transloco }}</th>
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
                          <span class="alt-metin">{{ b.aciklama }}</span>
                        }
                      </td>
                      <td class="sayi">v{{ b.surum }}</td>
                      <td class="sayi">
                        {{ 'tanimlar.document.kb' | transloco: { kb: kilobytes(b.boyut) } }}
                      </td>
                      <td>{{ b.guncelleme | tarih }}</td>
                      <td class="islemler">
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
          <p class="bos" role="status">{{ 'form.tanim.yukleniyor' | transloco }}</p>
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
