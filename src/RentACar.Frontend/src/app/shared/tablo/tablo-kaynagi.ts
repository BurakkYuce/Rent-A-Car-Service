import type { ApiHatasi } from '@core/api/api-hatasi';
import type { Sayfa } from '@core/api/sayfa';
import type { StoreDurumTuru } from '@core/veri/temel-store';

import type { TabloKaynagi, TabloSayfasi } from './tablo-modeli';

/** Tablonun çizdiği hâl (saf türetim; şablon yalnız bunu okur). */
export interface CozulmusKaynak<T> {
  readonly tur: StoreDurumTuru;
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

export function kaynakCoz<T>(kaynak: TabloKaynagi<T>): CozulmusKaynak<T> {
  switch (kaynak.tur) {
    case 'bos':
      return bosHal('bos');
    case 'hata':
      return { ...bosHal<T>('hata'), hata: kaynak.hata };
    case 'yukleniyor': {
      if (kaynak.onceki === undefined) return bosHal('yukleniyor');
      const { satirlar, sayfa } = veriCoz<T>(kaynak.onceki);
      return { tur: 'yukleniyor', satirlar, sayfa, hata: null, eskiVeri: true, kayitYok: false };
    }
    case 'hazir': {
      const { satirlar, sayfa } = veriCoz<T>(kaynak.veri);
      return {
        tur: 'hazir',
        satirlar,
        sayfa,
        hata: null,
        eskiVeri: false,
        kayitYok: satirlar.length === 0,
      };
    }
  }
}

function bosHal<T>(tur: StoreDurumTuru): CozulmusKaynak<T> {
  return { tur, satirlar: [], sayfa: null, hata: null, eskiVeri: false, kayitYok: false };
}

function veriCoz<T>(veri: Sayfa<T> | readonly T[]): {
  satirlar: readonly T[];
  sayfa: TabloSayfasi | null;
} {
  if (Array.isArray(veri)) return { satirlar: veri as readonly T[], sayfa: null };
  const s = veri as Sayfa<T>;
  return { satirlar: s.kayitlar, sayfa: { sayfa: s.sayfaNo, boyut: s.boyut, toplam: s.toplam } };
}
