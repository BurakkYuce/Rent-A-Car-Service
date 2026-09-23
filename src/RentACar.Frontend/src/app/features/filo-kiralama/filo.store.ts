import { Injectable, inject } from '@angular/core';

import { ApiIstemcisi, type SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sayfa } from '@core/api/sayfa';
import type { SecimUcuOgesi } from '@core/api/ui-tipleri';
import { istekBaglami } from '@core/oturum/istek-baglami';
import { TemelStore } from '@core/veri/temel-store';

import type { FiloKiralama, FiloListeSatiri } from './filo-modeli';

/** Filo kiralama listesi: sunucu sayfalı liste + URL'deki müşteri kimliğinin etiketi (sessiz). */
@Injectable()
export class FiloListesiStore {
  private readonly api = inject(ApiIstemcisi);

  readonly liste = new TemelStore(
    (p: SorguParametreleri) =>
      this.api.get<Sayfa<FiloListeSatiri>>('/api/ui/v1/filo-kiralama', { parametreler: p }),
    { oncekiVeriyiKoru: true },
  );

  readonly musteri = new TemelStore((id: string) =>
    this.api.get<SecimUcuOgesi<'musteri'>>(`/api/ui/v1/secim/musteri/${encodeURIComponent(id)}`, {
      context: istekBaglami({ sessiz: true }),
    }),
  );
}

/** Filo sözleşmesi detayı (künye + taksit planı + yetkiler; plan sunucuda hesaplanır). */
@Injectable()
export class FiloDetayStore {
  private readonly api = inject(ApiIstemcisi);

  readonly detay = new TemelStore(
    (id: string) =>
      this.api.get<FiloKiralama>(`/api/ui/v1/filo-kiralama/${encodeURIComponent(id)}`),
    { oncekiVeriyiKoru: true },
  );
}
