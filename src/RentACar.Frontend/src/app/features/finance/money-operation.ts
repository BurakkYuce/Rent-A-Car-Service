import type { ApiYolu } from '@core/api/api-istemcisi';
import type { ApiHatasi } from '@core/api/api-hatasi';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import {
  type TahsilatDenemeKaydi,
  TahsilatDenemesi,
  type TahsilatGonderimi,
  type TahsilatMukerrerTuru,
  sonucuBilinmeyenHata,
} from '@core/form/tahsilat-denemesi';

import type { AttemptContent } from './finance-model';

/** Bir para gönderiminin donmuş kopyası: aynı yol + aynı anahtar + birebir aynı gövde (yeniden deneme bununla). */
export interface FrozenOperation<TBody> {
  readonly path: ApiYolu;
  readonly key: string;
  readonly body: TBody;
  readonly content: AttemptContent;
}

export type OperationOutcome =
  /** 2xx: işlem yazıldı (ya da sunucu aynı içerikli tekrarı sessizce aynı kimlikle döndü). */
  | { readonly kind: 'done' }
  /** Sonucu bilinmiyor (ağ/5xx): kopya DONAR, tekrar AYNI anahtar + AYNI gövdeyle. */
  | { readonly kind: 'uncertain' }
  /** Kesin red (doğrulama, yetki, dönem kilidi…): yazılmadı; kopya çözülür, anahtar korunur (aynı işlem). */
  | { readonly kind: 'rejected' }
  /** 409 `cakisma`: kayıt bu arada değişti — ekran yenilenir, yeni anahtar. */
  | { readonly kind: 'stale' }
  /** 409 `mukerrer`: otomatik yeniden gönderim YOK; ekran yenilenir, yeni anahtar. */
  | { readonly kind: 'duplicate'; readonly type: TahsilatMukerrerTuru };

/**
 * Finans ekranlarının para yazan TEK gönderim kuralı (DEVIR §5 Frontend "Para formu yaşam döngüsü"):
 *
 * - İşlem başına bir `Idempotency-Key` (`yeniIslemAnahtari`); 2xx ya da kesin 409 sonrası yenilenir.
 * - Sonucu bilinmeyen hatadan sonra yeniden deneme DONMUŞ kopyayla gider (AYNI yol + anahtar + gövde; form değişse de
 *   — toplu işlemlerde satır listesi de kopyadadır). İlk istek yazıldıysa sunucu aynı kimliği (200) ya da 409
 *   `mukerrer` + `mevcut.ayniIcerik` döner: ikinci kayıt yazılmaz.
 * - Kesin redde kopya çözülür ama anahtar KORUNUR: düzeltilmiş gövde aynı işlemdir. (Belirsiz bir deneme yazılmışsa
 *   sunucu önce anahtarı arar → 409 `mukerrer`; kullanıcı ikinci işleme yönlendirilmez.)
 * - Belirsiz denemeler {@link TahsilatDenemeKaydi}'nda ANAHTARA bağlı tutulur (sekmeler arası; kişisel veri yok) —
 *   `mukerrer` sınıflandırması (`zatenKaydedildi` / `oncekiDenemeKaydedilmis` …) bunlardan yapılır.
 * - Kira taksit ödemesinin (`InstallmentPayment`, F6.2b) genelleştirilmiş hâli; çekirdeğe konmadı (paralel PR'lar).
 */
export class MoneyOperation<TBody> {
  private readonly attempt: TahsilatDenemesi;
  private key: string | null = null;
  private frozenCopy: FrozenOperation<TBody> | null = null;
  private pending: TahsilatGonderimi | null = null;

  constructor(
    registry: TahsilatDenemeKaydi,
    private readonly newKey: () => string = yeniIslemAnahtari,
  ) {
    this.attempt = new TahsilatDenemesi(registry);
  }

  /** Donmuş kopya (sonucu bilinmeyen gönderim) — varken form kilitlidir, düğme "tekrar gönder"dir. */
  get frozen(): FrozenOperation<TBody> | null {
    return this.frozenCopy;
  }

  /** Bir sonraki gönderimin anahtarı (henüz yoksa `null`). */
  get currentKey(): string | null {
    return this.frozenCopy?.key ?? this.key;
  }

  /** Gönderilecek kopya: donmuş kopya varsa O (form ve `build` yok sayılır); yoksa yeni gövde, aynı işlem anahtarı. */
  prepare(path: ApiYolu, body: TBody, content: AttemptContent): FrozenOperation<TBody> {
    if (this.frozenCopy) return this.frozenCopy;
    this.key ??= this.newKey();
    return { path, key: this.key, body, content };
  }

  /** Gönderimden HEMEN önce: belirsiz deneme izi başlar. */
  started(copy: FrozenOperation<TBody>): void {
    this.pending = this.attempt.basla(copy.key, copy.content);
  }

  succeeded(): OperationOutcome {
    if (this.pending) this.attempt.basarili(this.pending);
    this.reset();
    return { kind: 'done' };
  }

  failed(copy: FrozenOperation<TBody>, error: ApiHatasi): OperationOutcome {
    const g = this.pending;
    const type = g ? this.attempt.hataGeldi(g, error) : null;
    if (sonucuBilinmeyenHata(error)) {
      this.frozenCopy = copy;
      return { kind: 'uncertain' };
    }
    if (error.kod === 'mukerrer') {
      this.reset();
      return { kind: 'duplicate', type: type ?? 'bayatAnahtar' };
    }
    if (error.kod === 'cakisma') {
      this.reset();
      return { kind: 'stale' };
    }
    this.frozenCopy = null;
    return { kind: 'rejected' };
  }

  /** Son gönderimin deneme fotoğrafı (mükerrer bildirimi için). */
  get lastSubmission(): TahsilatGonderimi | null {
    return this.pending;
  }

  /**
   * Kullanıcı donmuş denemeden BİLİNÇLİ vazgeçti (onaylı): kopya ve anahtar bırakılır. Belirsiz deneme kayıtta
   * KALIR (sekmeler arası); yeni deneme yeni anahtarla gider — kullanıcı önce hareketleri kontrol eder.
   */
  abandon(): void {
    this.reset();
  }

  private reset(): void {
    this.frozenCopy = null;
    this.key = null;
  }
}
