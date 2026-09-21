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
export type SecimOgesi = Semalar['SecimOgesi'];

type SecimYolu = Extract<keyof paths, `/api/ui/v1/secim/${string}`>;
/** F1.6 typeahead uçları: `musteri`, `arac`, `lokasyon`, … (sözleşmeden türetilir). */
export type SecimUcu = SecimYolu extends `/api/ui/v1/secim/${infer U}` ? U : never;
/** Bir seçim ucunun döndürdüğü öğe (`MusteriSecimOgesi`, `AracSecimOgesi`, `SecimOgesi`…). */
export type SecimUcuOgesi<U extends SecimUcu> =
  paths[`/api/ui/v1/secim/${U}`]['get']['responses'][200]['content']['application/json'][number];

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
