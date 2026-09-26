import type { KiraListeSatiri } from '@core/api/ui-tipleri';
import { anParcala, gunEkle, type GunMetni } from '@core/form/tarih-girdisi';
import type { Filtreler } from '@core/veri/liste-sorgusu';

import { KIRA_LISTESI } from './kira-listesi.store';

export type KiraFiltreleri = Filtreler<typeof KIRA_LISTESI.filtreler>;

/**
 * Kayıtlı görünümler (Yol v2 §5.1/§9): `kiralar?gorunum=…`. Kodlar kenar çubuğundaki bağlantılarla
 * (`kabuk/menu/kisayollar.ts` `GORUNUM_KODLARI`) BİREBİR aynı. Görünüm yalnız bir ÖN AYARDIR: mevcut sunucu
 * süzgeçlerine çevrilir (yeni uç/parametre yok). Tarih ön ayarları İstanbul gününe göre, gün hassasiyetinde
 * (`basMin/basMax` sunucuda gün aralığıdır).
 */
export const KIRA_GORUNUM_KODLARI = [
  'kirada',
  'geciken',
  'bugun-cikan',
  'bugun-donecek',
  'faturasiz',
  'kapali',
] as const;
export type KiraGorunumKodu = (typeof KIRA_GORUNUM_KODLARI)[number];

export function gorunumKoduMu(deger: string | null): deger is KiraGorunumKodu {
  return deger !== null && (KIRA_GORUNUM_KODLARI as readonly string[]).includes(deger);
}

/**
 * Görünüm → süzgeçler. `gun` = İstanbul'da bugün (`bugun()`).
 * - geciken: kirada + bitiş DÜNE kadar (bugün saati geçmiş dönüş "bugün dönecek"te görünür — sunucu
 *   süzgeci gün hassasiyetinde; saat ayrımı için ayrı parametre yok).
 * - bugün çıkan: başlangıç bugün (durum fark etmez); bugün dönecek: kirada + bitiş bugün.
 */
export function gorunumFiltreleri(kod: KiraGorunumKodu, gun: GunMetni): KiraFiltreleri {
  switch (kod) {
    case 'kirada':
      return { durum: 'Kirada' };
    case 'geciken':
      return { durum: 'Kirada', tarihTuru: 'Bitis', basMax: gunEkle(gun, -1) };
    case 'bugun-cikan':
      return { tarihTuru: 'Baslangic', basMin: gun, basMax: gun };
    case 'bugun-donecek':
      return { durum: 'Kirada', tarihTuru: 'Bitis', basMin: gun, basMax: gun };
    case 'faturasiz':
      return { fatura: false };
    case 'kapali':
      return { durum: 'Tamamlandi' };
  }
}

/**
 * `liste.degistir` süzgeçleri BİRLEŞTİRİR; ön ayar önceki süzgeçleri silmeli → katalogdaki her ad verilir
 * (verilmeyen `undefined` = URL'den kalkar).
 */
export function tumSuzgecler(f: KiraFiltreleri): KiraFiltreleri {
  const sonuc: Record<string, unknown> = {};
  for (const ad of Object.keys(KIRA_LISTESI.filtreler)) {
    sonuc[ad] = (f as Readonly<Record<string, unknown>>)[ad];
  }
  return sonuc as KiraFiltreleri;
}

/** Süzgeçler anlamca aynı mı (`undefined` alanlar yok sayılır). */
export function suzgeclerAyni(a: KiraFiltreleri, b: KiraFiltreleri): boolean {
  const anahtar = (f: KiraFiltreleri) =>
    JSON.stringify(
      Object.entries(f)
        .filter(([, v]) => v !== undefined)
        .sort(([x], [y]) => x.localeCompare(y, 'en')),
    );
  return anahtar(a) === anahtar(b);
}

/**
 * Satırın ekran durumu (Yol v2 §1.2). YALNIZ GÖSTERİM: sunucu durumu (`durum`) değişmez; kirada ve bitişi geçmiş
 * gün → "n gün gecikti" (kırmızı), bitişi bugün → "Bugün dönüyor" (sarı + satır vurgusu).
 */
export type SatirGorunumu =
  | { readonly tur: 'gecikmis'; readonly gun: number }
  | { readonly tur: 'bugunDonuyor' }
  | { readonly tur: 'durum'; readonly durum: string };

const GUN_MS = 86_400_000;
const gunMs = (gun: GunMetni) => Date.parse(`${gun}T00:00:00Z`);

export function satirGorunumu(satir: KiraListeSatiri, bugun: GunMetni): SatirGorunumu {
  const bitis = anParcala(satir.bitTar)?.gun;
  if (satir.durum === 'Kirada' && bitis) {
    if (bitis === bugun) return { tur: 'bugunDonuyor' };
    if (bitis < bugun)
      return { tur: 'gecikmis', gun: Math.round((gunMs(bugun) - gunMs(bitis)) / GUN_MS) };
  }
  return { tur: 'durum', durum: satir.durum };
}

/** Bugünün işi (bugün çıkan ya da kirada olup bugün dönen) → `rc-satir-bugun` (krem satır vurgusu). */
export function satirSinifi(satir: KiraListeSatiri, bugun: GunMetni): string | null {
  const g = satirGorunumu(satir, bugun);
  if (g.tur === 'bugunDonuyor') return 'rc-satir-bugun';
  return anParcala(satir.basTar)?.gun === bugun ? 'rc-satir-bugun' : null;
}

/** Durum rozeti sınıfı (§1.2): kirada yeşil, gecikmiş kırmızı, bugün sarı, kapalı nötr, iptal kırmızı. */
export function rozetSinifi(g: SatirGorunumu): string {
  switch (g.tur) {
    case 'gecikmis':
      return 'rc-rozet rc-rozet--hata';
    case 'bugunDonuyor':
      return 'rc-rozet rc-rozet--uyari';
    case 'durum':
      return `rc-rozet ${DURUM_ROZETI[g.durum] ?? ''}`.trimEnd();
  }
}

const DURUM_ROZETI: Readonly<Record<string, string>> = {
  Kirada: 'rc-rozet--basari',
  Tamamlandi: 'rc-rozet--notr',
  Iptal: 'rc-rozet--hata',
};
