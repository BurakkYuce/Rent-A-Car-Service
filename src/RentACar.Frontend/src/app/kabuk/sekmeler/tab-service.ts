import { DOCUMENT } from '@angular/common';
import { inject, Injectable, InjectionToken, signal } from '@angular/core';
import { NavigationEnd, Router, type Routes } from '@angular/router';

import {
  isDirtyComponent,
  CONFIRM_PROMPT,
  leaveMessage,
} from '@core/form/kaydedilmemis-degisiklik';
import { ToastService } from '@core/geri-bildirim/toast-service';
import { translationFunction } from '@core/i18n/ceviri';
import { SessionService } from '@core/oturum/session-service';
import {
  generateKey,
  SHELL_MARKER,
  routePattern,
  tabKey,
  TAB_DATA,
  lastLeaf,
} from '@core/sekme/tab-key';
import { TabState } from '@core/sekme/tab-state';
import { TabRouteStrategy } from '@core/sekme/sekme-stratejisi';

import { PAGES } from '../../sayfalar';

/** Açık tutulabilecek en fazla sekme (bellekte yaşayan sayfa bileşeni sayısını da sınırlar). */
export const MAX_TABS = 10;

/** Tarayıcı deposu anahtarı (`rc.` öneki: çıkışta `OturumServisi` de siler). */
export const TAB_STORE = 'rc.sekmeler';

/** Kabuk sayfaları (sekme başlığı ve geri yüklemede desen doğrulaması); testlerde değiştirilir. */
export const SHELL_PAGES = new InjectionToken<Routes>('KABUK_SAYFALARI', {
  providedIn: 'root',
  factory: () => PAGES,
});

export interface Sekme {
  readonly anahtar: string;
  /** Rota deseni (`/kiralar/:id`). */
  readonly desen: string;
  /** `:id` parametresi; yoksa `null`. */
  readonly id: string | null;
  /** Sekmedeki son adres (sorgu + fragment dahil) — YALNIZ BELLEKTE. */
  readonly url: string;
  /** Rota başlığı (kişisel veri değil); sayfanın verdiği özel etiket `SekmeDurumu`'nda. */
  readonly baslik: string;
  /** Depoya yazılabilir mi (yol parametresi yalnız `:id`). */
  readonly kalici: boolean;
}

/** Depodaki kayıt: YALNIZ rota deseni + id (KVKK — ad, sorgu, filtre, form değeri yazılmaz). */
export interface SekmeDepoKaydi {
  readonly rota: string;
  readonly id: string | null;
}

const TITLE_SUFFIX = /\s+—\s+RentACar$/;
const PATTERN = /^\/(?:(?:[a-z0-9-]+|:id)(?:\/(?:[a-z0-9-]+|:id))*)?$/;
const IDENTITY = /^[A-Za-z0-9_-]{1,64}$/;

/**
 * Sekmeli çalışma alanı (Revlo `TabsService` fikri; kullanıcı/otel/dil/sunucuya-kayıt/sayfa-kataloğu
 * bağları kesilerek yeniden yazıldı). Her kabuk sayfası (desen + yol parametresi) bir sekmedir;
 * sekmeler arasında geçişte sayfa bileşeni `SekmeRotaStratejisi`'nde yaşar (form durumu korunur).
 *
 * - En fazla {@link MAX_TABS}. Doluyken yeni sayfa açılırsa en uzun süredir kullanılmayan,
 *   kaydedilmemiş değişikliği OLMAYAN sekme kapatılır (bilgi toast'u); hepsi kirliyse gezinme durur
 *   (uyarı toast'u).
 * - Tarayıcıda (`rc.sekmeler`) yalnız `{ rota, id }` saklanır; yenilemede sekmeler geri gelir, içerikleri
 *   ilk tıklamada yüklenir. Çıkışta `OturumServisi.temizlikKaydet` ile tümü silinir.
 */
@Injectable({ providedIn: 'root' })
export class TabService {
  private readonly router = inject(Router);
  private readonly strategy = inject(TabRouteStrategy);
  private readonly durum = inject(TabState);
  private readonly toast = inject(ToastService);
  private readonly confirmPrompt = inject(CONFIRM_PROMPT);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly t = translationFunction();
  private readonly headers = titleMap(inject(SHELL_PAGES));

  private readonly _tabs = signal<readonly Sekme[]>([]);
  readonly tabs = this._tabs.asReadonly();
  readonly activeKey = this.durum.activeKey.asReadonly();

  /** Görünür sayfa bileşeni (kabuğun outlet'i bildirir) — kirli mi sorusu için. */
  private activeComponent: unknown = null;
  private readonly usage = new Map<string, number>();
  private usageCounter = 0;

  constructor() {
    this._tabs.set(this.readFromStore());
    this.sync(false);
    // Kök servis: uygulama ömrünce yaşar, abonelik bilinçli olarak kapatılmaz.
    this.router.events.subscribe((evt) => {
      if (evt instanceof NavigationEnd) this.navigationEnded();
    });
    inject(SessionService).registerCleanup(() => this.clear());
  }

  /** Kabuk outlet'i: etkin bileşen değişti (`activate`/`attach` → bileşen, `deactivate`/`detach` → null). */
  setActiveComponent(component: unknown): void {
    this.activeComponent = component;
  }

  /** Sekmenin sayfası kaydedilmemiş değişiklik taşıyor mu (görünür ya da arka planda). */
  isDirty(key: string): boolean {
    const component =
      key === this.durum.activeKey() ? this.activeComponent : this.strategy.detachedComponent(key);
    return isDirtyComponent(component);
  }

  /** Herhangi bir sekmede (ya da sekme dışı görünür sayfada) kaydedilmemiş değişiklik var mı. */
  anyDirty(): boolean {
    return (
      isDirtyComponent(this.activeComponent) || this._tabs().some((s) => this.isDirty(s.anahtar))
    );
  }

  /**
   * Uygulamadan ayrılmadan (Blazor ekranı, çıkış) önce TEK soru: kirli sekme varsa onay istenir.
   * `true` → devam edilebilir.
   */
  async confirmLeave(): Promise<boolean> {
    if (!this.anyDirty()) return true;
    const custom = [
      this.activeComponent,
      ...this._tabs().map((s) => this.strategy.detachedComponent(s.anahtar)),
    ]
      .map((b) => leaveMessage(b))
      .find((m) => m !== null);
    return this.confirmPrompt(custom ?? this.t('form.kaydedilmemis.onay'));
  }

  /**
   * Yeni sekme için yer (kabuğun `canActivateChild`'ı çağırır). Sekme zaten açıksa ya da yer varsa
   * `true`; doluysa en eski temiz sekmeyi kapatır; hepsi kirliyse `false` (gezinme durur).
   */
  makeRoom(key: string): boolean {
    const list = this._tabs();
    if (list.some((s) => s.anahtar === key) || list.length < MAX_TABS) return true;
    const active = this.durum.activeKey();
    const candidate = list
      .filter((s) => s.anahtar !== active && !this.isDirty(s.anahtar))
      .sort((a, b) => (this.usage.get(a.anahtar) ?? 0) - (this.usage.get(b.anahtar) ?? 0))[0];
    if (!candidate) {
      this.toast.uyari(this.t('kabuk.sekme.sinir', { sayi: MAX_TABS }));
      return false;
    }
    const label = this.etiket(candidate);
    this.remove(candidate.anahtar);
    this.toast.bilgi(this.t('kabuk.sekme.eskiKapatildi', { sayi: MAX_TABS, etiket: label }));
    return true;
  }

  /** Sekmenin görünen adı: sayfanın verdiği etiket, yoksa rota başlığı (+ kısa id). */
  etiket(tab: Sekme): string {
    const custom = this.durum.labels().get(tab.anahtar);
    if (custom) return custom;
    return tab.id ? `${tab.baslik} · ${tab.id.slice(0, 8)}` : tab.baslik;
  }

  /** Sekmeye geç (son adresine). */
  gec(key: string): Promise<boolean> {
    const tab = this._tabs().find((s) => s.anahtar === key);
    return tab ? this.router.navigateByUrl(tab.url) : Promise.resolve(false);
  }

  /**
   * Sekmeyi kapat. Arka plandaki kirli sekme için onay sorulur. Görünür sekme kapanınca komşu sekmeye
   * (yoksa ana sayfaya) gidilir; sayfanın kendi `canDeactivate` sorusu reddedilirse sekme yerinde kalır.
   */
  async kapat(key: string): Promise<void> {
    const list = this._tabs();
    const order = list.findIndex((s) => s.anahtar === key);
    const tab = list[order];
    if (!tab) return;
    if (key !== this.durum.activeKey()) {
      const message =
        leaveMessage(this.strategy.detachedComponent(key)) ?? this.t('form.kaydedilmemis.onay');
      if (this.isDirty(key) && !(await this.confirmPrompt(message))) {
        return;
      }
      this.remove(key);
      return;
    }
    const neighbor = list[order + 1] ?? list[order - 1];
    // Önce listeden çıkar: sayfa artık saklanmaz, kendi canDeactivate'i kirli formu sorar.
    this._tabs.set(list.filter((s) => s !== tab));
    this.sync();
    const navigated = await this.router.navigateByUrl(neighbor?.url ?? '/');
    if (!navigated && this.durum.activeKey() === key) {
      // Vazgeçildi: sekme yerine döner (bileşen hâlâ görünür, hiçbir şey kaybolmadı).
      const current = this._tabs().filter((s) => s.anahtar !== key);
      this._tabs.set([...current.slice(0, order), tab, ...current.slice(order)]);
      this.sync();
      return;
    }
    this.usage.delete(key);
    this.durum.setLabel(key, null);
  }

  /** Çıkış (tam temizlik): sekmeler, arka plandaki bileşenler, etiketler, depo. */
  clear(): void {
    this._tabs.set([]);
    this.usage.clear();
    this.activeComponent = null;
    this.strategy.clear();
    this.durum.clear();
    try {
      this.window?.localStorage.removeItem(TAB_STORE);
    } catch {
      // Engelli depolama.
    }
  }

  private navigationEnded(): void {
    const leaf = lastLeaf(this.router.routerState.snapshot.root);
    const inShell = leaf.pathFromRoot.some((r) => r.routeConfig?.data?.[SHELL_MARKER] === true);
    if (!inShell) {
      this.durum.activate(null);
      return;
    }
    const key = tabKey(leaf);
    if (key === null) {
      this.durum.activate('');
      return;
    }
    const url = this.router.url;
    this.usage.set(key, ++this.usageCounter);
    const list = this._tabs();
    const existing = list.find((s) => s.anahtar === key);
    if (existing) {
      if (existing.url !== url) {
        this._tabs.set(list.map((s) => (s === existing ? { ...s, url } : s)));
      }
    } else {
      const pattern = routePattern(leaf);
      const id = leaf.paramMap.get('id');
      this._tabs.set([
        ...list,
        {
          anahtar: key,
          desen: pattern,
          id,
          url,
          baslik: simplifyTitle(leaf.title) ?? this.headers.get(pattern) ?? pattern,
          kalici:
            Object.keys(leaf.params).every((p) => p === 'id') &&
            PATTERN.test(pattern) &&
            (id === null || IDENTITY.test(id)),
        },
      ]);
    }
    this.durum.activate(key);
    this.sync();
  }

  private remove(key: string): void {
    this._tabs.update((list) => list.filter((s) => s.anahtar !== key));
    this.usage.delete(key);
    this.durum.setLabel(key, null);
    this.sync();
  }

  /** Strateji (hangi sayfalar saklanır) + depo senkronu. */
  private sync(write = true): void {
    this.strategy.setOpenKeys(this._tabs().map((s) => s.anahtar));
    if (!write) return;
    const records: SekmeDepoKaydi[] = this._tabs()
      .filter((s) => s.kalici)
      .map((s) => ({ rota: s.desen, id: s.id }));
    try {
      this.window?.localStorage.setItem(TAB_STORE, JSON.stringify(records));
    } catch {
      // Engelli depolama: sekmeler yalnız bu sayfa ömrünce yaşar.
    }
  }

  /** Depodaki `{ rota, id }` listesi → sekmeler. Bilinmeyen desen, bozuk id, fazlası atılır. */
  private readFromStore(): Sekme[] {
    let raw: unknown;
    try {
      raw = JSON.parse(this.window?.localStorage.getItem(TAB_STORE) ?? '[]');
    } catch {
      return [];
    }
    if (!Array.isArray(raw)) return [];
    const tabs: Sekme[] = [];
    for (const record of raw as unknown[]) {
      const tab = this.tabFromRecord(record);
      if (tab && !tabs.some((s) => s.anahtar === tab.anahtar)) tabs.push(tab);
      if (tabs.length === MAX_TABS) break;
    }
    return tabs;
  }

  private tabFromRecord(record: unknown): Sekme | null {
    if (typeof record !== 'object' || record === null) return null;
    const { rota: route, id } = record as Partial<Record<keyof SekmeDepoKaydi, unknown>>;
    if (typeof route !== 'string' || !PATTERN.test(route)) return null;
    const title = this.headers.get(route);
    if (title === undefined) return null;
    let identity: string | null = null;
    if (route.includes(':id')) {
      if (typeof id !== 'string' || !IDENTITY.test(id)) return null;
      identity = id;
    } else if (id !== null) {
      return null;
    }
    return {
      anahtar: generateKey(route, identity ? { id: identity } : {}),
      desen: route,
      id: identity,
      url: identity ? route.replace(':id', identity) : route,
      baslik: title,
      kalici: true,
    };
  }
}

function simplifyTitle(title: string | undefined): string | null {
  const sade = title?.replace(TITLE_SUFFIX, '').trim();
  return sade ? sade : null;
}

/** Sayfa rotalarından desen → başlık (sekme dışı ve yönlendirme rotaları hariç). */
function titleMap(pages: Routes, parent = ''): Map<string, string> {
  const map = new Map<string, string>();
  for (const route of pages) {
    if (route.path === undefined || route.path === '**' || route.redirectTo !== undefined) continue;
    const pattern = [parent, route.path].filter(Boolean).join('/');
    if (route.children) {
      for (const [d, b] of titleMap(route.children, pattern)) map.set(d, b);
      continue;
    }
    if (route.data?.[TAB_DATA] === false) continue;
    const title = typeof route.title === 'string' ? simplifyTitle(route.title) : null;
    map.set(`/${pattern}`, title ?? `/${pattern}`);
  }
  return map;
}
