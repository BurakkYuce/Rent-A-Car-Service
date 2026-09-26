import type { ApiHatasi } from '@core/api/api-hatasi';
import type { Sayfa } from '@core/api/sayfa';
import type { StoreStateType } from '@core/veri/temel-store';

import type { TableSource, TabloSayfasi } from './tablo-modeli';

/** Tablonun çizdiği hâl (saf türetim; şablon yalnız bunu okur). */
export interface CozulmusKaynak<T> {
  readonly tur: StoreStateType;
  /** `hata` ve `bos`'ta DAİMA boş — hata hiçbir koşulda eski/boş liste gibi görünmez. */
  readonly satirlar: readonly T[];
  /** Sunucu sayfalaması (`Sayfa<T>`); sayfasız liste ya da veri yoksa `null`. */
  readonly sayfa: TabloSayfasi | null;
  readonly hata: ApiHatasi | null;
  /** Yeniden yüklenirken önceki veri gösteriliyor (soluk + `aria-busy`). */
  readonly eskiVeri: boolean;
  /** Yalnız BAŞARILI ve sıfır kayıtlı yanıtta `true` (F3.4 `kayitYok` kuralı). */
  readonly kayitYok: boolean;
}

export function resolveSource<T>(source: TableSource<T>): CozulmusKaynak<T> {
  switch (source.tur) {
    case 'bos':
      return emptyState('bos');
    case 'hata':
      return { ...emptyState<T>('hata'), hata: source.hata };
    case 'yukleniyor': {
      if (source.onceki === undefined) return emptyState('yukleniyor');
      const { satirlar, sayfa } = resolveData<T>(source.onceki);
      return { tur: 'yukleniyor', satirlar, sayfa, hata: null, eskiVeri: true, kayitYok: false };
    }
    case 'hazir': {
      const { satirlar: rows, sayfa: page } = resolveData<T>(source.veri);
      return {
        tur: 'hazir',
        satirlar: rows,
        sayfa: page,
        hata: null,
        eskiVeri: false,
        kayitYok: rows.length === 0,
      };
    }
  }
}

function emptyState<T>(type: StoreStateType): CozulmusKaynak<T> {
  return { tur: type, satirlar: [], sayfa: null, hata: null, eskiVeri: false, kayitYok: false };
}

function resolveData<T>(data: Sayfa<T> | readonly T[]): {
  satirlar: readonly T[];
  sayfa: TabloSayfasi | null;
} {
  if (Array.isArray(data)) return { satirlar: data as readonly T[], sayfa: null };
  const s = data as Sayfa<T>;
  return { satirlar: s.kayitlar, sayfa: { sayfa: s.sayfaNo, boyut: s.boyut, toplam: s.toplam } };
}
