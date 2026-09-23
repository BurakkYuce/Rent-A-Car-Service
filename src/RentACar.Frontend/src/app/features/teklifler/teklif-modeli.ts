import { FormControl, FormGroup, Validators } from '@angular/forms';
import type { Sema } from '@core/api/ui-tipleri';
import { anBirlestir, type GunMetni } from '@core/form/tarih-girdisi';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

/** F5.1 teklif uçlarının sözleşme tipleri. */
export type TeklifListeSatiri = Sema<'TeklifListeSatiri'>;
export type TeklifDto = Sema<'TeklifDto'>;
export type TeklifDetayYaniti = Sema<'TeklifDetayYaniti'>;
export type TeklifIstegi = Sema<'TeklifIstegi'>;
export type TeklifOlusturYaniti = Sema<'TeklifOlusturYaniti'>;
export type TeklifKabulYaniti = Sema<'TeklifKabulYaniti'>;

export const TEKLIF_KOKU = '/api/ui/v1/teklifler';

/** Sunucu enum ADLARI (`QuotationStatus`: Taslak → Gonderildi → Kabul/Red). */
export const TEKLIF_DURUMLARI = ['Taslak', 'Gonderildi', 'Kabul', 'Red'] as const;
export type TeklifDurumu = (typeof TEKLIF_DURUMLARI)[number];

export function teklifDurumuMu(d: string): d is TeklifDurumu {
  return (TEKLIF_DURUMLARI as readonly string[]).includes(d);
}

const ROZET: Readonly<Record<TeklifDurumu, string>> = {
  Taslak: '',
  Gonderildi: 'rc-rozet--bilgi',
  Kabul: 'rc-rozet--basari',
  Red: 'rc-rozet--hata',
};

export function teklifRozeti(durum: string): string {
  return `rc-rozet ${teklifDurumuMu(durum) ? ROZET[durum] : ''}`.trim();
}

/** Açık teklif (kabul/red edilebilir) — sunucunun `yetkiler` kuralıyla aynı; liste satırında yetki alanı yok. */
export function teklifAcik(durum: string): boolean {
  return durum === 'Taslak' || durum === 'Gonderildi';
}

/**
 * Yeni teklif formu — Blazor QuotationList "+ Yeni Teklif" alanları birebir (müşteri, araç, başlangıç, bitiş, fiyat
 * türü, günlük ücret, geçerlilik, çıkış/dönüş ofisi). Blazor'da teklif düzenleme yok → sunucuda PUT yok.
 */
export function teklifFormuOlustur() {
  return new FormGroup({
    musteri: new FormControl<SecimSecenegi | null>(null, Validators.required),
    arac: new FormControl<SecimSecenegi | null>(null, Validators.required),
    basTar: new FormControl<string | null>(null, Validators.required),
    bitTar: new FormControl<string | null>(null, Validators.required),
    fiyatTuru: new FormControl<string | null>(null),
    gunlukUcret: new FormControl<string | number | null>(null),
    gecerlilik: new FormControl<GunMetni | null>(null),
    cikisOfisi: new FormControl<SecimSecenegi | null>(null),
    donusOfisi: new FormControl<SecimSecenegi | null>(null),
  });
}

export type TeklifFormu = ReturnType<typeof teklifFormuOlustur>;
export type TeklifFormDegeri = ReturnType<TeklifFormu['getRawValue']>;

/**
 * Geçerlilik takvim günü → an: günün İstanbul gece yarısı (Blazor `FormParse.Date(gecerlilik)` ile aynı gün
 * başı). Sunucu kuralı: geçerlilik başlangıçtan önce olamaz.
 */
export function gecerlilikAni(gun: GunMetni | null): string | null {
  return gun ? anBirlestir(gun, '00:00') : null;
}

/** Doğrulayıcının garanti ettiği zorunlu değer; yoksa programlama hatası. */
function zorunlu<T>(v: T | null | undefined, alan: string): T {
  if (v === null || v === undefined || v === '') {
    throw new Error(`Teklif formu: zorunlu alan boş gönderilemez (${alan}).`);
  }
  return v;
}

/** `POST /teklifler` gövdesi. Günlük ücret boş → tarife (fiyat motoru); para invariant metin AYNEN. */
export function teklifGovdesi(d: TeklifFormDegeri): TeklifIstegi {
  const ucret = d.gunlukUcret;
  return {
    musteriId: zorunlu(d.musteri?.id, 'musteri'),
    vehicleId: zorunlu(d.arac?.id, 'arac'),
    basTar: zorunlu(d.basTar, 'basTar'),
    bitTar: zorunlu(d.bitTar, 'bitTar'),
    gunlukUcret: ucret === '' ? null : ucret,
    fiyatTuru: d.fiyatTuru?.trim() ? d.fiyatTuru.trim() : null,
    cikisOfisi: d.cikisOfisi?.etiket ?? null,
    donusOfisi: d.donusOfisi?.etiket ?? null,
    gecerlilikTarihi: gecerlilikAni(d.gecerlilik),
  };
}
