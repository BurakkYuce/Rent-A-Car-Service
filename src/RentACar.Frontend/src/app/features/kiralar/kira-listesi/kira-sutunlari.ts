import type { KiraListeSatiri } from '@core/api/ui-tipleri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import type { TabloSutunu } from '@shared/tablo/tablo-modeli';

type Ceviri = (anahtar: CeviriAnahtari, parametreler?: Record<string, unknown>) => string;

/** Sütun kodları (= `tr.json` `kiraListesi.sutun.*` anahtarları; eksik çeviri derleme hatası). */
export type SutunKodu =
  | 'sozlesmeNo'
  | 'musteri'
  | 'plaka'
  | 'basTar'
  | 'bitTar'
  | 'vadeTar'
  | 'gun'
  | 'hediyeGun'
  | 'faturalananGun'
  | 'tutar'
  | 'bakiye'
  | 'kaynak'
  | 'cikisOfisi'
  | 'donusOfisi'
  | 'provizyon'
  | 'depozito'
  | 'komisyonOran'
  | 'komisyonTutar'
  | 'onayKodu'
  | 'projeAdi'
  | 'assistFirma'
  | 'ozelSofor'
  | 'durum'
  | 'fatura'
  | 'islemler';

/**
 * JSON sayısı (sözleşmede `number | string`: sunucu sayı yazar, metni de okur) → gösterim sayısı.
 * YALNIZ GÖSTERİM içindir; sunucuya giden tutar kayan noktaya girmez (`invariantOndalik`).
 */
export function sayi(deger: number | string | null | undefined): number | null {
  if (typeof deger === 'number') return Number.isFinite(deger) ? deger : null;
  if (typeof deger === 'string' && deger.trim() !== '') {
    const n = Number(deger);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

/** Kiranın para birimi (boş → TRY). Tutar ve bakiye bu birimle yazılır. */
export function kiraDovizi(satir: KiraListeSatiri): string {
  const kod = satir.doviz?.trim();
  return kod ? kod : 'TRY';
}

/**
 * Blazor RentalList (FAZ-46 canlı `kira_listesi.aspx`) sütunlarının tamamı. `kod`'lar KALICI (kullanıcı
 * düzeni `kiralar.liste` bunlarla eşlenir). Sıralanabilir sütunlar sunucunun `SiralamaHaritasi`
 * beyaz listesiyle aynı. Provizyon/Depozito/Komisyon BİLGİDİR (bakiyeye ve deftere girmez) — para
 * birimi iddia etmemek için sayı olarak, Blazor'daki gibi 2 haneyle yazılır; tutar ve bakiye kiranın
 * dövizindedir.
 */
export function kiraSutunlari(t: Ceviri): readonly TabloSutunu<KiraListeSatiri>[] {
  const s = (kod: SutunKodu) => t(`kiraListesi.sutun.${kod}`);
  const bilgiSayisi = { tur: 'sayi', haneler: '1.2-2', genislik: 110 } as const;
  return [
    {
      kod: 'sozlesmeNo',
      baslik: s('sozlesmeNo'),
      deger: (r) => r.sozlesmeNo,
      sirala: true,
      sabit: true,
      genislik: 150,
    },
    {
      kod: 'musteri',
      baslik: s('musteri'),
      deger: (r) => r.musteriAd,
      sirala: true,
      genislik: 180,
    },
    { kod: 'plaka', baslik: s('plaka'), deger: (r) => r.plaka, sirala: true, genislik: 100 },
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
      kod: 'vadeTar',
      baslik: s('vadeTar'),
      deger: (r) => r.vadeTar,
      tur: 'tarih',
      sirala: true,
      genislik: 100,
    },
    {
      kod: 'gun',
      baslik: s('gun'),
      deger: (r) => sayi(r.gun),
      tur: 'sayi',
      haneler: '1.0-0',
      sirala: true,
      genislik: 64,
    },
    {
      kod: 'hediyeGun',
      baslik: s('hediyeGun'),
      deger: (r) => sayi(r.hediyeGun),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 90,
    },
    {
      kod: 'faturalananGun',
      baslik: s('faturalananGun'),
      deger: (r) => sayi(r.faturalananGun),
      tur: 'sayi',
      haneler: '1.0-0',
      genislik: 80,
    },
    {
      kod: 'tutar',
      baslik: s('tutar'),
      deger: (r) => sayi(r.tutar),
      tur: 'para',
      paraBirimi: kiraDovizi,
      sirala: true,
      genislik: 120,
    },
    {
      kod: 'bakiye',
      baslik: s('bakiye'),
      deger: (r) => sayi(r.bakiye),
      tur: 'para',
      paraBirimi: kiraDovizi,
      sirala: true,
      genislik: 120,
    },
    { kod: 'kaynak', baslik: s('kaynak'), deger: (r) => r.kaynak, sirala: true, genislik: 120 },
    {
      kod: 'cikisOfisi',
      baslik: s('cikisOfisi'),
      deger: (r) => r.cikisOfisi,
      sirala: true,
      genislik: 130,
    },
    {
      kod: 'donusOfisi',
      baslik: s('donusOfisi'),
      deger: (r) => r.donusOfisi,
      sirala: true,
      genislik: 130,
    },
    { kod: 'provizyon', baslik: s('provizyon'), deger: (r) => sayi(r.provizyon), ...bilgiSayisi },
    { kod: 'depozito', baslik: s('depozito'), deger: (r) => sayi(r.depozito), ...bilgiSayisi },
    {
      kod: 'komisyonOran',
      baslik: s('komisyonOran'),
      deger: (r) => sayi(r.komisyonOran),
      ...bilgiSayisi,
      genislik: 80,
    },
    {
      kod: 'komisyonTutar',
      baslik: s('komisyonTutar'),
      deger: (r) => sayi(r.komisyonTutar),
      ...bilgiSayisi,
    },
    { kod: 'onayKodu', baslik: s('onayKodu'), deger: (r) => r.onayKodu, genislik: 110 },
    { kod: 'projeAdi', baslik: s('projeAdi'), deger: (r) => r.projeAdi, genislik: 120 },
    { kod: 'assistFirma', baslik: s('assistFirma'), deger: (r) => r.assistFirma, genislik: 120 },
    {
      kod: 'ozelSofor',
      baslik: s('ozelSofor'),
      deger: (r) => r.ozelSoforBilgisi,
      genislik: 140,
    },
    { kod: 'durum', baslik: s('durum'), deger: (r) => r.durum, sirala: true, genislik: 110 },
    {
      kod: 'fatura',
      baslik: s('fatura'),
      deger: (r) => (r.faturali ? t('kiraListesi.filtre.faturali') : '—'),
      genislik: 90,
    },
    {
      kod: 'islemler',
      baslik: s('islemler'),
      deger: () => null,
      gizlenemez: true,
      genislik: 260,
    },
  ];
}
