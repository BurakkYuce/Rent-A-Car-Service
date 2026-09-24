import { signal } from '@angular/core';

import type { ApiHatasi } from '@core/api/api-hatasi';
import { paraBicimle } from '@core/bicim/bicim';
import { yeniIslemAnahtari } from '@core/form/gonderim-kilidi';
import {
  type TahsilatDenemeKaydi,
  TahsilatDenemesi,
  type TahsilatGonderimi,
  type TahsilatIcerigi,
  type TahsilatMukerrerTuru,
  sonucuBilinmeyenHata,
} from '@core/form/tahsilat-denemesi';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';

import { num } from './service-insurance-model';

type Translate = (key: CeviriAnahtari, params?: Record<string, unknown>) => string;

/** A frozen send: the SAME key and the SAME body are used for every retry of one operation. */
export interface FrozenRequest<B> {
  /** Record the operation targets (payment of this MTV, line of this service…). */
  readonly target: string;
  readonly key: string;
  readonly body: B;
  /** Distinguishing content (amount, currency, account) for the uncertain-attempt registry. */
  readonly content: TahsilatIcerigi;
}

export type MoneyOutcome =
  | { readonly kind: 'done' }
  /** Result unknown (network/5xx): the copy FREEZES; retry goes with the same key + body. */
  | { readonly kind: 'uncertain' }
  /** Definite rejection (validation, permission…): nothing written; copy released, KEY KEPT. */
  | { readonly kind: 'rejected' }
  /** 409 `cakisma`: record changed meanwhile — reload, new key. */
  | { readonly kind: 'stale' }
  /** 409 `mukerrer`: no automatic resend; reload. */
  | { readonly kind: 'duplicate'; readonly type: TahsilatMukerrerTuru };

/**
 * Money write lifecycle of the F9 screens (MTV / muayene / sigorta payment, servis kalem, rücu yansıtma) — the
 * DEVIR §5 rules in one place:
 *
 * - One `Idempotency-Key` per logical operation; renewed after 2xx or a definite 409.
 * - After an UNKNOWN result the retry uses the frozen copy (same key + same body) even if the form changed; if the
 *   first request was written the server answers 409 `mukerrer` + `mevcut` and nothing is paid twice.
 * - After a definite rejection (400/403/session) the key is KEPT: the corrected body goes with the same key.
 * - Uncertain attempts are recorded in the app-wide {@link TahsilatDenemeKaydi} keyed by the operation key, so the
 *   `mukerrer` message can tell "your earlier attempt was written" from "someone else's operation".
 * - Session loss is handled by the interceptor (in-place login, the SAME request resent) — nothing to do here.
 */
export class MoneySubmission<B> {
  private readonly attempt: TahsilatDenemesi;
  private key: string | null = null;
  private keyTarget: string | null = null;
  private pending: TahsilatGonderimi | null = null;

  readonly frozen = signal<FrozenRequest<B> | null>(null);
  readonly sending = signal(false);

  constructor(
    registry: TahsilatDenemeKaydi,
    private readonly newKey: () => string = yeniIslemAnahtari,
  ) {
    this.attempt = new TahsilatDenemesi(registry);
  }

  /** Copy to send: the frozen copy of THIS target wins over the form; otherwise the body with the operation key. */
  prepare(target: string, body: B, content: TahsilatIcerigi): FrozenRequest<B> {
    const frozen = this.frozen();
    if (frozen?.target === target) return frozen;
    if (this.keyTarget !== target) {
      this.key = null;
      this.keyTarget = target;
    }
    this.key ??= this.newKey();
    return { target, key: this.key, body, content };
  }

  /** Immediately before the request. */
  started(copy: FrozenRequest<B>): void {
    this.sending.set(true);
    this.pending = this.attempt.basla(copy.key, copy.content);
  }

  succeeded(): MoneyOutcome {
    this.sending.set(false);
    if (this.pending) this.attempt.basarili(this.pending);
    this.reset();
    return { kind: 'done' };
  }

  failed(copy: FrozenRequest<B>, error: ApiHatasi): MoneyOutcome {
    this.sending.set(false);
    const type = this.pending ? this.attempt.hataGeldi(this.pending, error) : null;
    if (sonucuBilinmeyenHata(error)) {
      this.frozen.set(copy);
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
    this.frozen.set(null);
    return { kind: 'rejected' };
  }

  /** Snapshot of the last send (for the duplicate notice). */
  get lastSubmission(): TahsilatGonderimi | null {
    return this.pending;
  }

  /** User consciously gives up the frozen attempt: the next send is a NEW operation (new key). */
  abandon(): void {
    this.reset();
  }

  private reset(): void {
    this.frozen.set(null);
    this.key = null;
    this.keyTarget = null;
  }
}

export interface DuplicateNotice {
  readonly tone: 'bilgi' | 'uyari';
  readonly title: string;
  readonly message: string;
  /** Amount field must be cleared (an uncertain attempt was involved or the key was stale). */
  readonly clearAmount: boolean;
}

/**
 * 409 `mukerrer` notice of an F9 money operation. No class sends the user to a second payment: every message ends
 * with "record reloaded, check it".
 */
export function duplicateNotice(
  type: TahsilatMukerrerTuru,
  error: ApiHatasi,
  submission: TahsilatGonderimi | null,
  t: Translate,
): DuplicateNotice {
  const reloaded = t('servisSigorta.para.yenilendi');
  switch (type) {
    case 'zatenKaydedildi':
      return {
        tone: 'bilgi',
        title: t('servisSigorta.para.zatenKaydedildi'),
        message: `${error.detay} ${reloaded}`,
        clearAmount: false,
      };
    case 'oncekiDenemeKaydedilmis':
      return {
        tone: 'uyari',
        title: t('servisSigorta.para.oncekiDenemeBaslik'),
        message: t('servisSigorta.para.oncekiDenemeKaydedildi', {
          no: error.mevcut?.belgeNo ?? '',
          kayitli: paraBicimle(num(error.mevcut?.tutar), error.mevcut?.doviz),
          girilen: paraBicimle(
            num(submission?.icerik.tutar ?? null),
            submission?.icerik.doviz ?? 'TRY',
          ),
        }),
        clearAmount: true,
      };
    case 'baskaIslemDenemeYazilmadi':
    case 'baskaIslemYazildi':
      return {
        tone: 'uyari',
        title: t('servisSigorta.para.baskaIslem'),
        message: `${error.detay} ${reloaded}`,
        clearAmount: type === 'baskaIslemDenemeYazilmadi',
      };
    default:
      return {
        tone: 'uyari',
        title: t('servisSigorta.para.kayitDegismis'),
        message: `${error.detay} ${reloaded}`,
        clearAmount: true,
      };
  }
}
