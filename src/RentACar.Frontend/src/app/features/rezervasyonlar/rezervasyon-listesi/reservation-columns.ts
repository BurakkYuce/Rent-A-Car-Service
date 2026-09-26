import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

import { toNumber, type ReservationListRow } from '../rezervasyon-modeli';

type Translation = (key: CeviriAnahtari, parameters?: Record<string, unknown>) => string;

/** Sütun kodları (= `rezervasyon.sutun.*` çeviri anahtarları; eksik çeviri derleme hatası). KALICI. */
export type ReservationColumn =
  | 'no'
  | 'musteri'
  | 'cepTel'
  | 'plaka'
  | 'basTar'
  | 'bitTar'
  | 'cikisOfisi'
  | 'kaynak'
  | 'talepTuru'
  | 'geldigiBirim'
  | 'projeAdi'
  | 'onayKodu'
  | 'gun'
  | 'tutar'
  | 'durum'
  | 'islemler';

/**
 * Blazor ReservationList sütunlarının tamamı, aynı sırayla (FAZ-48). Sıralanabilir sütunlar sunucunun
 * `SiralamaHaritasi` beyaz listesiyle aynı. Ad ve telefon sunucuda KVKK kuralından geçmiş hâliyle gelir
 * (`MusteriGorunumu`: AnonimAd/AnonimTelefon). Tutar rezervasyonun kendi (TL) tutarıdır; rezervasyonda döviz yok.
 */
export function reservationColumns(t: Translation): readonly TabloSutunu<ReservationListRow>[] {
  const s = (code: ReservationColumn) => t(`rezervasyon.sutun.${code}`);
  return [
    { kod: 'no', baslik: s('no'), deger: (r) => r.no, sirala: true, sabit: true, genislik: 140 },
    {
      kod: 'musteri',
      baslik: s('musteri'),
      deger: (r) => r.musteriAd,
      sirala: true,
      genislik: 180,
    },
    { kod: 'cepTel', baslik: s('cepTel'), deger: (r) => r.cepTel, genislik: 120 },
    { kod: 'plaka', baslik: s('plaka'), deger: (r) => r.plaka, sirala: true, genislik: 124 },
    {
      kod: 'basTar',
      baslik: s('basTar'),
      deger: (r) => r.basTar,
      tur: 'tarihSaat',
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'bitTar',
      baslik: s('bitTar'),
      deger: (r) => r.bitTar,
      tur: 'tarihSaat',
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'cikisOfisi',
      baslik: s('cikisOfisi'),
      deger: (r) => r.cikisOfisi,
      sirala: true,
      genislik: 130,
    },
    { kod: 'kaynak', baslik: s('kaynak'), deger: (r) => r.kaynak, sirala: true, genislik: 120 },
    { kod: 'talepTuru', baslik: s('talepTuru'), deger: (r) => r.talepTuru, genislik: 110 },
    { kod: 'geldigiBirim', baslik: s('geldigiBirim'), deger: (r) => r.geldigiBirim, genislik: 120 },
    { kod: 'projeAdi', baslik: s('projeAdi'), deger: (r) => r.projeAdi, genislik: 120 },
    { kod: 'onayKodu', baslik: s('onayKodu'), deger: (r) => r.onayKodu, genislik: 110 },
    {
      kod: 'gun',
      baslik: s('gun'),
      deger: (r) => toNumber(r.gun),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 64,
    },
    {
      kod: 'tutar',
      baslik: s('tutar'),
      deger: (r) => toNumber(r.tutar),
      tur: 'para',
      sirala: true,
      genislik: 120,
    },
    { kod: 'durum', baslik: s('durum'), deger: (r) => r.durum, sirala: true, genislik: 130 },
    { kod: 'islemler', baslik: s('islemler'), deger: () => null, gizlenemez: true, genislik: 250 },
  ];
}
