import type { Schema } from '@core/api/ui-tipleri';
import type { SecimSecenegi } from '@shared/form/arama-secim/selection-source';

/**
 * Kira formu (F4.3) sözleşme tipleri — `docs/api/ui-v1.json`'dan üretilen şemaların takma adları.
 * API'de alan değişirse `npm run tipler` sonrası bu dosyayı kullanan kod `typecheck`'te kırılır.
 */
/**
 * `KiraSozlesmesiDto` sınıfı `required` olmayan init özellikleri taşıdığı için şemada her alan
 * isteğe bağlı görünür; sunucu (varsayılan System.Text.Json, yok-sayma koşulu yok) HER alanı yazar —
 * null dahil. Burada o gerçek tipe daraltılır (`?` kalkar, `null` kalır).
 */
export type RentalContract = Required<Schema<'KiraSozlesmesiDto'>>;
export type RentalDetailResponse = Omit<Schema<'KiraDetayYaniti'>, 'kira'> & {
  readonly kira: RentalContract;
};
export type CreateRentalRequest = Schema<'KiraOlusturIstegi'>;
export type CreateRentalResponse = Schema<'KiraOlusturYaniti'>;
export type UpdateRentalRequest = Schema<'KiraGuncelleIstegi'>;
export type RentalCalculationResult = Schema<'KiraHesapSonuc'>;
export type RentalFormDefaults = Schema<'KiraFormVarsayilanlari'>;
export type AvailableVehicle = Schema<'MusaitAracDto'>;
export type RentalVehicle = Schema<'KiraAracDto'>;
export type RentalReturnPreview = Schema<'KiraDonusOnizleme'>;
export type AddOnItem = Schema<'EkHizmetKalemiDto'>;
export type RentalAddOnResponse = Schema<'KiraEkHizmetYaniti'>;
export type SourceReservationResponse = Schema<'KaynakRezervasyonYaniti'>;
export type ScorecardSummary = Schema<'KarneOzetiDto'>;
export type QuickCustomerRequest = Schema<'MusteriHizliIstegi'>;
export type QuickCustomerResponse = Schema<'MusteriHizliYaniti'>;
export type DeliveryRequest = Schema<'TeslimIstegi'>;
export type ReturnRequest = Schema<'DonusIstegi'>;
export type ExtendRequest = Schema<'UzatIstegi'>;
export type ClosePreAuthRequest = Schema<'ProvizyonKapatIstegi'>;
export type AddAddOnRequest = Schema<'EkHizmetEkleIstegi'>;
// F4.3b parite ekleri
export type RentalCustomerSummary = Schema<'KiraMusteriOzeti'>;
export type RentalTotals = Schema<'KiraToplamlari'>;
export type RentalAddOnCatalog = Schema<'KiraEkHizmetKatalogu'>;
export type AddOnCatalogItem = Schema<'EkHizmetKatalogOgesi'>;
/** `GET /secim/musteri/{id}` — arama ucunun öğesiyle aynı (kimlik + ad + tip; PII yok). */
export type SelectionCustomer = Schema<'MusteriSecimOgesi'>;
/** `GET /secim/arac/{id}` — arama ucunun öğesiyle aynı (plaka, grup, durum). */
export type SelectionVehicle = Schema<'AracSecimOgesi'>;

/** Sunucu sayıları `number | string` (NumberHandling); gösterim için sayıya çevrilir, HESAP YAPILMAZ. */
export type ServerNumber = number | string | null | undefined;

/**
 * Araç seçimi: F1.6 `secim/arac` öğesi (`plaka`, `grup`, `durum`) ya da müsait araç satırı (tam kart).
 * Kart alanları yalnız gösterim içindir; kayıt YALNIZ `id` ile yapılır.
 */
export interface AracSecenegi extends SecimSecenegi {
  readonly plaka?: string | null;
  readonly marka?: string | null;
  readonly tip?: string | null;
  readonly modelYili?: ServerNumber;
  readonly vites?: string | null;
  readonly yakit?: string | null;
  readonly grup?: string | null;
  readonly segment?: string | null;
  readonly km?: ServerNumber;
  readonly sube?: string | null;
  readonly konum?: string | null;
  readonly durum?: string | null;
}
