import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';

import type { TakvimSecenekleri, TakvimYaniti } from './takvim-modeli';

/** Takvim sayfası store'u: ay ızgarası (sunucu hesaplar) + süzgeç önerileri (sessiz). */
@Injectable()
export class TakvimStore {
  private readonly api = inject(ApiIstemcisi);

  readonly izgara = new TemelStore(
    (p: SorguParametreleri) => this.api.get<TakvimYaniti>('/api/ui/v1/takvim', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly secenekler = new TemelStore(() =>
    this.api.get<TakvimSecenekleri>('/api/ui/v1/takvim/secenekler', {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}
