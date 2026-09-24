import type { ApiHatasi } from '@core/api/api-hatasi';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import {
  type TahsilatDenemeKaydi,
  TahsilatDenemesi,
  type TahsilatGonderimi,
  type TahsilatMukerrerTuru,
  sonucuBilinmeyenHata,
} from '@core/form/tahsilat-denemesi';

import type { AccountKind, InstallmentPayRequest, LoanDetail } from '../finance-model';

/** Formdaki seçimler (tutar ve sıra SUNUCUNUN planından gelir; kullanıcı yazmaz). */
export interface PaymentChoice {
  readonly hesap: AccountKind;
  readonly hesapId: string | null;
}

/** Bir ödeme gönderiminin donmuş kopyası: aynı anahtar + birebir aynı gövde (yeniden deneme bununla gider). */
export interface FrozenPayment {
  readonly loanId: string;
  readonly key: string;
  readonly body: InstallmentPayRequest;
  readonly amount: number | string;
  readonly currency: string;
}

/** Bir gönderimin sonucu (bileşen buna göre mesaj verir ve kaydı yeniler). */
export type PaymentOutcome =
  | { readonly kind: 'paid' }
  /** Sonucu bilinmiyor (ağ/5xx): kopya DONAR, tekrar aynı anahtar + gövdeyle. */
  | { readonly kind: 'uncertain' }
  /** Kesin red (doğrulama, yetki…): yazılmadı; kopya çözülür, form düzeltilip gönderilebilir (anahtar aynı). */
  | { readonly kind: 'rejected' }
  /** 409 `cakisma`: taksit bu arada ödendi — kayıt yenilenir, yeni anahtar. */
  | { readonly kind: 'stale' }
  /** 409 `mukerrer`: otomatik yeniden gönderim YOK; kayıt yenilenir. */
  | { readonly kind: 'duplicate'; readonly type: TahsilatMukerrerTuru };

/**
 * Araç kredisi taksit ödemesi (`POST arac-kredileri/{id}/taksit-ode`) — PARA, gider + defter yazar. DEVIR §5:
 *
 * - İşlem başına bir `Idempotency-Key`; 2xx ya da kesin sonuçtan (409) sonra yenilenir.
 * - Sonucu bilinmeyen hatadan sonra yeniden deneme DONMUŞ kopyayla (AYNI anahtar + AYNI gövde) gider — form
 *   değişse de. İlk istek yazıldıysa sunucu 409 `mukerrer` + `mevcut.ayniIcerik` döner: ikinci taksit ödenmez.
 * - Kesin redde (400/403) kopya çözülür ama anahtar korunur (ilk istek yazılmadı; düzeltilmiş gövde aynı anahtarla).
 * - Sıra sunucunun "sonraki taksit"idir; bu arada başka ödeme olduysa sunucu 409 `cakisma` verir.
 * - Belirsiz denemeler `TahsilatDenemeKaydi`'nda ANAHTARA bağlı tutulur (sekmeler arası; kişisel veri yok).
 */
export class InstallmentPayment {
  private readonly attempt: TahsilatDenemesi;
  private key: string | null = null;
  private frozenCopy: FrozenPayment | null = null;
  private pending: TahsilatGonderimi | null = null;

  constructor(
    registry: TahsilatDenemeKaydi,
    private readonly newKey: () => string = yeniIslemAnahtari,
  ) {
    this.attempt = new TahsilatDenemesi(registry);
  }

  /** Donmuş kopya (sonucu bilinmeyen gönderim) — varken form kilitlidir, düğme "Tekrar dene"dir. */
  get frozen(): FrozenPayment | null {
    return this.frozenCopy;
  }

  /**
   * Gönderilecek kopya: donmuş kopya bu krediye aitse O (form yok sayılır); değilse güncel detay + seçimden yeni
   * gövde. Ödenecek taksit yoksa `null`.
   */
  prepare(loan: LoanDetail, choice: PaymentChoice): FrozenPayment | null {
    if (this.frozenCopy?.loanId === loan.id) return this.frozenCopy;
    const next = loan.sonrakiTaksit;
    if (next === null || !loan.yetkiler.taksitOde) return null;
    this.key ??= this.newKey();
    return {
      loanId: loan.id,
      key: this.key,
      body: { sira: next.sira, hesap: choice.hesap, hesapId: choice.hesapId },
      amount: next.tutar,
      currency: loan.doviz,
    };
  }

  /** Gönderimden HEMEN önce: belirsiz deneme izi başlar (sekmeler arası kayıt anahtara bağlı). */
  started(copy: FrozenPayment): void {
    this.pending = this.attempt.basla(copy.key, {
      tutar: copy.amount,
      doviz: copy.currency,
      hesap: copy.body.hesap ?? null,
    });
  }

  succeeded(): PaymentOutcome {
    if (this.pending) this.attempt.basarili(this.pending);
    this.reset();
    return { kind: 'paid' };
  }

  failed(copy: FrozenPayment, error: ApiHatasi): PaymentOutcome {
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
    // Kesin red: yazılmadı. Anahtar KORUNUR (aynı işlem), kopya çözülür (kullanıcı düzeltebilir).
    this.frozenCopy = null;
    return { kind: 'rejected' };
  }

  /** Son gönderimin deneme fotoğrafı (mükerrer bildirimi için). */
  get lastSubmission(): TahsilatGonderimi | null {
    return this.pending;
  }

  /**
   * Kullanıcı donmuş denemeden vazgeçti (bilinçli): kopya ve anahtar bırakılır. Belirsiz deneme kayıtta KALIR;
   * yeni deneme yeni anahtarla gider ve sunucu sıra kontrolüyle (409 `cakisma`) ikinci taksidi engeller.
   */
  abandon(): void {
    this.reset();
  }

  private reset(): void {
    this.frozenCopy = null;
    this.key = null;
  }
}
