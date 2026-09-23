import type { Sema } from '@core/api/ui-tipleri';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { listeTanimi } from '@core/veri/liste-sorgusu';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';

export type FiloListeSatiri = Sema<'FiloListeSatiri'>;
export type FiloKiralama = Sema<'FiloKiralamaDto'>;
export type FiloKiralamaIstegi = Sema<'FiloKiralamaIstegi'>;
export type FiloKunyeIstegi = Sema<'FiloKunyeIstegi'>;
export type FiloOlusturYaniti = Sema<'FiloOlusturYaniti'>;
export type FiloTaksit = Sema<'FiloTaksitDto'>;

/** Sunucu enum ADLARI (`FiloKiraDurum`); tanımsız ad 400. */
export const FILO_DURUMLARI = ['Aktif', 'Tamamlandi', 'Iptal'] as const;
export type FiloDurumu = (typeof FILO_DURUMLARI)[number];

/** Blazor datalist önerileri (serbest metin de kabul). */
export const FATURA_TURU_ONERILERI = ['Dönem', 'Kırık'] as const;
export const FIYAT_TURU_ONERILERI = ['Aylık', '30 Gün Aylık', 'KDV Dahil', '30 Gün Dahil'] as const;

/**
 * Filo kiralama listesi URL ↔ API sözleşmesi (`GET /api/ui/v1/filo-kiralama`): Blazor FAZ-21 arama
 * paneli (müşteri, plaka, serbest arama, durum, başlangıç günü aralığı) + sunucu sayfalama/sıralama.
 */
export const FILO_LISTESI = listeTanimi({
  filtreler: {
    musteriId: { tur: 'kimlik' },
    plaka: { tur: 'metin', enFazla: 32 },
    ara: { tur: 'metin', enFazla: 100 },
    durum: { tur: 'secim', degerler: FILO_DURUMLARI },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: [
    'no',
    'musteri',
    'plaka',
    'basTar',
    'sureAy',
    'aylikUcret',
    'genelToplam',
    'durum',
  ],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

export function filoDurumu(deger: string): FiloDurumu | null {
  return (FILO_DURUMLARI as readonly string[]).includes(deger) ? (deger as FiloDurumu) : null;
}

/** KÜNYE alanları (para/süre YOK — taksit planı bu yoldan değişemez). */
export interface FiloKunyeDegeri {
  readonly sozlesmeNo: string | null;
  readonly makbuzNo: string | null;
  readonly dosyaNo: string | null;
  readonly sozlesmeTarihi: GunMetni | null;
  readonly imzaTarih: GunMetni | null;
  readonly satisTemsilcisi: string | null;
  readonly faturaTuru: string | null;
  readonly fiyatTuru: string | null;
  readonly kaynak: string | null;
  readonly vadeGun: number | null;
  readonly toplamKmLimiti: number | null;
  readonly cikisKm: number | null;
  readonly toplamKm: number | null;
  readonly aciklama: string | null;
}

/** Yeni sözleşme formu: künye + müşteri/araç/başlangıç/süre/ücret/KDV/damga. */
export interface FiloYeniDegeri extends FiloKunyeDegeri {
  readonly musteri: SecimSecenegi | null;
  readonly arac: SecimSecenegi | null;
  readonly basTar: GunMetni | null;
  readonly sureAy: number | null;
  readonly aylikUcret: string | null;
  /** Kesir (0,20 = %20) — Blazor formuyla aynı. */
  readonly kdvOrani: number | null;
  readonly damgaVergisi: string | null;
}

const sayiDegeri = (d: number | string | null | undefined): number | null =>
  d === null || d === undefined || d === '' ? null : Number(d);

/** Kayıt → künye form değeri. */
export function kunyeDegerleri(k: FiloKiralama): FiloKunyeDegeri {
  return {
    sozlesmeNo: k.sozlesmeNo,
    makbuzNo: k.makbuzNo,
    dosyaNo: k.dosyaNo,
    sozlesmeTarihi: gunDegeri(k.sozlesmeTarihi),
    imzaTarih: gunDegeri(k.imzaTarih),
    satisTemsilcisi: k.satisTemsilcisi,
    faturaTuru: k.faturaTuru,
    fiyatTuru: k.fiyatTuru,
    kaynak: k.kaynak,
    vadeGun: sayiDegeri(k.vadeGun),
    toplamKmLimiti: sayiDegeri(k.toplamKmLimiti),
    cikisKm: sayiDegeri(k.cikisKm),
    toplamKm: sayiDegeri(k.toplamKm),
    aciklama: k.aciklama,
  };
}

export const BOS_KUNYE: FiloKunyeDegeri = {
  sozlesmeNo: null,
  makbuzNo: null,
  dosyaNo: null,
  sozlesmeTarihi: null,
  imzaTarih: null,
  satisTemsilcisi: null,
  faturaTuru: null,
  fiyatTuru: null,
  kaynak: null,
  vadeGun: null,
  toplamKmLimiti: null,
  cikisKm: null,
  toplamKm: null,
  aciklama: null,
};

export function yeniDegerler(): FiloYeniDegeri {
  return {
    ...BOS_KUNYE,
    musteri: null,
    arac: null,
    basTar: null,
    sureAy: null,
    aylikUcret: null,
    kdvOrani: 0.2,
    damgaVergisi: null,
  };
}

/**
 * Künye → `PUT /filo-kiralama/{id}/kunye` tam değiştirme gövdesi (`surum` zorunlu). Dokunulmayan tarih
 * sunucunun anıyla AYNEN gider: belge tarihi sınırı yalnız DEĞİŞEN tarihe uygulanır (#271 Low-1), gün
 * yuvarlaması eski sözleşmenin tarihini kaydırıp sınıra takmaz.
 */
export function kunyeGovdesi(v: FiloKunyeDegeri, taban: FiloKiralama): FiloKunyeIstegi {
  return {
    surum: taban.surum ?? null,
    sozlesmeNo: metinDegeri(v.sozlesmeNo),
    makbuzNo: metinDegeri(v.makbuzNo),
    dosyaNo: metinDegeri(v.dosyaNo),
    sozlesmeTarihi: anDegeri(v.sozlesmeTarihi, taban.sozlesmeTarihi),
    imzaTarih: anDegeri(v.imzaTarih, taban.imzaTarih),
    satisTemsilcisi: metinDegeri(v.satisTemsilcisi),
    faturaTuru: metinDegeri(v.faturaTuru),
    fiyatTuru: metinDegeri(v.fiyatTuru),
    kaynak: metinDegeri(v.kaynak),
    vadeGun: v.vadeGun,
    toplamKmLimiti: v.toplamKmLimiti,
    cikisKm: v.cikisKm,
    toplamKm: v.toplamKm,
    aciklama: metinDegeri(v.aciklama),
  };
}

/** Yeni sözleşme → `POST /filo-kiralama` gövdesi. Döviz/kur formda yok (Blazor gibi TRY, kur 1). */
export function olusturGovdesi(v: FiloYeniDegeri): FiloKiralamaIstegi {
  return {
    musteriId: v.musteri?.id ?? '',
    vehicleId: v.arac?.id ?? '',
    basTar: anDegeri(v.basTar, null),
    sureAy: v.sureAy,
    aylikUcret: v.aylikUcret,
    kdvOrani: v.kdvOrani,
    damgaVergisi: v.damgaVergisi,
    toplamKmLimiti: v.toplamKmLimiti,
    aciklama: metinDegeri(v.aciklama),
    sozlesmeNo: metinDegeri(v.sozlesmeNo),
    makbuzNo: metinDegeri(v.makbuzNo),
    dosyaNo: metinDegeri(v.dosyaNo),
    sozlesmeTarihi: anDegeri(v.sozlesmeTarihi, null),
    imzaTarih: anDegeri(v.imzaTarih, null),
    satisTemsilcisi: metinDegeri(v.satisTemsilcisi),
    faturaTuru: metinDegeri(v.faturaTuru),
    fiyatTuru: metinDegeri(v.fiyatTuru),
    kaynak: metinDegeri(v.kaynak),
    vadeGun: v.vadeGun,
    cikisKm: v.cikisKm,
    toplamKm: v.toplamKm,
  };
}

/** JSON sayısı (`number | string`) → gösterim sayısı. YALNIZ gösterim. */
export function sayi(deger: number | string | null | undefined): number | null {
  if (typeof deger === 'number') return Number.isFinite(deger) ? deger : null;
  if (typeof deger === 'string' && deger.trim() !== '') {
    const n = Number(deger);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}
