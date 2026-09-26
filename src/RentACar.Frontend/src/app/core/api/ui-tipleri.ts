/**
 * `/api/ui/v1` sözleşme tipleri — ELLE YAZILMAZ, `uretilen/ui-v1.ts`'ten türetilir.
 *
 * Zincir: .NET testi `docs/api/ui-v1.json`'u canlı OpenAPI ile kilitler → `npm run tipler` bu dosyadan
 * `uretilen/ui-v1.ts`'i üretir (CI yeniden üretip fark görürse kırmızı) → buradaki takma adlar ve onları
 * kullanan kod API'deki kırıcı bir değişiklikte `npm run typecheck`'te derleme hatası verir.
 */
import type { components, paths } from './uretilen/ui-v1';

type Semalar = components['schemas'];

/**
 * Üretilen şemaya ad ile erişim (`Sema<'KiraDetayYaniti'>`). Özellikler kendi takma adlarını kendi
 * klasörlerinde bu yolla kurar; üretilen dosya doğrudan içe aktarılmaz.
 */
export type Schema<A extends keyof Semalar> = Semalar[A];

/** `GET /api/ui/v1/oturum/ben` 200 gövdesi (oturumdaki kullanıcı, kiracı, izinler, şube kapsamı). */
export type MeResponse =
  paths['/api/ui/v1/oturum/ben']['get']['responses'][200]['content']['application/json'];

/** `POST /api/ui/v1/oturum/giris` gövdesi. */
export type LoginRequest =
  paths['/api/ui/v1/oturum/giris']['post']['requestBody']['content']['application/json'];

export type MenuResponse = Semalar['MenuYaniti'];
/** `POST /api/ui/v1/istemci-hata` gövdesi (F3.3). */
export type ClientErrorRequest = Semalar['IstemciHataIstegi'];
export type SelectionItem = Semalar['SecimOgesi'];

/** `GET /api/ui/v1/panel/ozet` (F4.1): KPI, vade, kovalı dönüş/çıkış, finans (yalnız ViewReports'ta dolu). */
export type PanelSummaryResponse = Semalar['PanelOzetiYaniti'];
export type PanelReturnRow = Semalar['PanelDonusSatiri'];
export type PanelPickupRow = Semalar['PanelCikisSatiri'];
export type PanelFinance = Semalar['PanelFinans'];

type SelectionPath = Extract<keyof paths, `/api/ui/v1/secim/${string}`>;
/**
 * F1.6 typeahead uçları: `musteri`, `arac`, `lokasyon`, … (sözleşmeden türetilir). Kimlikle tek öğe uçları
 * (`musteri/{id}`, `arac/{id}` — F4.3b) liste döndürmez; typeahead kümesine GİRMEZ.
 */
export type SelectionEndpoint = SelectionPath extends `/api/ui/v1/secim/${infer U}`
  ? U extends `${string}/${string}`
    ? never
    : U
  : never;
/** Bir seçim ucunun döndürdüğü öğe (`MusteriSecimOgesi`, `AracSecimOgesi`, `SecimOgesi`…). */
export type SelectionEndpointItem<U extends SelectionEndpoint> =
  paths[`/api/ui/v1/secim/${U}`]['get']['responses'][200]['content']['application/json'][number];

/** `GET/PUT /api/ui/v1/tablo-duzenleri/{tabloKodu}` yanıtı (kullanıcının kayıtlı tablo düzeni; yoksa `duzen: null`). */
export type TableLayoutResponse = Semalar['TabloDuzeniYaniti'];
/** `PUT /api/ui/v1/tablo-duzenleri/{tabloKodu}` gövdesi. */
export type TableLayoutData = Semalar['TabloDuzeniVerisi'];

// ---- F4.2 kira listesi (`GET /api/ui/v1/kiralar`, `/ozet`, `/filtre-secenekleri`)
export type RentalListRow = Semalar['KiraListeSatiri'];
/** Satırın "Tahsil Et" verisi; `anahtar` sunucunun deterministik `TahsilatAnahtar`'ı (geri gönderilir). */
export type CollectionInfo = Semalar['TahsilatBilgisi'];
export type RentalListSummary = Semalar['KiraListeOzeti'];
export type RentalFilterOptions = Semalar['KiraFiltreSecenekleri'];
// F4.4a finans uçları (F4.2 "Tahsil Et" kullanır; F4.4/F8 aynı tipleri paylaşır)
export type CollectionRequest = Semalar['TahsilatIstegi'];
export type FinanceTransactionResponse = Semalar['FinansIslemYaniti'];
export type FinanceAccountItem = Semalar['FinansHesapOgesi'];

/** Sunucunun verdiği izin listesinde birebir eşleşme (izin adları sabit İngilizce enum adlarıdır). */
export function izinVar(ben: MeResponse, permission: string): boolean {
  return ben.izinler.includes(permission);
}

/** Üst çubuktaki şube etiketi: tüm şubeler, atanmış şube ya da atanmamış. */
export function branchLabel(ben: MeResponse): string {
  const scope = ben.subeKapsami;
  if (scope.tumSubeler) return 'Tüm şubeler';
  return scope.subeAd ?? 'Şube atanmamış';
}
