import type { ApiHatasi } from '@core/api/api-hatasi';
import { paraBicimle } from '@core/bicim/bicim';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

/**
 * Para formunun kalıcı notu (form altında; `mukerrer` için interceptor toast'u GÖSTERMEZ — tek kaynak). Metin anahtar +
 * parametreyle taşınır, çizen bileşen çevirir; `detail` sunucunun kendi açıklamasıdır (ikincil satır).
 */
export interface MoneyNotice {
  readonly tone: 'bilgi' | 'uyari';
  readonly title: CeviriAnahtari | null;
  readonly message: CeviriAnahtari;
  readonly params: Readonly<Record<string, string>>;
  readonly detail: string | null;
}

/** Gönderilen işlemin ayırt edici tutarı (bildirimdeki "girdiğiniz" için; formül değil, gösterim). */
export interface MoneyContent {
  readonly tutar: number | string | null;
  readonly doviz: string | null;
}

/**
 * 409 `mukerrer` sınıfı — İŞLEM BAŞINA rastgele anahtar için (deterministik kira tahsilat anahtarı DEĞİL; o
 * `tahsilat-denemesi.ts`'te). Bu anahtarla kayıt varsa onu bu işlemin önceki denemesi yazmıştır:
 *
 * - `recorded` — `mevcut.ayniIcerik`: birebir aynı işlem kayıtlı.
 * - `recordedChanged` — `mevcut` var, içerik farklı: önceki deneme kayıtlı, değiştirilen içerik YAZILMADI.
 * - `recordedEarlier` — `mevcut` yok (yarış kaybı, `FarkliIcerik`, başka uç): bu anahtarla bir kayıt VAR; ikinci
 *   kez yazılmadı. "Bayat anahtar / yazılmadı" DEĞİLDİR (2026-09-24 gece dersi) — anahtar yenilenmez.
 */
export type DuplicateKind = 'recorded' | 'recordedChanged' | 'recordedEarlier';

export function classifyDuplicate(error: Pick<ApiHatasi, 'mevcut'>): DuplicateKind {
  const m = error.mevcut;
  if (!m) return 'recordedEarlier';
  return m.ayniIcerik ? 'recorded' : 'recordedChanged';
}

/** Sunucu sayısı (`number | string`) → sayı; boş/biçimsiz `null`. Hesap yapılmaz. */
export function moneyAmount(v: number | string | null | undefined): number | null {
  if (v === null || v === undefined || v === '') return null;
  const n = typeof v === 'number' ? v : Number(v);
  return Number.isFinite(n) ? n : null;
}

const detailOf = (e: Pick<ApiHatasi, 'detay'>): string | null => e.detay?.trim() || null;

/**
 * `mukerrer` notu. HİÇBİR metin kullanıcıyı ikinci işleme yönlendirmez ("yeniden girin", "yazılmadı, tekrar
 * gönderin" yok): kayıtlı işlem No + tutarla bildirilir, düzeltme iade/iptalle yapılır. `submitted` verilirse (kanca:
 * ör. servis kaleminin NET satır tutarı) farklı içerikte "girdiğiniz … YAZILMADI" eklenir.
 */
export function duplicateNotice(
  error: Pick<ApiHatasi, 'mevcut' | 'detay'>,
  submitted?: MoneyContent | null,
): MoneyNotice {
  const m = error.mevcut;
  const detail = detailOf(error);
  if (!m)
    return {
      tone: 'bilgi',
      title: 'paraIslemi.dahaOnceBaslik',
      message: 'paraIslemi.dahaOnceKaydedildi',
      params: {},
      detail,
    };
  const params: Record<string, string> = {
    no: m.belgeNo ?? '',
    tutar: paraBicimle(moneyAmount(m.tutar), m.doviz || 'TRY'),
  };
  if (m.ayniIcerik)
    return {
      tone: 'bilgi',
      title: 'paraIslemi.zatenKaydedildi',
      message: 'paraIslemi.oncekiKaydedildi',
      params,
      detail,
    };
  const entered = moneyAmount(submitted?.tutar ?? null);
  if (entered !== null)
    return {
      tone: 'uyari',
      title: 'paraIslemi.farkliBaslik',
      message: 'paraIslemi.oncekiKaydedildiFarkliTutar',
      params: { ...params, girilen: paraBicimle(entered, submitted?.doviz || m.doviz || 'TRY') },
      detail,
    };
  return {
    tone: 'uyari',
    title: 'paraIslemi.farkliBaslik',
    message: 'paraIslemi.oncekiKaydedildiFarkli',
    params,
    detail,
  };
}

/** Sonucu bilinmeyen (ağ/5xx) gönderimin notu: gövde dondu, tekrar aynı içerik + anahtarla. */
export const UNCERTAIN_NOTICE: MoneyNotice = {
  tone: 'uyari',
  title: null,
  message: 'paraIslemi.sonucBilinmiyor',
  params: {},
  detail: null,
};

/**
 * Hata → not; `MoneySubmission` DIŞINDAKİ (başlıksız, sunucunun deterministik anahtarlı) işlemler için (ör. gelen
 * e-fatura giderleştirme). `mukerrer` aynı sınıflarla; ağ/5xx "sonuç bilinmiyor"; kesin redde not yok (hata alanda).
 */
export function errorNotice(
  error: Pick<ApiHatasi, 'kod' | 'mevcut' | 'detay'>,
  submitted?: MoneyContent | null,
): MoneyNotice | null {
  if (error.kod === 'mukerrer') return duplicateNotice(error, submitted);
  if (error.kod === 'ag' || error.kod === 'sunucu' || error.kod === 'bilinmeyen')
    return UNCERTAIN_NOTICE;
  return null;
}

/** Çağıranın kendi notu (ör. satışta "araç zaten satılmış" = önceki deneme yazılmış olabilir). */
export function customNotice(
  message: CeviriAnahtari,
  tone: MoneyNotice['tone'] = 'uyari',
  params: Readonly<Record<string, string>> = {},
): MoneyNotice {
  return { tone, title: null, message, params, detail: null };
}
