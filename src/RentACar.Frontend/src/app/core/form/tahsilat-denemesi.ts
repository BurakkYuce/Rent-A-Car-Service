import { Injectable, inject } from '@angular/core';
import type { ApiHatasi, MevcutIslem } from '../api/api-hatasi';
import { paraBicimle } from '../bicim/bicim';
import type { ToastServisi } from '../geri-bildirim/toast-servisi';
import type { CeviriAnahtari } from '../i18n/ceviri-anahtarlari';
import { OturumServisi } from '../oturum/oturum-servisi';

/**
 * Deterministik anahtarlı tahsilat (`tahsilatAnahtar`, E01) için 409 `mukerrer` sınıfları. Sunucuda bir anahtarla
 * EN FAZLA bir tahsilat yazılır (kısmi unique index); `mevcut` o kayıttır.
 *
 * - `zatenKaydedildi` — `mevcut.ayniIcerik`: kaybolan yanıttan sonraki kendi birebir tekrarı (HIGH-1).
 * - `oncekiDenemeKaydedilmis` — bu anahtarla sonucu bilinmeyen bir deneme VAR ve `mevcut` onun içeriğiyle (tutar +
 *   döviz) eşleşiyor (4. tur M-C; 5. tur LOW-1): yazılan kullanıcının önceki denemesi, şimdiki tutar YAZILMADI →
 *   tutar TEMİZLENİR (form korunursa ikinci basış yeni anahtarla ikinci tahsilatı yazar: 500 + 600 = 1100).
 * - `baskaIslemDenemeYazilmadi` — sonucu bilinmeyen deneme VAR ama `mevcut` onunla eşleşmiyor: anahtarı başka bir
 *   işlem (sekme/kullanıcı) kullandı; anahtar tek kayıt taşıdığı için önceki deneme de YAZILMADI. Tutar yine
 *   TEMİZLENİR (5. tur: belirsiz deneme varken form korunmaz — ikinci basış bilinçli yeniden giriş ister).
 * - `baskaIslemYazildi` — belirsiz deneme YOK, içerik farklı (iki sekme/iki kullanıcı; 3. tur M-A): form korunur;
 *   kullanıcı tutarı elle yazmadıysa (ön-dolu) yeni bakiyeyle yenilenir (L-2).
 * - `bayatAnahtar` — `mevcut` yok: ekran açıldıktan sonra kirada işlem oldu (ya da ilk deneme hâlâ işleniyor);
 *   tutar temizlenir.
 */
export type TahsilatMukerrerTuru =
  | 'zatenKaydedildi'
  | 'oncekiDenemeKaydedilmis'
  | 'baskaIslemDenemeYazilmadi'
  | 'baskaIslemYazildi'
  | 'bayatAnahtar';

/** Gönderilen tahsilatın ayırt edici içeriği (tutar invariant metin ya da sayı; döviz ISO; hesap türü). */
export interface TahsilatIcerigi {
  readonly tutar: string | number | null;
  readonly doviz: string;
  readonly hesap: string | null;
}

/**
 * Sonucu BİLİNMEYEN hata: istek sunucuya ulaşıp yazılmış, yanıt kaybolmuş olabilir (ağ, 5xx, `kod`'suz yanıt).
 * `dogrulama`/`cakisma`/`yetki_yok`/`oturum_yok`/`xsrf_gecersiz`/`cok_istek` kesin "yazılmadı"dır.
 */
export function sonucuBilinmeyenHata(hata: ApiHatasi): boolean {
  return hata.kod === 'ag' || hata.kod === 'sunucu' || hata.kod === 'bilinmeyen';
}

function sayiya(d: number | string | null | undefined): number | null {
  if (d === null || d === undefined || d === '') return null;
  const n = typeof d === 'number' ? d : Number(d);
  return Number.isFinite(n) ? n : null;
}

const dovizNorm = (d: string) => d.trim().toLocaleUpperCase('tr-TR');

/** `mevcut` bu denemenin kaydı olabilir mi: tutar + döviz (sunucu `mevcut`'ta hesap türü vermez). */
export function denemeyleEslesir(mevcut: MevcutIslem, d: TahsilatIcerigi): boolean {
  const a = sayiya(mevcut.tutar);
  return a !== null && a === sayiya(d.tutar) && dovizNorm(mevcut.doviz) === dovizNorm(d.doviz);
}

/**
 * Sonucu bilinmeyen tahsilat denemelerinin UYGULAMA GENELİ kaydı — ANAHTARA bağlı (5. tur MEDIUM-1). Anahtar kira
 * başınadır ve aynı anda birden çok formda durur (sabit panelde Nakit + Kart/Havale; kira listesi ve Panel aynı
 * kiranın aynı anahtarını gösterir). İz forma bağlı olsaydı Nakit'te kaybolan 500'den sonra Kart'taki 600 "tekrar"
 * sayılmaz, "başka işlem" diye korunur ve ikinci basış 600'ü de yazardı. Çıkışta temizlenir.
 */
@Injectable({ providedIn: 'root' })
export class TahsilatDenemeKaydi {
  private readonly bilinmeyenler = new Map<string, TahsilatIcerigi[]>();

  constructor() {
    inject(OturumServisi, { optional: true })?.temizlikKaydet(() => this.bilinmeyenler.clear());
  }

  /** Bu anahtarla sonucu bilinmeyen denemeler (anlık kopya; öğeler aynı nesneler). */
  bilinmeyen(anahtar: string): TahsilatIcerigi[] {
    return [...(this.bilinmeyenler.get(anahtar) ?? [])];
  }

  ekle(anahtar: string, icerik: TahsilatIcerigi): void {
    const liste = this.bilinmeyenler.get(anahtar) ?? [];
    liste.push(icerik);
    this.bilinmeyenler.set(anahtar, liste);
  }

  /** Bu anahtarla 2xx alındı: anahtar tek kayıt taşır, belirsiz denemeler yazılmamıştır. */
  kapat(anahtar: string): void {
    this.bilinmeyenler.delete(anahtar);
  }
}

/** Bir gönderimin fotoğrafı (`TahsilatDenemesi.basla`). */
export interface TahsilatGonderimi {
  readonly anahtar: string;
  readonly icerik: TahsilatIcerigi;
  /**
   * Bu anahtarla sonucu belirsiz denemeler: gönderim anındakiler (boş değilse bu gönderim bir TEKRAR) +
   * `hataGeldi`'de yanıt anına kadar başka formların eklediği.
   */
  readonly onceki: TahsilatIcerigi[];
}

/**
 * Bir tahsilat formunun deneme mantığı (üç giriş — sabit panel, kira listesi, Panel — tek kural). Belirsiz deneme
 * kaydı paylaşımlı {@link TahsilatDenemeKaydi}'dadır; bu sınıf yalnız formun L-2 bayrağını taşır.
 *
 * Kullanım: gönderimden HEMEN ÖNCE `basla(anahtar, icerik)`; 2xx'te `basarili(g)`; hatada `hataGeldi(g, hata)`.
 */
export class TahsilatDenemesi {
  private tutarYenilenecek: string | null = null;

  constructor(private readonly kayit: TahsilatDenemeKaydi) {}

  basla(anahtar: string, icerik: TahsilatIcerigi): TahsilatGonderimi {
    return { anahtar, icerik, onceki: this.kayit.bilinmeyen(anahtar) };
  }

  /** Bu anahtarla sonucu bilinmeyen bir deneme var mı (herhangi bir formdan). */
  tekrarMi(anahtar: string | null | undefined): boolean {
    return anahtar != null && this.kayit.bilinmeyen(anahtar).length > 0;
  }

  basarili(g: TahsilatGonderimi): void {
    this.kayit.kapat(g.anahtar);
    this.tutarYenilenecek = null;
  }

  /**
   * Hata sonrası: sonucu bilinmiyorsa deneme kayda eklenir (`null`); kesin red kaydı DEĞİŞTİRMEZ (`null`);
   * `mukerrer` sınıfı döner. 409 kaydı SİLMEZ: `mevcut`suz 409'da ilk deneme sunucuda hâlâ işleniyor olabilir
   * (eşzamanlı yarış, 5. tur LOW-2); `mevcut`lu 409'dan sonra da aynı anahtar ÖTEKİ formda (Nakit ↔ Kart, liste ↔
   * Panel) bayat kopyayla yeniden gönderilebilir — o gönderim de belirsiz denemeyi görmeli. Kayıt yalnız bu anahtarın
   * 2xx'inde (anahtar tek kayıt taşır → belirsiz denemeler yazılmamıştır) ve çıkışta silinir.
   */
  hataGeldi(g: TahsilatGonderimi, hata: ApiHatasi): TahsilatMukerrerTuru | null {
    if (sonucuBilinmeyenHata(hata)) {
      this.kayit.ekle(g.anahtar, g.icerik);
      return null;
    }
    if (hata.kod !== 'mukerrer') return null;
    for (const d of this.kayit.bilinmeyen(g.anahtar)) if (!g.onceki.includes(d)) g.onceki.push(d);
    const m = hata.mevcut;
    if (!m) return 'bayatAnahtar';
    if (m.ayniIcerik) return 'zatenKaydedildi';
    if (g.onceki.some((d) => denemeyleEslesir(m, d))) return 'oncekiDenemeKaydedilmis';
    return g.onceki.length > 0 ? 'baskaIslemDenemeYazilmadi' : 'baskaIslemYazildi';
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

/** Form tutarı temizlenir mi: belirsiz bir deneme söz konusuysa ya da anahtar bayatsa form KORUNMAZ. */
export function tutarTemizlenir(tur: TahsilatMukerrerTuru): boolean {
  return (
    tur === 'oncekiDenemeKaydedilmis' ||
    tur === 'baskaIslemDenemeYazilmadi' ||
    tur === 'bayatAnahtar'
  );
}

type Ceviri = (anahtar: CeviriAnahtari, parametreler?: Record<string, unknown>) => string;

export interface MukerrerBildirimi {
  /** Bu gönderimin fotoğrafı — gönderilen (yazılmayan) tutar ve belirsiz denemeler için. */
  readonly gonderim: TahsilatGonderimi;
  /** `bayatAnahtar` başlığı (ekrana özgü: "Kira kaydı değişmiş"). */
  readonly bayatBaslik: string;
  /** Mesaja eklenen "kayıt yeniden yüklendi" metni (ekrana özgü). */
  readonly ek: string;
}

/**
 * `mukerrer` toast'u (çağıran gösterir: interceptor'a `mukerrerCagiranGosterir` ya da `sessiz` verilir). Sunucunun
 * `detail`'ı `oncekiDenemeKaydedilmis` DIŞINDA gösterilir; o sınıfta sunucu metni ("bu ekran açıldıktan sonra
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
  const g = b.gonderim;
  switch (tur) {
    case 'zatenKaydedildi':
      toast.bilgi(ekli(hata.detay), { baslik: t('geriBildirim.zatenKaydedildi') });
      return;
    case 'oncekiDenemeKaydedilmis': {
      const m = hata.mevcut;
      const metin = t('geriBildirim.oncekiDenemeKaydedildi', {
        no: m?.belgeNo ?? '',
        kayitli: paraBicimle(sayiya(m?.tutar), m?.doviz),
        girilen: paraBicimle(sayiya(g.icerik.tutar), g.icerik.doviz),
      });
      toast.uyari(ekli(metin), { baslik: t('geriBildirim.oncekiDenemeBaslik') });
      return;
    }
    case 'baskaIslemDenemeYazilmadi': {
      const onceki = [...new Set(g.onceki.map((d) => paraBicimle(sayiya(d.tutar), d.doviz)))];
      const metin = `${hata.detay} ${t('geriBildirim.denemeKaydedilmedi', { onceki: onceki.join(', ') })}`;
      toast.uyari(ekli(metin), { baslik: t('geriBildirim.baskaIslemYazildi') });
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
