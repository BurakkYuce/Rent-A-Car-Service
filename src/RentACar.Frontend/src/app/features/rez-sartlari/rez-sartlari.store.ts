import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SecimUcuOgesi } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';

import type { RezSart } from './rez-sart-modeli';

/**
 * Rez şartları sayfa store'u. Liste sunucu sayfalı; "bekleyen" sayacı, grup önerileri ve URL'deki
 * müşteri kimliğinin etiketi SESSİZ (hataları liste hatasının yanında ikinci bant üretmez).
 */
@Injectable()
export class RezSartlariStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<RezSart>>('/api/ui/v1/rez-sartlari', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly bekleyen = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<RezSart>>('/api/ui/v1/rez-sartlari', {
        parametreler: p,
        context: istekBaglami({ sessiz: true }),
      }),
    { oncekiVeriyiKoru: true },
  );

  readonly gruplar = new TemelStore(() =>
    this.api.get<readonly string[]>('/api/ui/v1/rez-sartlari/gruplar', {
      context: istekBaglami({ sessiz: true }),
    }),
  );

  readonly musteri = new TemelStore((id: string) =>
    this.api.get<SecimUcuOgesi<'musteri'>>(`/api/ui/v1/secim/musteri/${encodeURIComponent(id)}`, {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}
