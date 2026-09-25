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
import {
  contextOfSession,
  identityOfContext,
  identityOfSession,
} from '@core/oturum/oturum-baglami';
import { OturumServisi } from '@core/oturum/oturum-servisi';

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
   * Gönderim anındaki oturum kimliği (kiracı|kullanıcı; şube hariç — {@link sessionContext}). Kayıt yalnız AYNI kimlikte
   * okunur: çıkış ya da başka kullanıcıyla giriş sonrası önceki kullanıcının kilitli formu, cari adı ve tutarı geri
   * gelmez (r316 M1); şube kapsamı değişimi denemeyi düşürmez (#320 M1).
   */
  readonly context: string | null;
}

/**
 * Bağlam anahtarı ve kimlik — TEK kaynak `@core/oturum/oturum-baglami` (`OturumServisi.baglam` de onu kullanır).
 * Burada yalnız yeniden dışa aktarılır; kendi biçimini tanımlamaz.
 */
export { contextOfSession, identityOfContext, identityOfSession };

/**
 * Sekmeler arası oturum bağlamı kanalı (aynı köken). Bir sekmede çıkış ya da başka kullanıcı girişi olunca öteki
 * sekmeler bağlamı düşürür ve `ben`'i sunucudan yeniden okur (ortak çerez artık yeni kullanıcınındır — r316 M-new).
 * Testlerde `null` kanalı kapatır.
 */
export const MONEY_SESSION_CHANNEL = new InjectionToken<string | null>('MONEY_SESSION_CHANNEL', {
  providedIn: 'root',
  factory: () => 'rc-oturum-baglami',
});

/**
 * Para denemelerinin bağlı olduğu oturum KİMLİĞİ (kiracı|kullanıcı; şube HARİÇ); oturum yoksa `null` (test sahtelerinde
 * `baglam` olmayabilir). #320 M1: sunucunun idempotency anahtarı şube içermez — şube kapsamı değişen aynı kullanıcının
 * denemesi AYNI işlemdir. Şube değişimi denemeyi düşürseydi uçan tekrarın yanıtı yutulur, form temizlenir ve kullanıcı
 * tutarı yeniden girip ikinci işlemi yazardı. Kayıt, temizleme, bayat yanıt ve sekmeler arası karşılaştırmaların HEPSİ
 * bunu kullanır; anahtarın biçimi tek kaynaktan (`contextOfSession` → `identityOfContext`).
 */
export function sessionContext(session: OturumServisi | null): string | null {
  const key = session?.baglam?.()?.anahtar;
  return key == null ? null : identityOfContext(key);
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
    // Kimlik değişince (çıkış, başka kullanıcı/kiracı) önceki kimliğin denemeleri düşer; şube değişimi düşürmez.
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
