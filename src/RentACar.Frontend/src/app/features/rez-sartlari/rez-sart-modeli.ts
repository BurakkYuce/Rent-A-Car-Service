import type { SorguParametreleri } from '@core/api/api-istemcisi';
import type { Sema } from '@core/api/ui-tipleri';
import type { GunMetni } from '@core/form/tarih-girdisi';
import { listeTanimi } from '@core/veri/liste-sorgusu';
import type { SecimSecenegi } from '@shared/form/arama-secim/secim-kaynagi';

import { anDegeri, gunDegeri, metinDegeri } from '@features/planlama-ortak/form-yardimcilari';

export type RezSart = Sema<'RezSartDto'>;
export type RezSartIstegi = Sema<'RezSartIstegi'>;
export type RezSartGuncelleIstegi = Sema<'RezSartGuncelleIstegi'>;

/** API `durum` değerleri (`RezSartListeFiltresi`; tanımsız değer 400). */
export const REZ_SART_DURUMLARI = ['bekleyen', 'karsilanan'] as const;
export type RezSartDurumu = (typeof REZ_SART_DURUMLARI)[number];

/**
 * Rez şartı listesi URL ↔ API sözleşmesi (`GET /api/ui/v1/rez-sartlari`): Blazor süzgeçleri (müşteri,
 * durum, talep günü aralığı) + sunucu sayfalama/sıralama (`SiralamaHaritasi`). Varsayılan sıra sunucunun
 * (Blazor listesinin) sırası.
 */
export const REZ_SART_LISTESI = listeTanimi({
  filtreler: {
    musteriId: { tur: 'kimlik' },
    durum: { tur: 'secim', degerler: REZ_SART_DURUMLARI },
    bas: { tur: 'tarih' },
    bit: { tur: 'tarih' },
  },
  siralanabilir: ['talepTarihi', 'musteri', 'grup', 'sart', 'karsilamaTarihi'],
  varsayilanSirala: null,
  varsayilanBoyut: 50,
});

/** "N bekleyen" sayacı: aynı süzgeçler + `durum=bekleyen`, tek kayıtlık sayfa (yalnız `toplam` okunur). */
export function bekleyenParametreleri(p: SorguParametreleri): SorguParametreleri | null {
  if (p['durum'] === 'karsilanan') return null; // süzgeç karşılananlar: bekleyen 0
  const sonuc: Record<string, SorguParametreleri[string]> = {};
  for (const [ad, deger] of Object.entries(p)) {
    if (ad !== 'sayfa' && ad !== 'boyut' && ad !== 'sirala') sonuc[ad] = deger;
  }
  return { ...sonuc, durum: 'bekleyen', sayfa: 1, boyut: 1 };
}

/** Form değerleri (Blazor oluştur/düzenle formunun alanları). Tarihler İstanbul takvim günü. */
export interface RezSartFormDegeri {
  readonly musteri: SecimSecenegi | null;
  readonly sart: string | null;
  readonly grup: string | null;
  readonly basTar: GunMetni | null;
  readonly bitTar: GunMetni | null;
  readonly talepTarihi: GunMetni | null;
  readonly karsilamaTarihi: GunMetni | null;
  readonly teslimEden: string | null;
}

/** Kayıt → form değeri. */
export function kayittanDegerler(s: RezSart): RezSartFormDegeri {
  return {
    musteri: { id: s.musteriId, etiket: s.musteriAd },
    sart: s.sart,
    grup: s.grup,
    basTar: gunDegeri(s.basTar),
    bitTar: gunDegeri(s.bitTar),
    talepTarihi: gunDegeri(s.talepTarihi),
    karsilamaTarihi: gunDegeri(s.karsilamaTarihi),
    teslimEden: s.teslimEden,
  };
}

/** Yeni kayıt formu: talep tarihi bugün (Blazor varsayılanı). */
export function yeniDegerler(bugun: GunMetni): RezSartFormDegeri {
  return {
    musteri: null,
    sart: null,
    grup: null,
    basTar: null,
    bitTar: null,
    talepTarihi: bugun,
    karsilamaTarihi: null,
    teslimEden: null,
  };
}

/**
 * Form → `POST` gövdesi (yeni) ya da `PUT` tam değiştirme gövdesi (düzenleme). PUT'ta bağ kimlikleri
 * (`reservationId`/`quotationId`) tabandan AYNEN geri gider (tam değiştirme; ekranda düzenlenmez) ve
 * `surum` zorunludur. Dokunulmayan tarih sunucunun anıyla gider (gün yuvarlaması yok). Oluşturmada
 * karşılama tarihi gönderilmez (Blazor oluştur formunda yok).
 */
export function rezSartGovdesi(v: RezSartFormDegeri, taban: RezSart | null): RezSartGuncelleIstegi {
  const govde: RezSartIstegi = {
    musteriId: v.musteri?.id ?? '',
    sart: metinDegeri(v.sart),
    grup: metinDegeri(v.grup),
    basTar: anDegeri(v.basTar, taban?.basTar),
    bitTar: anDegeri(v.bitTar, taban?.bitTar),
    talepTarihi: anDegeri(v.talepTarihi, taban?.talepTarihi),
    karsilamaTarihi: taban === null ? null : anDegeri(v.karsilamaTarihi, taban.karsilamaTarihi),
    teslimEden: metinDegeri(v.teslimEden),
    reservationId: taban?.reservationId ?? null,
    quotationId: taban?.quotationId ?? null,
  };
  return taban === null ? govde : { ...govde, surum: taban.surum ?? null };
}
