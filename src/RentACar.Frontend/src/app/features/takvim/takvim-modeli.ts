import type { SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import { listeTanimi } from '@core/veri/liste-sorgusu';

export type TakvimYaniti = Sema<'TakvimYaniti'>;
export type TakvimAraci = Sema<'TakvimAraci'>;
export type TakvimSecenekleri = Sema<'TakvimSecenekleri'>;

/** Gün hücresinin dolulukları (sunucu hesaplar; kira rezervasyonu ezer). */
export type Doluluk = 'Kira' | 'Rezervasyon';

/**
 * Takvim URL ↔ API sözleşmesi (`GET /api/ui/v1/takvim`): Blazor `/takvim` süzgeçleriyle aynı adlar
 * (`ay`, `plaka`, `grup`, `sube`). Sayfalama yok (sunucu 200 araçla keser, `aracToplam` bildirir);
 * `sayfa`/`boyut` API'ye gitmez (`takvimParametreleri`).
 */
export const TAKVIM = listeTanimi({
  filtreler: {
    ay: { tur: 'metin', enFazla: 7 },
    plaka: { tur: 'metin', enFazla: 64 },
    grup: { tur: 'metin', enFazla: 64 },
    sube: { tur: 'metin', enFazla: 128 },
  },
});

const AY = /^\d{4}-(0[1-9]|1[0-2])$/;

/** `yyyy-MM` biçiminde mi (elle yazılmış bozuk URL sunucuya 400 ürettirmesin — bugünün ayına düşer). */
export function ayGecerli(ay: string | undefined): ay is string {
  return ay !== undefined && AY.test(ay);
}

/** Liste parametrelerinden yalnız süzgeçler; bozuk `ay` gönderilmez (sunucu varsayılanı: İstanbul'da bu ay). */
export function takvimParametreleri(p: SorguParametreleri): SorguParametreleri {
  const sonuc: Record<string, SorguParametreleri[string]> = {};
  for (const [ad, deger] of Object.entries(p)) {
    if (ad === 'sayfa' || ad === 'boyut' || ad === 'sirala') continue;
    if (ad === 'ay' && (typeof deger !== 'string' || !ayGecerli(deger))) continue;
    sonuc[ad] = deger;
  }
  return sonuc;
}

export interface TakvimGunu {
  /** Ayın günü (1 tabanlı) = sütun başlığı. */
  readonly no: number;
  /** `yyyy-MM-dd` (İstanbul takvim günü). */
  readonly gun: string;
  readonly haftaSonu: boolean;
}

/** Ayın günleri (`ay` = `yyyy-MM`). Hafta sonu takvim gününden (saat dilimsiz, UTC bileşenleriyle). */
export function ayinGunleri(ay: string, gunSayisi: number): readonly TakvimGunu[] {
  const [y, a] = ay.split('-').map(Number);
  return Array.from({ length: gunSayisi }, (_, i) => {
    const no = i + 1;
    const haftaGunu = new Date(Date.UTC(y ?? 1970, (a ?? 1) - 1, no)).getUTCDay();
    return {
      no,
      gun: `${ay}-${String(no).padStart(2, '0')}`,
      haftaSonu: haftaGunu === 0 || haftaGunu === 6,
    };
  });
}

/** Hücre değeri → bilinen doluluk (sunucu başka bir metin yazarsa boş sayılır). */
export function doluluk(deger: string | null | undefined): Doluluk | null {
  return deger === 'Kira' || deger === 'Rezervasyon' ? deger : null;
}

/**
 * Kira formuna araçla giden bağlantının sorgusu (F4.3 sözleşmesi `?varac=`). Takvim bir PENCERE
 * seçtirmez: penceresiz `varac` kira formunda aracı ön seçer, tarihleri kullanıcı girer.
 */
export function kiralaSorgusu(aracId: string): Record<string, string> {
  return { varac: aracId };
}
