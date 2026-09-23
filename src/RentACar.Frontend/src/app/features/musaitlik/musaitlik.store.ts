import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';

import type { MusaitlikSecenekleri, MusaitlikYaniti } from './musaitlik-modeli';

/** Müsaitlik arama store'u: sonuç (sunucu pencere + fiyat + broker çiti) ve süzgeç seçenekleri (sessiz). */
@Injectable()
export class MusaitlikStore {
  private readonly api = inject(ApiIstemcisi);

  readonly sonuc = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<MusaitlikYaniti>('/api/ui/v1/musaitlik', {
        parametreler: p,
        // 400 (eksik bitiş/gün sayısı) sayfada sonuç yerine gösterilir; ikinci bant/toast üretmesin.
        context: istekBaglami({ sessiz: true }),
      }),
    { oncekiVeriyiKoru: true },
  );

  readonly secenekler = new TemelStore(() =>
    this.api.get<MusaitlikSecenekleri>('/api/ui/v1/musaitlik/secenekler', {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}
