import type { ApiHatasi } from '../api/api-hatasi';
import { paraBicimle } from '../bicim/bicim';
import type { ToastServisi } from '../geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '../i18n/ceviri-anahtarlari';

/**
 * Deterministik anahtarlı tahsilat (`tahsilatAnahtar`, E01) için 409 `mukerrer` sınıfları:
 *
 * - `zatenKaydedildi` — `mevcut.ayniIcerik`: kaybolan yanıttan sonraki kendi birebir tekrarı (HIGH-1).
 * - `oncekiDenemeKaydedilmis` — `mevcut` dolu, içerik FARKLI ve bu gönderim sonucu bilinmeyen bir denemenin
 *   TEKRARI (F4.4 4. tur M-C): kullanıcının önceki denemesi (ör. 500) aslında yazıldı, tutarı düzeltip (600) aynı
 *   donmuş anahtarla yeniden gönderdi. 600 YAZILMADI ama "başka bir tahsilat" demek ve formu korumak ikinci basışta
 *   500 + 600 = 1100 yazdırıyordu → tutar TEMİZLENİR, mesaj önceki denemenin kaydedildiğini söyler.
 * - `baskaIslemYazildi` — `mevcut` dolu, içerik farklı, tekrar DEĞİL (iki sekme/iki kullanıcı; 3. tur M-A): form
 *   korunur; kullanıcı tutarı elle yazmadıysa (ön-dolu) yeni bakiyeyle yenilenir (L-2).
 * - `bayatAnahtar` — `mevcut` yok: ekran açıldıktan sonra kirada işlem oldu; tutar temizlenir.
 */
export type TahsilatMukerrerTuru =
  'zatenKaydedildi' | 'oncekiDenemeKaydedilmis' | 'baskaIslemYazildi' | 'bayatAnahtar';

/**
 * Sonucu BİLİNMEYEN hata: istek sunucuya ulaşıp yazılmış, yanıt kaybolmuş olabilir (ağ, 5xx, `kod`'suz yanıt).
 * `dogrulama`/`cakisma`/`yetki_yok`/`oturum_yok`/`xsrf_gecersiz`/`cok_istek` kesin "yazılmadı"dır.
 */
export function sonucuBilinmeyenHata(hata: ApiHatasi): boolean {
  return hata.kod === 'ag' || hata.kod === 'sunucu' || hata.kod === 'bilinmeyen';
}

/**
 * Bir tahsilat formunun deneme izi (üç giriş — sabit panel, kira listesi, Panel — tek kural). Anahtara bağlıdır:
 * satır/detay tazelenip anahtar değişince eski denemenin izi yeni anahtara taşınmaz.
 *
 * Kullanım: gönderimden HEMEN ÖNCE `tekrarMi(anahtar)`; hata gelince `hataGeldi(...)` (mukerrer sınıfı döner);
 * 2xx'te `basarili()`. L-2 için `tutarYenilemesiIste` + `tutarYenilensinMi`.
 */
export class TahsilatDenemesi {
  private bilinmeyen: string | null = null;
  private tutarYenilenecek: string | null = null;

  /** Bu anahtarla sonucu bilinmeyen bir deneme var mı — varsa şimdiki gönderim onun TEKRARI. */
  tekrarMi(anahtar: string | null | undefined): boolean {
    return anahtar != null && this.bilinmeyen === anahtar;
  }

  /**
   * Hata sonrası: sonucu bilinmiyorsa anahtar işaretlenir (`null`); `mukerrer` ise işlem sonuçlanmıştır, iz
   * silinir ve sınıf döner; diğer hatalarda (kesin "yazılmadı") iz KORUNUR — önceki bilinmeyen deneme hâlâ açık.
   */
  hataGeldi(anahtar: string, hata: ApiHatasi, tekrar: boolean): TahsilatMukerrerTuru | null {
    if (sonucuBilinmeyenHata(hata)) {
      this.bilinmeyen = anahtar;
      return null;
    }
    if (hata.kod !== 'mukerrer') return null;
    this.bilinmeyen = null;
    const m = hata.mevcut;
    if (!m) return 'bayatAnahtar';
    if (m.ayniIcerik) return 'zatenKaydedildi';
    return tekrar ? 'oncekiDenemeKaydedilmis' : 'baskaIslemYazildi';
  }

  basarili(): void {
    this.bilinmeyen = null;
    this.tutarYenilenecek = null;
  }

  /** L-2: `baskaIslemYazildi` sonrası dokunulmamış (ön-dolu) tutar, anahtarı `anahtar`'dan farklı ilk tazelemede yenilensin. */
  tutarYenilemesiIste(anahtar: string): void {
    this.tutarYenilenecek = anahtar;
  }

  /** Tazelenen kaydın anahtarı geldi: tutar yeni öneriyle yenilenmeli mi (bir kez `true`). */
  tutarYenilensinMi(yeniAnahtar: string | null | undefined): boolean {
    if (this.tutarYenilenecek === null || yeniAnahtar == null) return false;
    if (yeniAnahtar === this.tutarYenilenecek) return false;
    this.tutarYenilenecek = null;
    return true;
  }
}

function sayiya(d: number | string | null | undefined): number | null {
  if (d === null || d === undefined || d === '') return null;
  const n = typeof d === 'number' ? d : Number(d);
  return Number.isFinite(n) ? n : null;
}

type Ceviri = (anahtar: CeviriAnahtari, parametreler?: Record<string, unknown>) => string;

export interface MukerrerBildirimi {
  /** Gönderilen (yazılmayan) tutar + döviz — `oncekiDenemeKaydedilmis` metni için. */
  readonly girilenTutar: number | string | null;
  readonly doviz: string;
  /** `bayatAnahtar` başlığı (ekrana özgü: "Kira kaydı değişmiş"). */
  readonly bayatBaslik: string;
  /** Mesaja eklenen "kayıt yeniden yüklendi" metni (ekrana özgü). */
  readonly ek: string;
}

/**
 * `mukerrer` toast'u (çağıran gösterir: interceptor'a `mukerrerCagiranGosterir` ya da `sessiz` verilir). Sunucunun
 * `detail`'ı `oncekiDenemeKaydedilmis` DIŞINDA aynen gösterilir; o sınıfta sunucu metni ("bu ekran açıldıktan sonra
 * başka bir tahsilat yazıldı") yanıltıcıdır — yazılan kullanıcının KENDİ önceki denemesidir.
 */
export function tahsilatMukerrerBildir(
  toast: ToastServisi,
  t: Ceviri,
  tur: TahsilatMukerrerTuru,
  hata: ApiHatasi,
  b: MukerrerBildirimi,
): void {
  const ekli = (metin: string) => (b.ek ? `${metin} ${b.ek}` : metin);
  switch (tur) {
    case 'zatenKaydedildi':
      toast.bilgi(ekli(hata.detay), { baslik: t('geriBildirim.zatenKaydedildi') });
      return;
    case 'oncekiDenemeKaydedilmis': {
      const m = hata.mevcut;
      const metin = t('geriBildirim.oncekiDenemeKaydedildi', {
        no: m?.belgeNo ?? '',
        kayitli: paraBicimle(sayiya(m?.tutar), m?.doviz),
        girilen: paraBicimle(sayiya(b.girilenTutar), b.doviz),
      });
      toast.uyari(ekli(metin), { baslik: t('geriBildirim.oncekiDenemeBaslik') });
      return;
    }
    case 'baskaIslemYazildi':
      toast.uyari(ekli(hata.detay), { baslik: t('geriBildirim.baskaIslemYazildi') });
      return;
    case 'bayatAnahtar':
      toast.uyari(ekli(hata.detay), { baslik: b.bayatBaslik });
      return;
  }
}
