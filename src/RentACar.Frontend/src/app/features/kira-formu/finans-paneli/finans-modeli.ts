import { type Signal, computed, signal } from '@angular/core';
import { paraBicimle } from '@core/bicim/bicim';
import { invariantOndalik } from '@core/form/ondalik';
import { sayiya } from '../kira-formu-modeli';
import type { SunucuSayisi } from '../kira-tipleri';
import type {
  DepozitoAlIstegi,
  DepozitoIratIstegi,
  DisHizmetIstegi,
  FaturaKesIstegi,
  HesapTuru,
  OdemeIstegi,
  TahsilatBilgisi,
  TahsilatIstegi,
} from './finans-tipleri';

/**
 * Sabit panel finans işlemlerinin SAF kuralları (F4.4): istek gövdeleri, kur alanı, tahsilat anahtarı
 * kopyası. Tutar formülü YOK — tutarlar kullanıcının yazdığı (ya da sunucunun verdiği) değerlerdir; para
 * değerleri invariant ondalık METİN taşınır (`rc-para-girdisi`: "1.500,50" → "1500.50"; sunucu `decimal`).
 */

/** Temel para birimi; kur alanı yalnız bunun dışında gönderilir. */
export const TEMEL_DOVIZ = 'TRY';
/** Blazor sabit panelinin döviz seçenekleri (kiranın dövizi farklıysa listeye eklenir). */
export const DOVIZLER = ['TRY', 'USD', 'EUR'] as const;
/** FAZ-84 kanal (saf bilgi; defteri etkilemez). */
export const KANALLAR = ['Masaüstü', 'Mobil', 'Tablet'] as const;

type ParaDegeri = string | number | null | undefined;

/** Para kontrolü değeri → invariant metin (`"1500.50"`); boş/biçimsiz → `null`. */
export function paraMetni(deger: ParaDegeri, kesir = 4): string | null {
  return invariantOndalik(deger, { kesir });
}

/** Sunucunun `varsayilanTutar`'ı → ön-doldurma (yalnız POZİTİFse; bakiye ≤ 0 ise alan boş kalır). */
export function onDoldurmaTutari(deger: ParaDegeri): string | null {
  const m = paraMetni(deger, 2);
  return m !== null && !m.startsWith('-') && !/^0+(\.0+)?$/.test(m) ? m : null;
}

/** ISO kod (boş → TRY). Kira formunun eski adları ("TL", "EURO") ISO'ya çevrilir; kalanı sunucu doğrular. */
export function dovizKodu(doviz: string | null | undefined): string {
  const d = (doviz ?? '').trim();
  if (d === '' || d === 'TL') return TEMEL_DOVIZ;
  if (d === 'EURO') return 'EUR';
  return d;
}

/** Gösterim: sunucu tutarı + döviz → `1.234,56 ₺`; değer yoksa "—". HESAP YAPILMAZ. */
export function paraGoster(v: SunucuSayisi, doviz: string | null | undefined): string {
  return paraBicimle(sayiya(v), dovizKodu(doviz)) || '—';
}

/** Döviz seçenekleri: sabit liste + (varsa) kiranın dövizi. */
export function dovizSecenekleri(ek: string | null | undefined): readonly string[] {
  const kod = ek ? dovizKodu(ek) : null;
  return kod && !(DOVIZLER as readonly string[]).includes(kod) ? [...DOVIZLER, kod] : DOVIZLER;
}

/**
 * Kur alanı kuralı: temel parada (TRY) kur GÖNDERİLMEZ (sunucu 1 kullanır; ≠1 reddedilir). Dövizde yazılan
 * kur gönderilir; boşsa hiç gönderilmez → sunucu çözer (firma sabit kuru → TCMB; bulunamazsa red).
 */
export function kurAlani(doviz: string | null | undefined, kur: ParaDegeri): { kur?: string } {
  if (dovizKodu(doviz) === TEMEL_DOVIZ) return {};
  const m = paraMetni(kur, 6);
  return m === null ? {} : { kur: m };
}

function metin(deger: string | null | undefined): string | null {
  const d = deger?.trim() ?? '';
  return d === '' ? null : d;
}

export interface TahsilatDegeri {
  readonly tutar: ParaDegeri;
  readonly doviz: string | null;
  readonly kur: ParaDegeri;
  readonly hesapId: string | null;
  readonly kanal: string | null;
  readonly aciklama: string | null;
}

/**
 * Kira tahsilatı gövdesi. Cari, kira ve DETERMİNİSTİK anahtar SUNUCUNUN verdiği satır kopyasından
 * (`TahsilatBilgisi`) gelir — formdan değil. Anahtar gövdede taşınır; `Idempotency-Key` başlığı
 * GÖNDERİLMEZ (idempotency envanteri "SPA sözleşmesi": deterministik anahtar varken başlık tüketilmez;
 * yeniden deneme AYNI kopyayla yapılır, anahtarsız asla).
 */
export function tahsilatGovdesi(
  kopya: TahsilatBilgisi,
  hesap: HesapTuru,
  d: TahsilatDegeri,
): TahsilatIstegi {
  return {
    cariId: kopya.cariId,
    kiraId: kopya.rentalId,
    tahsilatAnahtar: kopya.anahtar,
    tutar: paraMetni(d.tutar, 2) ?? '',
    hesap,
    hesapId: d.hesapId,
    doviz: dovizKodu(d.doviz),
    ...kurAlani(d.doviz, d.kur),
    kanal: d.kanal,
    aciklama: metin(d.aciklama),
  };
}

export interface OdemeDegeri {
  readonly tutar: ParaDegeri;
  readonly hesapId: string | null;
  readonly kanal: string | null;
  readonly aciklama: string | null;
}

/**
 * Giden havale (Banka ödemesi). Blazor paritesi: kiraya BAĞLANMAZ (kira Tahsilat/Bakiye'si değişmez),
 * cari = kiranın müşterisi, TRY. Anahtar `Idempotency-Key` başlığında (ZORUNLU; işlem başına).
 */
export function odemeGovdesi(cariId: string, d: OdemeDegeri): OdemeIstegi {
  return {
    cariId,
    tutar: paraMetni(d.tutar, 2) ?? '',
    hesap: 'Banka',
    hesapId: d.hesapId,
    kanal: d.kanal,
    aciklama: metin(d.aciklama),
  };
}

export interface DepozitoAlDegeri {
  readonly tutar: ParaDegeri;
  readonly hesap: HesapTuru | null;
  readonly hesapId: string | null;
}

/** Depozito al (TRY, cari = kiranın müşterisi): Borç Kasa/Banka / Alacak Depozito. */
export function depozitoAlGovdesi(cariId: string, d: DepozitoAlDegeri): DepozitoAlIstegi {
  return {
    cariId,
    tutar: paraMetni(d.tutar, 2) ?? '',
    hesap: d.hesap,
    hesapId: d.hesapId,
  };
}

export interface IratDegeri {
  readonly tutar: ParaDegeri;
  readonly aciklama: string | null;
}

/** Depozito irat: gelir bu kiranın aracına atfedilir (kira bu carinin olmalı — sunucu çiti). */
export function iratGovdesi(cariId: string, kiraId: string, d: IratDegeri): DepozitoIratIstegi {
  return { cariId, kiraId, tutar: paraMetni(d.tutar, 2) ?? '', aciklama: metin(d.aciklama) };
}

export interface FaturaDegeri {
  readonly otv: ParaDegeri;
  readonly tevkifatOran: ParaDegeri;
  readonly tevkifatTutar: ParaDegeri;
  readonly damgaVergisi: ParaDegeri;
  readonly iadeMi: boolean | null;
  readonly manuelMi: boolean | null;
}

/** Kiradan fatura (fark faturası otomatiği serviste). Vergi alanları bilgi amaçlı, boş = gönderilmez. */
export function faturaGovdesi(kiraId: string, d: FaturaDegeri): FaturaKesIstegi {
  return {
    kiraId,
    otv: paraMetni(d.otv, 2),
    tevkifatOran: paraMetni(d.tevkifatOran, 2),
    tevkifatTutar: paraMetni(d.tevkifatTutar, 2),
    damgaVergisi: paraMetni(d.damgaVergisi, 2),
    iadeMi: d.iadeMi ?? false,
    manuelMi: d.manuelMi ?? false,
  };
}

export interface DisHizmetDegeri {
  readonly cariId: string | null;
  readonly alinanHizmet: string | null;
  readonly hizmetAlinanFirma: string | null;
  readonly hizmetBedeli: ParaDegeri;
  readonly komisyonOran: ParaDegeri;
  readonly doviz: string | null;
  readonly kur: ParaDegeri;
  readonly komisyonFaturaNo: string | null;
  readonly aciklama: string | null;
}

/** B2B dış hizmet alımı (tam defterli). Anahtar başlıkta (ZORUNLU; işlem başına). */
export function disHizmetGovdesi(kiraId: string, d: DisHizmetDegeri): DisHizmetIstegi {
  return {
    kiraId,
    cariId: d.cariId ?? '',
    alinanHizmet: metin(d.alinanHizmet),
    hizmetAlinanFirma: metin(d.hizmetAlinanFirma),
    hizmetBedeli: paraMetni(d.hizmetBedeli, 2) ?? '',
    komisyonOran: paraMetni(d.komisyonOran, 2),
    doviz: dovizKodu(d.doviz),
    ...kurAlani(d.doviz, d.kur),
    komisyonFaturaNo: metin(d.komisyonFaturaNo),
    aciklama: metin(d.aciklama),
  };
}

/** `detayGeldi` sonucu: form ne yapmalı? */
export type KopyaSonucu =
  /** Yeni kopya alındı, form boşta → değerler sunucudan ön-doldurulur. */
  | 'ondoldur'
  /** Yeni kopya alındı ama değerlere dokunulmaz (kullanıcı yazdı ya da 409 sonrası ön-doldurma kapalı). */
  | 'anahtar'
  /** Açık form: ESKİ kopya korunur (bayatsa sunucu 409 verir → yeniden yüklenir). */
  | 'korundu';

/**
 * Tahsilat formunun SATIR KOPYASI (deterministik `tahsilatAnahtar` + cari + kira + döviz). Kural:
 *
 * - Form boştaysa (dokunulmamış, sonuçlanmamış gönderim yok) her yeni detay kopyayı tazeler.
 * - Kullanıcı formu doldurduysa ya da bir gönderim SONUÇLANMADIYSA (ağ/5xx/oturum/doğrulama) kopya
 *   DONAR: yeniden deneme aynı anahtarla gider. İlk istek sunucuya ulaşıp yazıldıysa ikincisi anahtar
 *   üzerinden 409 alır — anahtar hiçbir zaman sessizce yenisiyle (ya da anahtarsızla) değiştirilmez.
 * - Gönderim sonuçlanınca (2xx ya da 409 `mukerrer`) kopya "tazeleme bekliyor" olur; gönderim düğmesi
 *   YENİ detay gelene dek kapalıdır, gelen detayın anahtarı alınır (ikinci meşru tahsilat yeni anahtarla).
 * - 409 sonrası (`sonuclandi(false)`) ön-doldurma KAPANIR, bir sonraki 2xx'e kadar: kullanıcı güncel bakiyeye
 *   bakıp tutarı bilinçli girer (F4.4 adversarial HIGH-1 — kaybolan yanıttan sonra ön-dolu tutar ikinci
 *   tahsilata davetti).
 */
export class TahsilatKopyasi {
  private readonly _kopya = signal<TahsilatBilgisi | null>(null);
  private readonly _tazelemeBekleniyor = signal(false);
  private denendi = false;
  private ondoldurIzni = true;

  readonly kopya: Signal<TahsilatBilgisi | null> = this._kopya.asReadonly();
  readonly tazelemeBekleniyor: Signal<boolean> = this._tazelemeBekleniyor.asReadonly();
  /** Gönderilebilir: kopya var ve sonuçlanan işlemden sonra tazeleme beklenmiyor. */
  readonly gonderilebilir = computed(() => this._kopya() !== null && !this._tazelemeBekleniyor());

  /**
   * `anahtarBayat`: bu tazeleme, AYNI anahtarı taşıyan kardeş formun (Nakit ↔ Kart/Havale) SONUÇLANAN işleminden
   * geliyor — eski anahtar kesin kullanıldı. Kirli form yine de yeni anahtarı alır (değerlere dokunulmaz → 'anahtar');
   * aksi halde ilk basış kesin bir 409 turu yaşardı (#318 L2). Donmuş (sonucu bilinmeyen) deneme varsa anahtar
   * DEĞİŞMEZ: o deneme yazılmış olabilir, tekrar aynı anahtarla gitmeli.
   */
  detayGeldi(bilgi: TahsilatBilgisi | null, formKirli: boolean, anahtarBayat = false): KopyaSonucu {
    if (this._tazelemeBekleniyor()) {
      this._tazelemeBekleniyor.set(false);
      this.denendi = false;
    } else if (this._kopya() !== null && (this.denendi || (formKirli && !anahtarBayat))) {
      return 'korundu';
    }
    this._kopya.set(bilgi);
    return formKirli || !this.ondoldurIzni ? 'anahtar' : 'ondoldur';
  }

  /** Gönderim denendi ama sonuçlanmadı (ağ/5xx/oturum/doğrulama): donmuş anahtar sayfa terkinde kaybolmasın. */
  get sonuclanmamis(): boolean {
    return this.denendi && !this._tazelemeBekleniyor();
  }

  /** Gönderim başlıyor: kopya donar ve döner (yoksa `null` — gönderim yapılmaz). */
  gonderiliyor(): TahsilatBilgisi | null {
    const k = this._kopya();
    if (k === null || this._tazelemeBekleniyor()) return null;
    this.denendi = true;
    return k;
  }

  /**
   * 2xx (`ondoldur` true) ya da 409 `mukerrer` (`false`): işlem sonuçlandı, sonraki detayın anahtarı alınır.
   * 409'dan sonra ön-doldurma bir sonraki başarılı işleme kadar kapalı kalır.
   */
  sonuclandi(ondoldur = true): void {
    this.ondoldurIzni = ondoldur;
    this._tazelemeBekleniyor.set(true);
  }
}
