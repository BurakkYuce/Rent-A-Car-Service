import {
  DestroyRef,
  Injectable,
  InjectionToken,
  type Signal,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';

import type { ApiYolu } from '@core/api/api-istemcisi';
import { OturumServisi } from '@core/oturum/oturum-servisi';
import type { Ben } from '@core/oturum/oturum-tipleri';

import type { MoneyContent, MoneyNotice } from './money-notice';

/** Sonucu kesinleşmemiş (uçuşta ya da belirsiz) bir para gönderiminin kopyası: aynı yol + anahtar + gövde. */
export interface MoneyAttempt<TBody = unknown> {
  /** Anahtarın bağlı olduğu kayıt (bu MTV'nin ödemesi, bu servisin kalemi…); başka kayıt → yeni anahtar. */
  readonly target: string;
  readonly path: ApiYolu;
  readonly method: 'post' | 'put';
  readonly key: string;
  readonly body: TBody;
  /** Bildirimdeki "girdiğiniz" tutarı (kanca; gövdeye girmez). */
  readonly content: MoneyContent | null;
  /** Formun o anki ham değeri (bileşen yeniden kurulunca form aynı içerikle kilitli açılır). */
  readonly formValue: unknown;
  /** Yanıt henüz gelmedi. */
  readonly inFlight: boolean;
  /** Bileşen geri gelince gösterilecek not. */
  readonly notice: MoneyNotice;
  /**
   * Gönderim anındaki oturum bağlamı (kiracı|kullanıcı|şube). Kayıt yalnız AYNI bağlamda okunur: çıkış ya da başka
   * kullanıcıyla giriş sonrası önceki kullanıcının kilitli formu, cari adı ve tutarı geri gelmez (r316 M1).
   */
  readonly context: string | null;
}

/**
 * `GET oturum/ben` yanıtının bağlam anahtarı — `OturumServisi.baglam` ile AYNI biçim (kiracı|kullanıcı|şube kapsamı).
 * Tekrar göndermeden önceki sunucu doğrulamasında kullanılır; biçim orada değişirse burada da değişmeli.
 */
export function contextOfSession(ben: Pick<Ben, 'kiraci' | 'kullanici' | 'subeKapsami'>): string {
  const sube = ben.subeKapsami.tumSubeler ? '*' : (ben.subeKapsami.subeId ?? '-');
  return `${ben.kiraci.id}|${ben.kullanici.id}|${sube}`;
}

/**
 * Sekmeler arası oturum bağlamı kanalı (aynı köken). Bir sekmede çıkış ya da başka kullanıcı girişi olunca öteki
 * sekmeler bağlamı düşürür ve `ben`'i sunucudan yeniden okur (ortak çerez artık yeni kullanıcınındır — r316 M-new).
 * Testlerde `null` kanalı kapatır.
 */
export const MONEY_SESSION_CHANNEL = new InjectionToken<string | null>('MONEY_SESSION_CHANNEL', {
  providedIn: 'root',
  factory: () => 'rc-oturum-baglami',
});

/** Oturum bağlamının anahtarı; oturum yoksa `null` (test sahtelerinde `baglam` olmayabilir). */
export function sessionContext(session: OturumServisi | null): string | null {
  return session?.baglam?.()?.anahtar ?? null;
}

/**
 * Sonucu kesinleşmemiş para denemeleri — kapsam (`scope`) başına bir kayıt. Deneme GÖNDERİLMEDEN ÖNCE "uçuşta" olarak
 * yazılır ve yalnız KESİN sonuçta (2xx, `mevcut`lu 409, kesin red, `cakisma`) silinir: form uçuştayken yok edilse de
 * anahtar ve gövde burada kalır, bileşen geri gelince form kilitli, AYNI gövde + anahtarla açılır — yeni anahtarla
 * ikinci ödeme yazılmaz.
 *
 * Varsayılan örnek köktedir; sayfa `providers`'ına koyarak kapsamı sayfaya daraltır (sayfa terk koruması ve "uçuşta"
 * düğme pasifliği o sayfanın denemelerini sayar). Yalnız bellekte (tarayıcı deposuna yazılmaz; tutar kişisel veri
 * sayılmasa da kalıcı kalmasın); çıkışta temizlenir.
 */
@Injectable({ providedIn: 'root' })
export class PendingMoneyAttempts {
  private readonly session = inject(OturumServisi, { optional: true });
  private readonly attempts = signal<ReadonlyMap<string, MoneyAttempt>>(new Map());
  /** Güncel bağlamın denemeleri. */
  private readonly current = computed(() => {
    const context = sessionContext(this.session);
    return [...this.attempts().values()].filter((a) => a.context === context);
  });
  /** Kesinleşmemiş deneme sayısı (sayfa terk koruması). */
  readonly count: Signal<number> = computed(() => this.current().length);
  /** Yanıtı beklenen gönderim var mı: formu yok eden düğmeler (Kapat, başka satır, süzgeç) pasif olmalı. */
  readonly inFlight: Signal<boolean> = computed(() => this.current().some((a) => a.inFlight));

  constructor() {
    this.session?.temizlikKaydet(() => this.attempts.set(new Map()));
    // Bağlam değişince (çıkış, başka kullanıcı/şube) önceki bağlamın denemeleri düşer.
    let last = sessionContext(this.session);
    effect(() => {
      const current = sessionContext(this.session);
      untracked(() => {
        if (current === last) return;
        this.attempts.set(new Map());
        last = current;
        this.broadcast(current);
      });
    });
    const name = inject(MONEY_SESSION_CHANNEL);
    if (name !== null && typeof BroadcastChannel !== 'undefined') {
      const channel = new BroadcastChannel(name);
      channel.onmessage = (e: MessageEvent<unknown>) => this.received(e.data);
      this.channel = channel;
      inject(DestroyRef, { optional: true })?.onDestroy(() => channel.close());
    }
  }

  private channel: BroadcastChannel | null = null;

  private broadcast(context: string | null): void {
    try {
      this.channel?.postMessage({ tur: 'baglam', anahtar: context });
    } catch {
      // Kapalı kanal: sekme içi davranış yine doğru; tekrar öncesi sunucu doğrulaması ikinci savunmadır.
    }
  }

  /**
   * Öteki sekmenin bağlamı bizimkinden farklı: `ben` sunucudan yeniden okunur. Kimlik gerçekten değiştiyse (çıkış →
   * 401, başka kullanıcı) bağlam değişir ve denemeler + bileşen formları yukarıdaki kuralla düşer; aynı kullanıcı
   * yeniden girdiyse hiçbir şey kaybolmaz.
   */
  private received(data: unknown): void {
    if (typeof data !== 'object' || data === null) return;
    const m = data as Record<string, unknown>;
    if (m['tur'] !== 'baglam') return;
    const other = typeof m['anahtar'] === 'string' ? m['anahtar'] : null;
    if (other === sessionContext(this.session)) return;
    void this.session?.yukle?.();
  }

  /** Bu kapsamın denemesi — yalnız GÜNCEL oturum bağlamında yazılmışsa. */
  get(scope: string): MoneyAttempt | undefined {
    const a = this.attempts().get(scope);
    return a && a.context === sessionContext(this.session) ? a : undefined;
  }

  /** Deneme yazılır — yalnız gönderildiği bağlam hâlâ güncelse (bağlam değiştikten sonra gelen yanıt yazmaz). */
  set(scope: string, attempt: MoneyAttempt): void {
    if (attempt.context !== sessionContext(this.session)) return;
    const next = new Map(this.attempts());
    next.set(scope, attempt);
    this.attempts.set(next);
  }

  /** Kapsamın kaydı düşer; `context` verilirse yalnız o bağlamın kaydıysa (eski oturumun yanıtı yenisini silmez). */
  delete(scope: string, context?: string | null): void {
    const a = this.attempts().get(scope);
    if (!a || (context !== undefined && a.context !== context)) return;
    const next = new Map(this.attempts());
    next.delete(scope);
    this.attempts.set(next);
  }
}
