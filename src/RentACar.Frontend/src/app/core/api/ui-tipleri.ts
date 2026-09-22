/**
 * `/api/ui/v1` sözleşme tipleri — ELLE YAZILMAZ, `uretilen/ui-v1.ts`'ten türetilir.
 *
 * Zincir: .NET testi `docs/api/ui-v1.json`'u canlı OpenAPI ile kilitler → `npm run tipler` bu dosyadan
 * `uretilen/ui-v1.ts`'i üretir (CI yeniden üretip fark görürse kırmızı) → buradaki takma adlar ve onları
 * kullanan kod API'deki kırıcı bir değişiklikte `npm run typecheck`'te derleme hatası verir.
 */
import type { components, paths } from './uretilen/ui-v1';

type Semalar = components['schemas'];

/** `GET /api/ui/v1/oturum/ben` 200 gövdesi (oturumdaki kullanıcı, kiracı, izinler, şube kapsamı). */
export type BenYaniti =
  paths['/api/ui/v1/oturum/ben']['get']['responses'][200]['content']['application/json'];

/** `POST /api/ui/v1/oturum/giris` gövdesi. */
export type GirisIstegi =
  paths['/api/ui/v1/oturum/giris']['post']['requestBody']['content']['application/json'];

export type MenuYaniti = Semalar['MenuYaniti'];
/** `POST /api/ui/v1/istemci-hata` gövdesi (F3.3). */
export type IstemciHataIstegi = Semalar['IstemciHataIstegi'];
export type SecimOgesi = Semalar['SecimOgesi'];

/** `GET /api/ui/v1/panel/ozet` (F4.1): KPI, vade, kovalı dönüş/çıkış, finans (yalnız ViewReports'ta dolu). */
export type PanelOzetiYaniti = Semalar['PanelOzetiYaniti'];
export type PanelDonusSatiri = Semalar['PanelDonusSatiri'];
export type PanelCikisSatiri = Semalar['PanelCikisSatiri'];
export type PanelFinans = Semalar['PanelFinans'];
/** Satır başına "Tahsil Et" verisi; `anahtar` sunucunun deterministik `TahsilatAnahtar`'ı (FinanceWrite'ta dolu). */
export type PanelTahsilatBilgisi = Semalar['TahsilatBilgisi'];
/** `POST /api/ui/v1/finans/tahsilat` gövdesi (F4.4a) ve `GET finans/hesaplar` öğesi. */
export type FinansTahsilatIstegi = Semalar['TahsilatIstegi'];
export type FinansHesapOgesi = Semalar['FinansHesapOgesi'];

type SecimYolu = Extract<keyof paths, `/api/ui/v1/secim/${string}`>;
/** F1.6 typeahead uçları: `musteri`, `arac`, `lokasyon`, … (sözleşmeden türetilir). */
export type SecimUcu = SecimYolu extends `/api/ui/v1/secim/${infer U}` ? U : never;
/** Bir seçim ucunun döndürdüğü öğe (`MusteriSecimOgesi`, `AracSecimOgesi`, `SecimOgesi`…). */
export type SecimUcuOgesi<U extends SecimUcu> =
  paths[`/api/ui/v1/secim/${U}`]['get']['responses'][200]['content']['application/json'][number];

/** `GET/PUT /api/ui/v1/tablo-duzenleri/{tabloKodu}` yanıtı (kullanıcının kayıtlı tablo düzeni; yoksa `duzen: null`). */
export type TabloDuzeniYaniti = Semalar['TabloDuzeniYaniti'];
/** `PUT /api/ui/v1/tablo-duzenleri/{tabloKodu}` gövdesi. */
export type TabloDuzeniVerisi = Semalar['TabloDuzeniVerisi'];

/** Sunucunun verdiği izin listesinde birebir eşleşme (izin adları sabit İngilizce enum adlarıdır). */
export function izinVar(ben: BenYaniti, izin: string): boolean {
  return ben.izinler.includes(izin);
}

/** Üst çubuktaki şube etiketi: tüm şubeler, atanmış şube ya da atanmamış. */
export function subeEtiketi(ben: BenYaniti): string {
  const kapsam = ben.subeKapsami;
  if (kapsam.tumSubeler) return 'Tüm şubeler';
  return kapsam.subeAd ?? 'Şube atanmamış';
}
