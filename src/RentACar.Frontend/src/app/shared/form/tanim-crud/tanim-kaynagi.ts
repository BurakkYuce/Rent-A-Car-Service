import { inject } from '@angular/core';
import type { Observable } from 'rxjs';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import type { SecenekOgesi } from '../kontroller/secenek';

/** Tanım satırı: kimlik + alan değerleri (alan adları `TanimAlani.ad`). */
export interface TanimSatiri {
  readonly id: string;
  readonly [alan: string]: unknown;
}

export type TanimAlanTuru = 'metin' | 'sayi' | 'para' | 'onay' | 'secim';

export interface TanimAlani {
  readonly ad: string;
  readonly etiket: string;
  readonly tur: TanimAlanTuru;
  readonly zorunlu?: boolean;
  readonly azamiUzunluk?: number;
  /** `secim` için sabit seçenekler (enum). */
  readonly secenekler?: readonly SecenekOgesi<string>[];
}

export type TanimDegeri = Readonly<Record<string, unknown>>;

/** Tanım ekranının veri kaynağı. `anahtar` → `Idempotency-Key` (GonderimKilidi kuralı). */
export interface TanimKaynagi<S extends TanimSatiri = TanimSatiri> {
  listele(): Observable<readonly S[]>;
  olustur(deger: TanimDegeri, anahtar: string): Observable<unknown>;
  guncelle(id: string, deger: TanimDegeri, anahtar: string): Observable<unknown>;
  sil(id: string, anahtar: string): Observable<unknown>;
}

/**
 * REST tanım kaynağı (F11 sözleşmesi): `GET kok` liste, `POST kok` oluştur, `PUT kok/{id}` güncelle,
 * `DELETE kok/{id}` sil. Enjeksiyon bağlamında çağrılır.
 */
export function restTanimKaynagi<S extends TanimSatiri = TanimSatiri>(
  kok: ApiYolu,
): TanimKaynagi<S> {
  const api = inject(ApiIstemcisi);
  const kayit = (id: string): ApiYolu => `${kok}/${encodeURIComponent(id)}`;
  return {
    listele: () => api.get<readonly S[]>(kok),
    olustur: (deger, anahtar) => api.post(kok, deger, { islemAnahtari: anahtar }),
    guncelle: (id, deger, anahtar) => api.put(kayit(id), deger, { islemAnahtari: anahtar }),
    sil: (id, anahtar) => api.delete(kayit(id), { islemAnahtari: anahtar }),
  };
}
