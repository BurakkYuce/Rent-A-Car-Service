import {
  FormControl,
  FormGroup,
  Validators,
  type AbstractControl,
  type ValidationErrors,
  type ValidatorFn,
} from '@angular/forms';
import type { Sema } from '@core/api/ui-tipleri';
import {
  anBirlestir,
  anParcala,
  gunBicimle,
  gunEkle,
  type GunMetni,
} from '@core/form/tarih-girdisi';
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

/**
 * Sunucunun kabul edeceği en erken geçerlilik günü: geçerlilik günün İstanbul gece yarısı olarak gittiği için
 * başlangıç 00:00 değilse başlangıç günü REDDEDİLİR (gece yarısı < başlangıç) → ertesi gün. Başlangıç yoksa `null`.
 */
export function gecerlilikEnErken(basTar: string | null): GunMetni | null {
  const p = anParcala(basTar);
  if (!p) return null;
  return p.saat === '00:00' ? p.gun : gunEkle(p.gun, 1);
}

/**
 * İstemci doğrulaması — sunucu kuralının (`GecerlilikTarihi < BasTar` → red) birebir aynısı, aynı anlarla
 * karşılaştırır; istek gitmeden alanın altında açıklayıcı mesaj verir. Kardeş `basTar` kontrolünü okur (başlangıç
 * değişince çağıran `updateValueAndValidity` yapar). Mesaj çağırandan (çeviri).
 */
export function gecerlilikDogrulayici(mesaj: (enErken: string) => string): ValidatorFn {
  return (k: AbstractControl): ValidationErrors | null => {
    const gun = k.value as GunMetni | null;
    const bas = k.parent?.get('basTar')?.value as string | null | undefined;
    const an = gecerlilikAni(gun);
    if (!an || !bas) return null;
    if (Date.parse(an) >= Date.parse(bas)) return null;
    const enErken = gecerlilikEnErken(bas);
    return { gecerlilikErken: { mesaj: mesaj(enErken ? gunBicimle(enErken) : '') } };
  };
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
