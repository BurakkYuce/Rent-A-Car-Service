import { inject } from '@angular/core';
import { type Observable, map } from 'rxjs';
import { ApiIstemcisi, type ApiYolu } from '@core/api/api-istemcisi';
import type { SecenekOgesi } from '../kontroller/secenek';

/**
 * Tanım satırı: kimlik + alan değerleri (alan adları `TanimAlani.ad`). `surum` (F11 sözleşmesi) satırın
 * iyimser eşzamanlılık belirtecidir: varsa PUT'a aynen gider; yoksa (`undefined`/`null`) ve kaynakta `read`
 * varsa düzenleme açılırken kayıt tekil okunur.
 */
export interface TanimSatiri {
  readonly id: string;
  readonly surum?: string | null;
  readonly [alan: string]: unknown;
}

/**
 * - `datalist`: seç veya yaz (`<datalist>`; serbest metin de kabul) — öneriler `suggestions(q)` ile, yazdıkça.
 * - `date`: takvim günü (`"2026-09-22"`, `rc-tarih-secici`).
 * - `textarea`: çok satırlı düz metin (`rc-metin-alani`; F11.2b sayfa gövdesi, SSS cevabı). Listede kısaltılır.
 */
export type TanimAlanTuru =
  'metin' | 'sayi' | 'para' | 'onay' | 'secim' | 'datalist' | 'date' | 'textarea';

export type DefinitionOptions = readonly SecenekOgesi<unknown>[];

export interface TanimAlani {
  readonly ad: string;
  readonly etiket: string;
  readonly tur: TanimAlanTuru;
  readonly zorunlu?: boolean;
  readonly azamiUzunluk?: number;
  /** `secim` için seçenekler (enum, `true/false` durum); dinamik liste için fonksiyon (signal da olur). */
  readonly secenekler?: DefinitionOptions | (() => DefinitionOptions);
  /** `datalist` için: yazılan metne (`q`) göre öneri metinleri. Hata → öneri yok (alan serbest metin kalır). */
  readonly suggestions?: (q: string) => Observable<readonly string[]>;
  /** `sayi` için kesir hanesi (varsayılan 0) ve negatif izni. */
  readonly fraction?: number;
  readonly negative?: boolean;
  readonly placeholder?: string;
  /** Yeni kayıtta başlangıç değeri (ör. `aktif: true`). */
  readonly defaultValue?: unknown;
  /** `false`: tabloda sütun olarak gösterilmez (yalnız düzenleme formunda). */
  readonly inList?: boolean;
  /**
   * `true`: formda GÖRÜNMEZ ama değeri gövdeye aynen gider (tam değiştirme PUT'unda ekranda olmayan alan
   * silinmesin — ör. şubenin bağlı kasa/banka hesabı).
   */
  readonly hidden?: boolean;
}

export type TanimDegeri = Readonly<Record<string, unknown>>;

/** Tanım ekranının veri kaynağı. `anahtar` → `Idempotency-Key` (GonderimKilidi kuralı). */
export interface TanimKaynagi<S extends TanimSatiri = TanimSatiri> {
  listele(): Observable<readonly S[]>;
  /** Tekil kayıt (güncel `surum` ile) — 409 `cakisma` sonrası birleştirme ve sürümsüz liste satırı için. */
  read?(id: string): Observable<S>;
  olustur(deger: TanimDegeri, anahtar: string): Observable<unknown>;
  /** `version`: düzenlenen kaydın `surum`'u (bilinmiyorsa `undefined` — gövdeye yazılmaz). */
  guncelle(
    id: string,
    deger: TanimDegeri,
    anahtar: string,
    version?: string | null,
  ): Observable<unknown>;
  sil(id: string, anahtar: string): Observable<unknown>;
}

type RawRow = Readonly<Record<string, unknown>>;

export interface RestDefinitionOptions {
  /** API satırı → tanım satırı (ör. alan dönüşümü). Varsayılan: aynen. */
  readonly toRow?: (raw: RawRow) => TanimSatiri;
  /** Form değeri → POST/PUT gövdesi (`surum` ayrıca eklenir). Varsayılan: aynen. */
  readonly toBody?: (value: TanimDegeri) => RawRow;
}

/**
 * REST tanım kaynağı (F11 sözleşmesi, `DefinitionEndpoints`): `GET kok` düz dizi (her satırda `surum`),
 * `GET kok/{id}` tekil, `POST kok` oluştur, `PUT kok/{id}` tam değiştirme (`surum` zorunlu, bayatsa 409
 * `cakisma`), `DELETE kok/{id}` sil (kullanımdaysa 400 + mesaj). Enjeksiyon bağlamında çağrılır.
 */
export function restTanimKaynagi<S extends TanimSatiri = TanimSatiri>(
  kok: ApiYolu,
  options: RestDefinitionOptions = {},
): TanimKaynagi<S> {
  const api = inject(ApiIstemcisi);
  const record = (id: string): ApiYolu => `${kok}/${encodeURIComponent(id)}`;
  const toRow = (raw: RawRow) => (options.toRow ? options.toRow(raw) : raw) as S;
  const toBody = (v: TanimDegeri): RawRow => (options.toBody ? options.toBody(v) : v);
  return {
    listele: () => api.get<readonly RawRow[]>(kok).pipe(map((l) => l.map(toRow))),
    read: (id) => api.get<RawRow>(record(id)).pipe(map(toRow)),
    olustur: (value, key) => api.post(kok, toBody(value), { islemAnahtari: key }),
    guncelle: (id, value, key, version) =>
      api.put(
        record(id),
        version === undefined ? toBody(value) : { ...toBody(value), surum: version },
        {
          islemAnahtari: key,
        },
      ),
    sil: (id, key) => api.delete(record(id), { islemAnahtari: key }),
  };
}
