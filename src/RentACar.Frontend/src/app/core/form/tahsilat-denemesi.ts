import { DestroyRef, Injectable, InjectionToken, inject } from '@angular/core';
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
 * Sekmeler arası kanal adı (aynı köken; başka köken göremez). Token: testler her "sekme grubu"na ayrı ad verir
 * (paralel/sıralı testlerin trafiği birbirine karışmasın); `null` kanalı kapatır.
 */
export const TAHSILAT_DENEME_KANALI = new InjectionToken<string | null>('TAHSILAT_DENEME_KANALI', {
  providedIn: 'root',
  factory: () => 'rc-tahsilat-denemesi',
});

/**
 * Sekmeler arası mesaj. Yalnız anahtar (kira + bakiye + işlem sayısından türetilmiş opak UUID) ve deneme içeriği
 * (tutar, döviz, hesap türü) taşınır — müşteri adı/kimliği gibi kişisel veri YOK.
 */
type KanalMesaji =
  | { readonly tur: 'ekle'; readonly anahtar: string; readonly icerik: TahsilatIcerigi }
  | { readonly tur: 'kapat'; readonly anahtar: string }
  | { readonly tur: 'temizle' }
  | { readonly tur: 'senkronIste' }
  | {
      readonly tur: 'durum';
      readonly kayitlar: readonly (readonly [string, readonly TahsilatIcerigi[]])[];
    };

/** Gelen içerikten YALNIZ bilinen üç alan alınır (fazlası yayılmaz/saklanmaz); biçimsizse `null`. */
function icerikAyikla(x: unknown): TahsilatIcerigi | null {
  if (typeof x !== 'object' || x === null) return null;
  const { tutar, doviz, hesap } = x as Record<string, unknown>;
  const tutarGecerli = tutar === null || typeof tutar === 'string' || typeof tutar === 'number';
  if (!tutarGecerli || typeof doviz !== 'string' || !(hesap === null || typeof hesap === 'string'))
    return null;
  return { tutar, doviz, hesap };
}

function kanalAc(ad: string | null): BroadcastChannel | null {
  return ad === null || typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(ad);
}

const ayniIcerik = (a: TahsilatIcerigi, b: TahsilatIcerigi) =>
  a.tutar === b.tutar && a.doviz === b.doviz && a.hesap === b.hesap;

/**
 * Sonucu bilinmeyen tahsilat denemelerinin UYGULAMA GENELİ kaydı — ANAHTARA bağlı (5. tur MEDIUM-1). Anahtar kira
 * başınadır ve aynı anda birden çok formda durur (sabit panelde Nakit + Kart/Havale; kira listesi ve Panel aynı
 * kiranın aynı anahtarını gösterir). İz forma bağlı olsaydı Nakit'te kaybolan 500'den sonra Kart'taki 600 "tekrar"
 * sayılmaz, "başka işlem" diye korunur ve ikinci basış 600'ü de yazardı. Çıkışta temizlenir.
 *
 * **Sekmeler arası (Low temizliği A):** aynı tarayıcının sekmeleri de aynı anahtarı gösterir (bir sekmede kaybolan
 * 500, öteki sekmede 600 basılınca görülmeli). Kayıt `BroadcastChannel` ile senkronlanır — `sessionStorage` sekmeye
 * özeldir, `localStorage` diske yazar (tutar kalıcı kalırdı). Yeni açılan sekme mevcut durumu ister (`senkronIste`),
 * açık sekmeler `durum` ile yanıtlar. Çıkışta hem yerel kayıt hem öteki sekmeler temizlenir. Kanal yoksa (eski
 * tarayıcı, SSR) davranış eskisi gibi sekme içidir.
 */
@Injectable({ providedIn: 'root' })
export class TahsilatDenemeKaydi {
  private readonly bilinmeyenler = new Map<string, TahsilatIcerigi[]>();
  private readonly kanal: BroadcastChannel | null = kanalAc(inject(TAHSILAT_DENEME_KANALI));

  constructor() {
    inject(OturumServisi, { optional: true })?.temizlikKaydet(() => {
      this.bilinmeyenler.clear();
      this.yay({ tur: 'temizle' });
    });
    inject(DestroyRef, { optional: true })?.onDestroy(() => this.kanaliKapat());
    if (this.kanal) {
      this.kanal.onmessage = (e: MessageEvent<unknown>) => this.al(e.data);
      this.yay({ tur: 'senkronIste' });
    }
  }

  /** Bu anahtarla sonucu bilinmeyen denemeler (anlık kopya; öğeler aynı nesneler). */
  bilinmeyen(anahtar: string): TahsilatIcerigi[] {
    return [...(this.bilinmeyenler.get(anahtar) ?? [])];
  }

  ekle(anahtar: string, icerik: TahsilatIcerigi): void {
    this.yerelEkle(anahtar, icerik);
    this.yay({
      tur: 'ekle',
      anahtar,
      icerik: { tutar: icerik.tutar, doviz: icerik.doviz, hesap: icerik.hesap },
    });
  }

  /** Bu anahtarla 2xx alındı: anahtar tek kayıt taşır, belirsiz denemeler yazılmamıştır. */
  kapat(anahtar: string): void {
    this.bilinmeyenler.delete(anahtar);
    this.yay({ tur: 'kapat', anahtar });
  }

  /** Kanalı kapatır (kök enjektör yok edilirken; testlerde sekme kapanışı). */
  kanaliKapat(): void {
    this.kanal?.close();
  }

  private yerelEkle(anahtar: string, icerik: TahsilatIcerigi): void {
    const liste = this.bilinmeyenler.get(anahtar) ?? [];
    liste.push(icerik);
    this.bilinmeyenler.set(anahtar, liste);
  }

  private yay(m: KanalMesaji): void {
    try {
      this.kanal?.postMessage(m);
    } catch {
      // Kapalı kanal: sekme içi kayıt yine doğru; senkron en iyi çaba.
    }
  }

  /** Öteki sekmeden gelen mesaj — yeniden YAYILMAZ (döngü yok); biçimsiz mesaj yok sayılır. */
  private al(veri: unknown): void {
    if (typeof veri !== 'object' || veri === null) return;
    const m = veri as Record<string, unknown>;
    switch (m['tur']) {
      case 'ekle': {
        const icerik = icerikAyikla(m['icerik']);
        if (typeof m['anahtar'] === 'string' && icerik) this.yerelEkle(m['anahtar'], icerik);
        return;
      }
      case 'kapat':
        if (typeof m['anahtar'] === 'string') this.bilinmeyenler.delete(m['anahtar']);
        return;
      case 'temizle':
        this.bilinmeyenler.clear();
        return;
      case 'senkronIste':
        if (this.bilinmeyenler.size > 0)
          this.yay({
            tur: 'durum',
            kayitlar: [...this.bilinmeyenler].map(([a, l]) => [a, [...l]] as const),
          });
        return;
      case 'durum':
        if (Array.isArray(m['kayitlar'])) this.durumBirlestir(m['kayitlar']);
        return;
    }
  }

  /** Birden çok sekme aynı durumu yanıtlayabilir: aynı içerik ikinci kez eklenmez. */
  private durumBirlestir(kayitlar: unknown[]): void {
    for (const k of kayitlar) {
      if (!Array.isArray(k) || typeof k[0] !== 'string' || !Array.isArray(k[1])) continue;
      const anahtar = k[0];
      for (const ham of k[1] as unknown[]) {
        const icerik = icerikAyikla(ham);
        if (icerik && !this.bilinmeyen(anahtar).some((d) => ayniIcerik(d, icerik)))
          this.yerelEkle(anahtar, icerik);
      }
    }
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
