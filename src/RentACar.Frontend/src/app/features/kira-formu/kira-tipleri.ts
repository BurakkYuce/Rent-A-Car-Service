import type { Sema } from '@core/api/ui-tipleri';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

/**
 * Kira formu (F4.3) sözleşme tipleri — `docs/api/ui-v1.json`'dan üretilen şemaların takma adları.
 * API'de alan değişirse `npm run tipler` sonrası bu dosyayı kullanan kod `typecheck`'te kırılır.
 */
/**
 * `KiraSozlesmesiDto` sınıfı `required` olmayan init özellikleri taşıdığı için şemada her alan
 * isteğe bağlı görünür; sunucu (varsayılan System.Text.Json, yok-sayma koşulu yok) HER alanı yazar —
 * null dahil. Burada o gerçek tipe daraltılır (`?` kalkar, `null` kalır).
 */
export type KiraSozlesmesi = Required<Sema<'KiraSozlesmesiDto'>>;
export type KiraDetayYaniti = Omit<Sema<'KiraDetayYaniti'>, 'kira'> & {
  readonly kira: KiraSozlesmesi;
};
export type KiraOlusturIstegi = Sema<'KiraOlusturIstegi'>;
export type KiraOlusturYaniti = Sema<'KiraOlusturYaniti'>;
export type KiraGuncelleIstegi = Sema<'KiraGuncelleIstegi'>;
export type KiraHesapSonuc = Sema<'KiraHesapSonuc'>;
export type KiraFormVarsayilanlari = Sema<'KiraFormVarsayilanlari'>;
export type MusaitArac = Sema<'MusaitAracDto'>;
export type KiraAraci = Sema<'KiraAracDto'>;
export type KiraDonusOnizleme = Sema<'KiraDonusOnizleme'>;
export type EkHizmetKalemi = Sema<'EkHizmetKalemiDto'>;
export type KiraEkHizmetYaniti = Sema<'KiraEkHizmetYaniti'>;
export type KaynakRezervasyonYaniti = Sema<'KaynakRezervasyonYaniti'>;
export type KarneOzeti = Sema<'KarneOzetiDto'>;
export type MusteriHizliIstegi = Sema<'MusteriHizliIstegi'>;
export type MusteriHizliYaniti = Sema<'MusteriHizliYaniti'>;
export type TeslimIstegi = Sema<'TeslimIstegi'>;
export type DonusIstegi = Sema<'DonusIstegi'>;
export type UzatIstegi = Sema<'UzatIstegi'>;
export type ProvizyonKapatIstegi = Sema<'ProvizyonKapatIstegi'>;
export type EkHizmetEkleIstegi = Sema<'EkHizmetEkleIstegi'>;

/** Sunucu sayıları `number | string` (NumberHandling); gösterim için sayıya çevrilir, HESAP YAPILMAZ. */
export type SunucuSayisi = number | string | null | undefined;

/**
 * Araç seçimi: F1.6 `secim/arac` öğesi (`plaka`, `grup`, `durum`) ya da müsait araç satırı (tam kart).
 * Kart alanları yalnız gösterim içindir; kayıt YALNIZ `id` ile yapılır.
 */
export interface AracSecenegi extends SecimSecenegi {
  readonly plaka?: string | null;
  readonly marka?: string | null;
  readonly tip?: string | null;
  readonly modelYili?: SunucuSayisi;
  readonly vites?: string | null;
  readonly yakit?: string | null;
  readonly grup?: string | null;
  readonly segment?: string | null;
  readonly km?: SunucuSayisi;
  readonly sube?: string | null;
  readonly konum?: string | null;
  readonly durum?: string | null;
}
