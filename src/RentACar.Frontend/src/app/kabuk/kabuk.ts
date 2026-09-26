import { DOCUMENT } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  Injector,
  linkedSignal,
  signal,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { branchLabel, type MenuResponse } from '@core/api/ui-tipleri';
import type { CeviriAnahtari } from '@core/i18n/ceviri-anahtarlari';
import { trUpperCase } from '@core/metin/tr-normalize';
import { PageLeave } from '@core/form/kaydedilmemis-degisiklik';
import { SessionService } from '@core/oturum/session-service';
import { FetchPolicy } from '@core/veri/fetch-policy';
import type { StoreState } from '@core/veri/temel-store';
import { Icon } from '@shared/ikon/icon';

import { activeEntry, type MenuKaydi, buildMenuModel } from './menu/menu-modeli';
import { MenuStore } from './menu/menu-store';
import { YanMenu, type SideMenuState } from './menu/yan-menu';
import { TabBar } from './sekmeler/tab-bar';
import { TabService } from './sekmeler/tab-service';
import { TopBar } from './ust-cubuk/top-bar';

/** Rozetler bu aralıkla tazelenir (sekme görünürken). */
const MENU_REFRESH_MS = 5 * 60_000;
/** Çekmece (mobil yan menü) bu genişlikte ve altında. `_kabuk` stilleriyle aynı. */
const MOBILE_QUERY = '(max-width: 900px)';
/** Daraltılmış kenar çubuğu (56 px ikon şeridi) kalıcı anahtarı. */
export const SHELL_NARROW_KEY = 'rc.kabuk.dar';
/** Okunmamış bildirim rozeti (sunucu `MenuKaydi.RozetOkunmamisBildirim`): üst çubuk zili bu öğeyi açar. */
const NOTIFICATION_BADGE = 'okunmamis-bildirim';
const ROLE_LABELS: Readonly<Record<string, CeviriAnahtari>> = {
  Admin: 'kabuk.rol.Admin',
  Yonetici: 'kabuk.rol.Yonetici',
  Operator: 'kabuk.rol.Operator',
  Muhasebe: 'kabuk.rol.Muhasebe',
};

/**
 * Uygulama kabuğu (F3.2, Blazor `MainLayout` yeniden yazıldı; Yol v2 §5 görünümü): lacivert kenar çubuğu
 * (`GET /api/ui/v1/menu`, 240 px ↔ 56 px ikon şeridi, `rc.kabuk.dar`), altta şube + kullanıcı kartı + çıkış;
 * üst çubuk (daralt, Ctrl+K paleti, plaka arama, bildirim zili, tema), sekmeli çalışma alanı. Oturum isteyen tüm sayfalar bunun içinde
 * (`sayfalar.ts`); giriş sayfası dışında.
 *
 * - SPA öğesi router ile; Blazor öğesi TAM SAYFA açılır (`/app` dışı) — önce tüm sekmelerde
 *   kaydedilmemiş değişiklik sorulur (tek soru), onaydan sonra `beforeunload` ikinci kez sormaz.
 * - Mobilde (≤ 900 px) yan menü çekmece: açılınca odak içine, arka plan `inert`, Esc/perde kapatır,
 *   odak menü düğmesine döner.
 */
@Component({
  selector: 'rc-kabuk',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RouterOutlet, TranslocoPipe, TabBar, TopBar, YanMenu],
  providers: [FetchPolicy, MenuStore],
  templateUrl: './kabuk.html',
  styleUrls: ['./kabuk.scss', './kabuk-yan-alt.scss'],
  host: { '(document:keydown)': 'shortcut($event)' },
})
export class Kabuk {
  private readonly router = inject(Router);
  private readonly oturum = inject(SessionService);
  private readonly leave = inject(PageLeave);
  private readonly injector = inject(Injector);
  private readonly window = inject(DOCUMENT).defaultView;
  private readonly store = inject(MenuStore);
  private readonly policy = inject(FetchPolicy);
  protected readonly tabs = inject(TabService);

  private readonly yanMenu = viewChild.required<ElementRef<HTMLElement>>('yanMenu');
  private readonly icerik = viewChild.required<ElementRef<HTMLElement>>('icerik');
  private readonly ustCubuk = viewChild.required(TopBar);

  /** Geçerli SPA yolu (sorgu/fragment yok) — etkin menü öğesi için. */
  private readonly path = signal(getPath(this.router.url));
  /** Geçerli adresin `gorunum` sorgusu — kira kayıtlı görünümünün işareti. */
  protected readonly gorunum = signal(getView(this.router.url));

  /** Son başarılı menü: yenileme hatasında eski menü kalır (hata BOŞ menü olarak gösterilmez). */
  private readonly lastMenu = linkedSignal<StoreState<MenuResponse>, MenuResponse | null>({
    source: this.store.menu.durum,
    computation: (status, previous) => {
      if (status.tur === 'hazir') return status.veri;
      if (status.tur === 'bos') return null;
      return previous?.value ?? null;
    },
  });
  protected readonly model = computed(() => {
    const menu = this.lastMenu();
    return menu ? buildMenuModel(menu) : null;
  });
  protected readonly menuState = computed<SideMenuState>(() => {
    const type = this.store.menu.tur();
    return type === 'hata' ? 'hata' : type === 'hazir' ? 'hazir' : 'yukleniyor';
  });
  protected readonly updateFailed = computed(
    () => this.store.menu.tur() === 'hata' && this.model() !== null,
  );
  protected readonly active = computed(() => {
    const model = this.model();
    return model ? activeEntry(model, this.path()) : null;
  });

  /** Üst çubuk zili: okunmamış bildirim öğesi (sunucu menüde gönderdiyse) + sayacı. */
  protected readonly bildirim = computed(() => {
    const model = this.model();
    const record = model?.tumu.find((k) => !k.hizli && k.rozetKodu === NOTIFICATION_BADGE);
    return model && record
      ? { kayit: record, sayi: model.rozetler.get(NOTIFICATION_BADGE) ?? 0 }
      : null;
  });

  /** Kenar çubuğu altı: kullanıcı kartı (baş harf avatarı, ad, rol · şube) ve firma. */
  protected readonly identity = computed(() => {
    const ben = this.oturum.ben();
    if (!ben) return null;
    const name = ben.kullanici.adSoyad || ben.kullanici.kullaniciAdi;
    return {
      ad: name,
      basHarf: initials(name),
      rol: ROLE_LABELS[ben.rol] ?? null,
      rolHam: ben.rol,
      sube: branchLabel(ben),
      firma: ben.kiraci.ad,
    };
  });

  protected readonly mobile = signal(false);
  protected readonly drawerOpen = signal(false);
  /** 56 px ikon şeridi (masaüstü); mobilde çekmece her zaman tam genişlik. */
  protected readonly narrow = signal(this.readNarrow());
  protected readonly narrowEnabled = computed(() => this.narrow() && !this.mobile());
  private isPaletteOpen = false;

  constructor() {
    this.policy.connect({
      parametre: signal<number>(0),
      yukle: (p) => this.store.menu.yukle(p),
      sifirla: () => this.store.menu.reset(),
    });

    const subscription = this.router.events.subscribe((evt) => {
      if (!(evt instanceof NavigationEnd)) return;
      this.path.set(getPath(evt.urlAfterRedirects));
      this.gorunum.set(getView(evt.urlAfterRedirects));
      if (this.drawerOpen()) this.closeDrawer(false);
    });

    const timer = this.window?.setInterval(() => {
      if (this.window?.document.visibilityState === 'visible') this.policy.yenile();
    }, MENU_REFRESH_MS);

    const query = this.window?.matchMedia?.(MOBILE_QUERY);
    const mobileChanged = (evt: { matches: boolean }) => {
      this.mobile.set(evt.matches);
      if (!evt.matches) this.drawerOpen.set(false);
    };
    if (query) {
      this.mobile.set(query.matches);
      query.addEventListener('change', mobileChanged);
    }

    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      if (timer !== undefined) this.window?.clearInterval(timer);
      query?.removeEventListener('change', mobileChanged);
    });
  }

  protected collapseMenu(): void {
    const newItem = !this.narrow();
    this.narrow.set(newItem);
    try {
      if (newItem) this.window?.localStorage.setItem(SHELL_NARROW_KEY, '1');
      else this.window?.localStorage.removeItem(SHELL_NARROW_KEY);
    } catch {
      // Depo kapalı: tercih yalnız bu oturumda.
    }
  }

  protected expandMenu(): void {
    if (this.narrow()) this.collapseMenu();
  }

  private readNarrow(): boolean {
    try {
      return this.window?.localStorage.getItem(SHELL_NARROW_KEY) === '1';
    } catch {
      return false;
    }
  }

  protected refreshMenu(): void {
    this.policy.yenile();
  }

  /** Menü / palet öğesi: SPA → router; Blazor → kirli form sorusu, sonra tam sayfa. */
  async openItem(record: MenuKaydi): Promise<void> {
    const target = record.hedef;
    if (target.tur === 'spa') {
      await this.router.navigateByUrl(target.yol);
      return;
    }
    if (!(await this.tabs.confirmLeave())) return;
    this.leave.goToFullPage(target.adres);
  }

  /** Üst çubuk zili: bildirim öğesini menüdeki gibi açar (Blazor ise kirli form sorusu). */
  protected async openNotifications(): Promise<void> {
    const b = this.bildirim();
    if (b) await this.openItem(b.kayit);
  }

  /** Çıkış: tüm sekmelerdeki kaydedilmemiş değişiklik TEK soruyla; onaylanınca guard yeniden sormaz. */
  protected async logout(): Promise<void> {
    if (!(await this.tabs.confirmLeave())) return;
    await this.leave.runConfirmed(() => this.oturum.logout());
  }

  /** Belge klavyesi: Ctrl+K / ⌘K paleti açar; Esc açık çekmeceyi kapatır (odak menü düğmesine). */
  protected shortcut(evt: KeyboardEvent): void {
    if (evt.key === 'Escape' && this.drawerOpen()) {
      this.closeDrawer(true);
      return;
    }
    const k = evt.key === 'k' || evt.key === 'K' || evt.code === 'KeyK';
    if (!k || !(evt.ctrlKey || evt.metaKey) || evt.altKey || evt.shiftKey) return;
    evt.preventDefault();
    void this.openPalette();
  }

  /** Ctrl+K paleti: diyalog ve palet bileşeni tembel yüklenir (ilk açılışta). */
  protected async openPalette(): Promise<void> {
    if (this.isPaletteOpen) return;
    this.isPaletteOpen = true;
    try {
      const { komutPaletiniAc: openCommandPalette } =
        await import('./komut-paleti/command-palette');
      const selected = await openCommandPalette(this.injector, this.model()?.tumu ?? []);
      if (selected) await this.openItem(selected);
    } finally {
      this.isPaletteOpen = false;
    }
  }

  protected openDrawer(): void {
    this.drawerOpen.set(true);
    afterNextRender(
      () => this.yanMenu().nativeElement.querySelector<HTMLElement>('button, a[href]')?.focus(),
      { injector: this.injector },
    );
  }

  protected closeDrawer(returnFocus: boolean): void {
    if (!this.drawerOpen()) return;
    this.drawerOpen.set(false);
    // İçerik sütunu `inert` iken odaklanamaz: öznitelik kalktıktan (çizimden) sonra.
    if (returnFocus) {
      afterNextRender(() => this.ustCubuk().focusMenuButton(), { injector: this.injector });
    }
  }

  /** Atlama bağlantısı: `<base href>` altında `#icerik` başka belgeye çözülür; odak elle taşınır. */
  protected skipToContent(evt: Event): void {
    evt.preventDefault();
    this.icerik().nativeElement.focus();
  }

  protected isComponentActive(component: unknown): void {
    this.tabs.setActiveComponent(component);
  }
}

function getPath(url: string): string {
  return url.split(/[?#]/, 1)[0] || '/';
}

function getView(url: string): string | null {
  const query = url.split('#', 1)[0]?.split('?')[1];
  return query ? new URLSearchParams(query).get('gorunum') : null;
}

/** "Ayşe Yılmaz" → "AY"; tek kelime → ilk iki harf. Türkçe büyük harf (i → İ). */
function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const letters =
    words.length > 1
      ? [...(words[0] ?? '')].slice(0, 1).join('') +
        [...(words[words.length - 1] ?? '')].slice(0, 1).join('')
      : [...(words[0] ?? '')].slice(0, 2).join('');
  return trUpperCase(letters);
}
